"""``POST /api/v1/finance-methods/recommend`` với mô hình giả (và với ``GeminiClient`` thật chạy trên ``MockTransport``): khóa nội bộ, hợp đồng, mã lỗi, không log dữ liệu."""

import json

import httpx
import pytest
import structlog

from app.api.v1.finance_methods import get_method_backup_picker, get_method_gemini_client, get_method_prompt
from app.bill import gemini as gemini_module
from app.bill.gemini import GeminiClient, GeminiFailed
from app.core.config import settings
from app.main import app
from app.services import method_backup
from app.services.method_backup import OpenRouterPicker
from app.services.method_picker import MethodPromptRow

URL = "/api/v1/finance-methods/recommend"
SECRET_SIGNAL = "tight_cash_flow_zq"
SECRET_SUMMARY = "mo-ta-rieng-cua-phuong-phap"
METHODS = [
    {"code": "category_budget", "name": "Ngân sách theo danh mục", "summary": SECRET_SUMMARY, "role": "primary"},
    {"code": "available_to_spend", "name": "Số tiền được tiêu", "summary": "Một con số mỗi ngày.", "role": "both"},
    {"code": "goal_fund", "name": "Quỹ mục tiêu", "summary": "Để dành dần.", "role": "supporting"},
]
BODY = {"signals": [{"key": "income_variable", "weight": 3}, {"key": SECRET_SIGNAL, "weight": 2}], "methods": METHODS}
GOOD = {"primary": {"code": "available_to_spend", "reason": "Thu nhập của bạn thất thường nên một con số mỗi ngày dễ theo hơn."},
        "support": {"code": "goal_fund", "reason": "Bạn đã có mục tiêu rõ ràng."}}


class FakeGemini:
    def __init__(self, reply=None, error=None):
        self.reply, self.error, self.prompts, self.schemas = reply, error, [], []

    async def generate_text(self, prompt, schema):
        self.prompts.append(prompt)
        self.schemas.append(schema)
        if self.error:
            raise self.error
        return self.reply if isinstance(self.reply, str) else json.dumps(self.reply, ensure_ascii=False)


@pytest.fixture(autouse=True)
def prompt_row():
    app.dependency_overrides[get_method_prompt] = lambda: MethodPromptRow("test-v7", "CHI-DAN-TU-DB")
    app.dependency_overrides[get_method_backup_picker] = lambda: OpenRouterPicker(None, (), 5.0)  # mặc định TẮT; test dự phòng tự bật bằng use_backup
    yield
    for dep in (get_method_prompt, get_method_gemini_client, get_method_backup_picker):
        app.dependency_overrides.pop(dep, None)


def use(gemini):
    app.dependency_overrides[get_method_gemini_client] = lambda: gemini
    return gemini


async def post(body=BODY, auth=True):
    headers = {"Authorization": f"Bearer {settings.internal_api_key}"} if auth else {}
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        return await client.post(URL, json=body, headers=headers)


def error_of(res):
    return res.json()["error"]


async def test_requires_internal_key():
    use(FakeGemini(GOOD))
    assert (await post(auth=False)).status_code == 401


async def test_returns_primary_support_and_prompt_version():
    fake = use(FakeGemini(GOOD))
    res = await post()
    assert res.status_code == 200
    data = res.json()
    assert set(data) == {"primary", "support", "prompt_version", "processing_ms"}
    assert data["primary"] == GOOD["primary"] and data["support"] == GOOD["support"]
    assert data["prompt_version"] == "test-v7"
    prompt = fake.prompts[0]
    assert "CHI-DAN-TU-DB" in prompt and SECRET_SIGNAL in prompt and SECRET_SUMMARY in prompt  # chỉ dẫn từ DB + dữ liệu đều vào prompt
    assert fake.schemas[0]["properties"]["primary"]["properties"]["code"]["enum"] == ["category_budget", "available_to_spend"]


async def test_support_is_null_when_model_chooses_none():
    use(FakeGemini({"primary": GOOD["primary"], "support": None}))
    res = await post()
    assert res.status_code == 200 and res.json()["support"] is None


@pytest.mark.parametrize("extra", [{"user_id": "u-1"}, {"user_id_hash": "a" * 64}])
async def test_identity_fields_are_refused(extra):
    fake = use(FakeGemini(GOOD))
    res = await post({**BODY, **extra})
    assert res.status_code == 422 and fake.prompts == []  # không có gì đi ra bên thứ ba


async def test_no_primary_candidate_is_422_and_nothing_is_sent():
    fake = use(FakeGemini(GOOD))
    only_support = [{**METHODS[2]}]
    res = await post({**BODY, "methods": only_support})
    assert res.status_code == 422 and error_of(res)["code"] == "no_primary_candidate" and fake.prompts == []


async def test_missing_prompt_is_503_prompt_missing():
    app.dependency_overrides[get_method_prompt] = lambda: None
    fake = use(FakeGemini(GOOD))
    res = await post()
    assert res.status_code == 503 and error_of(res)["code"] == "prompt_missing" and fake.prompts == []


async def test_provider_failure_is_503_ai_unavailable():
    use(FakeGemini(error=GeminiFailed("http_503")))
    res = await post()
    assert res.status_code == 503
    assert error_of(res) == {"code": "ai_unavailable", "message": "Mô hình AI không phản hồi được.", "details": ["http_503"]}


@pytest.mark.parametrize(
    "reply,reason",
    [
        ({"primary": {"code": "tiet_kiem_vang", "reason": "x"}}, "unknown_code"),
        ({"primary": {"code": "category_budget", "reason": "x"}, "support": {"code": "category_budget", "reason": "y"}}, "same_method"),
        ({"primary": {"code": "category_budget", "reason": "x"},
          "support": [{"code": "goal_fund", "reason": "y"}, {"code": "available_to_spend", "reason": "z"}]}, "bad_shape"),
        ("```json {oops", "bad_json"),
        ({"primary": {"code": "goal_fund", "reason": "x"}}, "wrong_role"),
        ({"primary": {"code": "category_budget", "reason": ""}}, "bad_reason"),
    ],
)
async def test_invalid_model_output_is_502_and_never_repaired(reply, reason):
    use(FakeGemini(reply))
    res = await post()
    assert res.status_code == 502
    assert error_of(res)["code"] == "ai_invalid_output" and error_of(res)["details"] == [reason]
    assert "primary" not in res.json()  # không có kết quả một phần để backend dùng nhầm


async def test_logs_never_contain_signals_methods_codes_or_reasons():
    reason = "ly-do-rieng-tu-khong-duoc-log"
    use(FakeGemini({"primary": {"code": "available_to_spend", "reason": reason}}))
    with structlog.testing.capture_logs() as ok_logs:
        assert (await post()).status_code == 200
    use(FakeGemini({"primary": {"code": "tiet_kiem_vang", "reason": reason}}))
    with structlog.testing.capture_logs() as bad_logs:
        assert (await post()).status_code == 502
    dumped = json.dumps(ok_logs + bad_logs, ensure_ascii=False)
    assert ok_logs and bad_logs
    for secret in (SECRET_SIGNAL, SECRET_SUMMARY, reason, "available_to_spend", "tiet_kiem_vang", "income_variable", "CHI-DAN-TU-DB"):
        assert secret not in dumped


# ---- GeminiClient thật trên MockTransport: chuỗi model, thử lại, cầu dao, không đi nhà cung cấp dự phòng ----


@pytest.fixture
def no_sleep(monkeypatch):
    async def fake_sleep(_):
        return None

    monkeypatch.setattr(gemini_module.asyncio, "sleep", fake_sleep)


class Clock:
    def __init__(self):
        self.now = 1000.0

    def __call__(self):
        return self.now


def real(handler, clock=None, key="key"):
    return use(GeminiClient(key, "m1", 5.0, transport=httpx.MockTransport(handler), clock=clock or Clock(), fallback_models=("m2",), backup_key="openrouter-key", backup_model="x/y"))


def reply(payload):
    text = payload if isinstance(payload, str) else json.dumps(payload)
    return httpx.Response(200, json={"candidates": [{"content": {"parts": [{"text": text}]}}]})


async def test_real_client_falls_back_to_next_model(no_sleep):
    seen = []

    def handler(request):
        seen.append(str(request.url))
        return httpx.Response(503) if "m1:" in str(request.url) else reply(GOOD)

    real(handler)
    res = await post()
    assert res.status_code == 200
    assert [("m1:" in u, "m2:" in u) for u in seen] == [(True, False), (False, True)]
    sent = [u for u in seen if "openrouter" in u]
    assert sent == []  # dự phòng thứ hai (dù có cấu hình khóa) KHÔNG dùng cho route này


async def test_real_client_asks_for_json_and_schema_and_nothing_identifying():
    bodies = []

    def handler(request):
        bodies.append(json.loads(request.content))
        return reply(GOOD)

    real(handler)
    assert (await post()).status_code == 200
    config = bodies[0]["generationConfig"]
    assert config["responseMimeType"] == "application/json" and config["temperature"] == 0 and "enum" in json.dumps(config["responseSchema"])
    assert "user" not in json.dumps(bodies[0]["contents"]).lower().replace("signals", "")


async def test_real_client_retries_last_model_once_on_429(no_sleep):
    calls = []

    def handler(request):
        calls.append(str(request.url))
        return httpx.Response(500) if "m1:" in str(request.url) else (httpx.Response(429) if len(calls) < 3 else reply(GOOD))

    real(handler)
    assert (await post()).status_code == 200
    assert [("m1:" in u) for u in calls] == [True, False, False]  # m1 lỗi → m2 gặp 429 → m2 thử lại một lần


async def test_all_models_failing_opens_the_circuit(no_sleep):
    calls = []

    def handler(request):
        calls.append(1)
        return httpx.Response(500)

    clock = Clock()
    real(handler, clock)
    first = await post()
    assert first.status_code == 503 and error_of(first)["code"] == "ai_unavailable"
    reached = len(calls)
    second = await post()  # cầu dao mở 30 giây: không gọi ra ngoài nữa
    assert second.status_code == 503 and len(calls) == reached
    clock.now += gemini_module.CIRCUIT_SECONDS + 1
    await post()
    assert len(calls) > reached  # hết 30 giây thì thử lại


async def test_real_client_invalid_json_does_not_open_the_circuit_or_retry():
    calls = []

    def handler(request):
        calls.append(1)
        return reply("không phải json")

    real(handler)
    res = await post()
    assert res.status_code == 502 and error_of(res)["details"] == ["bad_json"] and len(calls) == 1  # không hỏi lại model khác
    assert (await post()).status_code == 502 and len(calls) == 2  # cầu dao vẫn đóng: lỗi do nội dung, không phải do nhà cung cấp


async def test_missing_api_key_is_ai_unavailable():
    real(lambda request: reply(GOOD), key=None)
    res = await post()
    assert res.status_code == 503 and error_of(res)["details"] == ["unavailable"]


# ---- dự phòng thứ hai: OpenRouter (chỉ khi Gemini hết cách) ----

OR_MODELS = ("free/model-a:free", "free/model-b:free")


def use_backup(handler, models=OR_MODELS, clock=None):
    picker = OpenRouterPicker("or-key", models, 5.0, transport=httpx.MockTransport(handler), clock=clock or Clock())
    app.dependency_overrides[get_method_backup_picker] = lambda: picker
    return picker


def or_reply(payload):
    text = payload if isinstance(payload, str) else json.dumps(payload)
    return httpx.Response(200, json={"choices": [{"message": {"content": text}}]})


async def test_backup_answers_when_gemini_is_out_of_options():
    bodies = []

    def handler(request):
        assert str(request.url) == "https://openrouter.ai/api/v1/chat/completions" and request.headers["authorization"] == "Bearer or-key"
        bodies.append(json.loads(request.content))
        return or_reply(GOOD)

    use(FakeGemini(error=GeminiFailed("http_503")))
    use_backup(handler)
    res = await post()
    assert res.status_code == 200 and res.json()["primary"] == GOOD["primary"] and res.json()["prompt_version"] == "test-v7"
    body = bodies[0]
    assert body["model"] == "free/model-a:free" and body["temperature"] == 0
    assert body["provider"] == {"data_collection": "deny"}, "model miễn phí chỉ được dùng khi bên cung cấp cam kết không lưu/huấn luyện"
    assert body["reasoning"] == {"enabled": False}
    schema = body["response_format"]["json_schema"]
    assert body["response_format"]["type"] == "json_schema" and schema["strict"] is True
    assert schema["schema"]["properties"]["primary"]["properties"]["code"]["enum"] == ["category_budget", "available_to_spend"]
    assert schema["schema"]["required"] == ["primary", "support"]
    prompt = body["messages"][0]["content"]
    assert "CHI-DAN-TU-DB" in prompt and SECRET_SIGNAL in prompt and "user_id" not in prompt  # cùng dữ liệu ẩn danh như gửi Gemini


async def test_backup_is_never_called_when_gemini_answers():
    calls = []
    use(FakeGemini(GOOD))
    use_backup(lambda request: calls.append(1) or or_reply(GOOD))
    assert (await post()).status_code == 200 and calls == []


async def test_backup_tries_next_model_when_a_model_has_no_endpoint_under_the_data_policy():
    seen = []

    def handler(request):
        model = json.loads(request.content)["model"]
        seen.append(model)
        if model == "free/model-a:free":
            return httpx.Response(404, json={"error": {"message": "No endpoints found matching your data policy"}})
        return or_reply(GOOD)

    use(FakeGemini(error=GeminiFailed("overall_timeout")))
    use_backup(handler)
    res = await post()
    assert res.status_code == 200 and seen == ["free/model-a:free", "free/model-b:free"]


async def test_both_providers_failing_is_503_with_both_reasons_and_opens_the_backup_circuit():
    calls = []

    def handler(request):
        calls.append(1)
        return httpx.Response(429)

    clock = Clock()
    use(FakeGemini(error=GeminiFailed("http_503")))
    use_backup(handler, clock=clock)
    first = await post()
    assert first.status_code == 503 and error_of(first) == {
        "code": "ai_unavailable", "message": "Mô hình AI không phản hồi được.", "details": ["http_503", "backup_http_429"]}
    assert len(calls) == 2  # hai model đều bị thử
    second = await post()
    assert second.status_code == 503 and error_of(second)["details"] == ["http_503", "backup_unavailable"] and len(calls) == 2  # cầu dao 30 giây
    clock.now += method_backup.CIRCUIT_SECONDS + 1
    await post()
    assert len(calls) > 2


async def test_invalid_backup_output_is_502_and_never_repaired():
    use(FakeGemini(error=GeminiFailed("http_503")))
    use_backup(lambda request: or_reply({"primary": {"code": "tiet_kiem_vang", "reason": "x"}, "support": None}))
    res = await post()
    assert res.status_code == 502 and error_of(res)["details"] == ["unknown_code"]


async def test_backup_accepts_support_null_as_the_strict_schema_requires():
    use(FakeGemini(error=GeminiFailed("http_503")))
    use_backup(lambda request: or_reply({"primary": GOOD["primary"], "support": None}))
    res = await post()
    assert res.status_code == 200 and res.json()["support"] is None


async def test_backup_is_off_without_key_or_models():
    assert OpenRouterPicker(None, OR_MODELS, 5.0).enabled is False
    assert OpenRouterPicker("k", (), 5.0).enabled is False
    use(FakeGemini(error=GeminiFailed("http_503")))
    res = await post()  # fixture: dự phòng tắt → giữ nguyên hành vi cũ
    assert res.status_code == 503 and error_of(res)["details"] == ["http_503"]


async def test_backup_respects_the_remaining_time_budget():
    calls = []
    clock = Clock()
    picker = OpenRouterPicker("k", OR_MODELS, 5.0, transport=httpx.MockTransport(lambda r: calls.append(1) or or_reply(GOOD)), clock=clock)
    with pytest.raises(method_backup.BackupFailed) as info:
        await picker.generate("p", {}, budget_seconds=method_backup.MIN_BUDGET_SECONDS - 0.1)
    assert info.value.args[0] == "backup_no_budget" and calls == []  # còn quá ít thời gian thì không bắt đầu một lần gọi mới


async def test_backup_timeout_is_a_fixed_code():
    import asyncio

    async def slow(request):
        await asyncio.sleep(1)
        return or_reply(GOOD)

    picker = OpenRouterPicker("k", ("free/model-a:free",), 0.05, transport=httpx.MockTransport(slow))
    with pytest.raises(method_backup.BackupFailed) as info:
        await picker.generate("p", {}, budget_seconds=10)
    assert info.value.args[0] == "backup_timeout"


async def test_backup_path_logs_nothing_sensitive():
    reason = "ly-do-rieng-tu-khong-duoc-log"
    use(FakeGemini(error=GeminiFailed("http_503")))
    use_backup(lambda request: or_reply({"primary": {"code": "available_to_spend", "reason": reason}, "support": None}))
    with structlog.testing.capture_logs() as logs:
        assert (await post()).status_code == 200
    dumped = json.dumps(logs, ensure_ascii=False)
    assert logs and "openrouter" in dumped
    for secret in (SECRET_SIGNAL, SECRET_SUMMARY, reason, "available_to_spend", "CHI-DAN-TU-DB", "or-key"):
        assert secret not in dumped
