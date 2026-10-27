"""``POST /api/v1/chat/parse``: xác thực, quy đổi sang slug backend, và Rasa tắt thì vẫn chạy bằng luật."""

import httpx
import pytest

from app.api.v1 import chat as chat_route
from app.api.v1.bill import get_bill_knowledge
from app.services.nlu import RasaNlu
from app.core.config import settings
from app.core.enums import CATEGORY_SLUGS
from app.main import app

HASH = "a" * 64


class FakeKnowledge:
    """Bảng nhớ tên món giả: đếm số lần được hỏi để biết route chỉ hỏi khi cần."""

    def __init__(self, cache=None, fail=False):
        self.cache, self.fail, self.calls, self.refreshes = dict(cache or {}), fail, [], 0

    async def refresh_if_stale(self):
        self.refreshes += 1

    async def cached_items(self, keys):
        self.calls.append(list(keys))
        if self.fail:
            raise RuntimeError("db down")
        return {k: self.cache[k] for k in keys if k in self.cache}


@pytest.fixture(autouse=True)
def no_rasa():
    chat_route.set_nlu(None)
    app.dependency_overrides[get_bill_knowledge] = lambda: FakeKnowledge()  # test đơn vị không đụng DB
    yield
    app.dependency_overrides.pop(get_bill_knowledge, None)
    chat_route.set_nlu(None)


async def post(text, headers=True, **extra):
    transport = httpx.ASGITransport(app=app)  # không chạy lifespan nên không cần Postgres
    auth = {"Authorization": f"Bearer {settings.internal_api_key}"} if headers else {}
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        return await client.post("/api/v1/chat/parse", json={"text": text, "user_id_hash": HASH, **extra}, headers=auth)


async def test_missing_key_is_rejected():
    assert (await post("đi ăn 200k", headers=False)).status_code == 401


async def test_expense_becomes_a_backend_slug():
    body = (await post("đi ăn 200k")).json()
    assert body["intent"] == "add_expense" and body["nlu"]["source"] == "rules"
    entry = body["entry"]
    assert (entry["kind"], entry["amount_cents"], entry["category_slug"], entry["auto_save_ok"]) == ("expense", 200_000, "food", True)
    assert entry["category_slug"] in CATEGORY_SLUGS


@pytest.mark.parametrize(
    ("text", "slug"),
    [("đóng tiền wifi 250k", "bills"), ("khám bệnh 300k", "health"), ("đi grab 50k", "transport"), ("đi chợ 150k", "food"), ("tiền nhà 3 củ 8", "housing")],
)
async def test_every_category_is_a_valid_backend_slug(text, slug):
    assert (await post(text)).json()["entry"]["category_slug"] == slug


async def test_income_is_the_single_income_slug_with_its_type():
    entry = (await post("nhận lương 14 củ 5")).json()["entry"]
    assert (entry["kind"], entry["category_slug"], entry["income_type"], entry["auto_save_ok"]) == ("income", "income", "salary", False)


@pytest.mark.parametrize(
    ("text", "kind"),
    [("cho bạn mượn 500k", "expense"), ("trả nợ cho bạn 300k", "expense"), ("bạn trả nợ 500k", "income"), ("bạn cho tôi mượn 300k", "income")],
)
async def test_lending_borrowing_and_repaying_all_use_the_loan_slug_in_both_directions(text, kind):
    """"loan" là danh mục hệ thống của backend nhưng không thuộc CATEGORY_SLUGS (ràng buộc CHECK của DB AI): route vẫn phải trả nó."""
    entry = (await post(text)).json()["entry"]
    assert (entry["kind"], entry["category_slug"], entry["auto_save_ok"]) == (kind, "loan", False)
    assert "loan" not in CATEGORY_SLUGS


async def test_a_question_about_two_categories_lists_both_and_one_category_stays_as_before():
    query = (await post("tháng này tôi đã tiêu bao nhiêu tiền cho giải trí và mua sắm")).json()["query"]
    assert query["category_slugs"] == ["entertainment", "shopping"] and query["category_slug"] == "entertainment"
    one = (await post("tháng này tôi tiêu bao nhiêu cho ăn uống")).json()["query"]
    assert one["category_slug"] == "food" and one["category_slugs"] == []


async def test_a_goal_sentence_returns_a_goal_draft_and_no_entry():
    body = (await post("đặt mục tiêu vào 2 tháng sau tôi sẽ mua được xe máy khoảng 50 triệu")).json()
    assert body["intent"] == "create_goal" and body["entry"] is None and body["entries"] == []
    goal = body["goal"]
    assert (goal["name"], goal["name_is_default"], goal["target_cents"], goal["deadline_text"]) == ("Mua xe máy", False, 50_000_000, "2 tháng sau")
    assert goal["deadline"].endswith("23:59:59+07:00")


async def test_a_goal_sentence_without_an_amount_asks_back_with_no_draft():
    body = (await post("đặt mục tiêu tiết kiệm")).json()
    assert body["intent"] == "create_goal" and body["goal"] is None and "bao nhiêu" in body["reply_text"]


async def test_transfer_has_no_category():
    entry = (await post("chuyển 2 triệu sang tiết kiệm")).json()["entry"]
    assert entry["kind"] == "transfer" and entry["category_slug"] is None


async def test_unknown_category_is_null_not_other():
    entry = (await post("250k")).json()["entry"]
    assert entry["category_slug"] is None and entry["auto_save_ok"] is False


async def test_the_dictionary_fills_a_merchant_the_chat_vocabulary_does_not_know():
    entry = (await post("ghé highlands 55k")).json()["entry"]
    assert entry["category_slug"] == "food" and entry["auto_save_ok"] is True


async def test_total_question_returns_a_query_not_numbers():
    body = (await post("tháng này tôi tiêu bao nhiêu")).json()
    assert body["intent"] == "ask_spending" and body["entry"] is None
    assert (body["query"]["metric"], body["query"]["period_key"]) == ("expense", "thang nay")
    assert body["query"]["start"].endswith("+07:00")


async def test_text_is_capped_and_hash_is_required():
    assert (await post("a" * 501)).status_code == 422
    assert (await post("đi ăn 200k", user_id_hash="short")).status_code == 422


async def test_rasa_is_used_when_available_and_ignored_when_down():
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"intent": {"name": "add_expense", "confidence": 0.97}, "entities": []})

    chat_route.set_nlu(RasaNlu("http://rasa:5005", client=httpx.AsyncClient(transport=httpx.MockTransport(handler))))
    body = (await post("đi ăn 200k")).json()
    assert body["nlu"] == {"source": "rasa", "intent": "add_expense", "confidence": 0.97}

    def down(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("down")

    chat_route.set_nlu(RasaNlu("http://rasa:5005", client=httpx.AsyncClient(transport=httpx.MockTransport(down))))
    body = (await post("đi ăn 200k")).json()
    assert body["nlu"]["source"] == "rules" and body["entry"]["category_slug"] == "food"


async def test_rasa_circuit_breaker_stops_calling_after_a_failure():
    calls = []

    def down(request: httpx.Request) -> httpx.Response:
        calls.append(1)
        raise httpx.ConnectError("down")

    nlu = RasaNlu("http://rasa:5005", client=httpx.AsyncClient(transport=httpx.MockTransport(down)))
    assert await nlu.parse("a") is None and await nlu.parse("b") is None
    assert len(calls) == 1


async def test_response_carries_entries_and_the_date():
    body = (await post("hôm qua ăn phở 45k, đổ xăng 30k")).json()
    assert [e["amount_cents"] for e in body["entries"]] == [45_000, 30_000]
    assert body["entry"] == body["entries"][0] and body["entries"][0]["occurred_at"] is not None
    assert all(not e["auto_save_ok"] for e in body["entries"])


async def test_single_entry_is_also_listed_and_has_no_date():
    body = (await post("đi ăn 200k")).json()
    assert body["entries"] == [body["entry"]] and body["entry"]["occurred_at"] is None


@pytest.mark.parametrize(
    ("text", "slug"),
    [
        ("uống nước 10k", "food"),
        ("mua 2 lít sữa 50k", "food"),
        ("mua sữa bột 400k", "family"),
        ("đóng học phí 3 triệu", "education"),
        ("đóng tiền nước 200k", "housing"),
        ("sửa nhà 2 triệu", None),  # "sửa" bỏ dấu thành "sua": không được kéo sang sữa
        ("uống thuốc 50k", "health"),
        ("cắt tóc 80k", "other"),  # từ khóa nằm cạnh học phí trong bảng cũ: đừng làm rơi khi chuyển học phí sang education
    ],
)
async def test_ambiguous_words_resolve_by_context_and_otherwise_stay_unknown(text, slug):
    assert (await post(text)).json()["entry"]["category_slug"] == slug


@pytest.mark.parametrize("text", ["mua nước 20k", "20k"])
async def test_unknown_category_is_never_auto_saved(text):
    entry = (await post(text)).json()["entry"]
    assert entry["auto_save_ok"] is False
    if text == "mua nước 20k":
        assert entry["category_slug"] is None


async def test_amount_only_has_no_description():
    entry = (await post("20k")).json()["entry"]
    assert entry["amount_cents"] == 20_000 and entry["description"] is None


async def test_bank_transfer_is_a_transfer_and_never_auto_saved():
    entry = (await post("chuyển khoản 200k")).json()["entry"]
    assert entry["kind"] == "transfer" and entry["auto_save_ok"] is False and entry["category_slug"] is None


async def test_a_leading_minus_does_not_make_the_amount_negative():
    entry = (await post("-50k ăn cơm")).json()["entry"]
    assert (entry["kind"], entry["amount_cents"]) == ("expense", 50_000)


@pytest.mark.parametrize("text", ["ăn cơm", "mua nước", "đi ăn", "uống cà phê"])
async def test_an_expense_phrase_without_a_number_asks_for_the_amount(text):
    assert (await post(text)).json()["intent"] == "needs_amount"


@pytest.mark.parametrize("text", ["ăn gì bây giờ?", "xin chào", "thời tiết hôm nay thế nào", "nên mua nhà hay thuê nhà"])
async def test_questions_and_small_talk_do_not_ask_for_an_amount(text):
    assert (await post(text)).json()["intent"] != "needs_amount"


def use_cache(**cache):
    fake = FakeKnowledge({k.replace("_", " "): v for k, v in cache.items()})
    app.dependency_overrides[get_bill_knowledge] = lambda: fake
    return fake


async def test_cached_category_fills_an_unknown_phrase_but_never_auto_saves():
    fake = use_cache(zxqv_gizmo="shopping")
    entry = (await post("zxqv gizmo 50k")).json()["entry"]
    assert entry["category_slug"] == "shopping" and entry["auto_save_ok"] is False
    assert fake.calls == [["zxqv gizmo"]]


async def test_a_rule_hit_never_consults_the_cache():
    fake = use_cache(pho_bo="shopping")
    entry = (await post("ăn phở bò 45k")).json()["entry"]
    assert entry["category_slug"] == "food" and fake.calls == []


async def test_income_and_transfers_never_consult_the_cache():
    fake = use_cache()
    await post("nhận lương 12 triệu")
    await post("chuyển khoản 200k")
    assert fake.calls == []


async def test_every_chat_parse_refreshes_the_db_keywords_even_without_ai():
    """Từ khóa/thương hiệu sửa trong DB phải đến được chat: trước đây chỉ các route hóa đơn nạp lại chúng."""
    fake = use_cache()
    await post("ăn phở bò 45k")  # allow_ai mặc định False
    await post("nhận lương 12 triệu")
    assert fake.refreshes == 2


async def test_chat_uses_keywords_loaded_from_the_db():
    """Qua DbBillKnowledge thật: hàng trong kb_keywords thay bảng trong image và chat phân loại theo nó."""
    from app.bill import classifier
    from app.bill.knowledge import DbBillKnowledge
    from app.services import category_resolver

    class Rows:
        def __init__(self, rows):
            self.rows = rows

        def all(self):
            return self.rows

    class Session:
        async def __aenter__(self):
            return self

        async def __aexit__(self, *exc):
            return False

        async def execute(self, statement):
            return Rows([("product", "shopping", "zxqvgizmo")] if "kb_keywords" in str(statement) else [])

    saved_product, saved_merchant = dict(classifier._PRODUCT_KEYWORDS), dict(classifier._CATEGORY_KEYWORDS)
    app.dependency_overrides[get_bill_knowledge] = lambda: DbBillKnowledge(session_factory=Session)
    try:
        assert category_resolver.resolve("zxqvgizmo", chat=True).slug is None  # chưa nạp thì bảng trong image không biết từ này
        assert (await post("zxqvgizmo 50k")).json()["entry"]["category_slug"] == "shopping"
    finally:
        classifier._PRODUCT_KEYWORDS.clear(), classifier._PRODUCT_KEYWORDS.update(saved_product)
        classifier._CATEGORY_KEYWORDS.clear(), classifier._CATEGORY_KEYWORDS.update(saved_merchant)
        category_resolver.invalidate()


async def test_cache_failure_is_harmless():
    app.dependency_overrides[get_bill_knowledge] = lambda: FakeKnowledge(fail=True)
    response = await post("zxqv gizmo 50k")
    assert response.status_code == 200 and response.json()["entry"]["category_slug"] is None


async def test_every_unknown_entry_of_a_multi_entry_sentence_is_looked_up_once():
    fake = use_cache(zxqv_gizmo="shopping", qwop_widget="family")
    body = (await post("zxqv gizmo 50k, qwop widget 30k, ăn phở 45k")).json()
    assert [e["category_slug"] for e in body["entries"]] == ["shopping", "family", "food"]
    assert len(fake.calls) == 1 and sorted(fake.calls[0]) == ["qwop widget", "zxqv gizmo"]


async def test_a_cached_slug_outside_the_taxonomy_is_ignored():
    use_cache(zxqv_gizmo="income")
    assert (await post("zxqv gizmo 50k")).json()["entry"]["category_slug"] is None


async def test_stated_total_is_returned_for_checking():
    body = (await post("mua bánh mì 25k và đổ xăng 35k tổng cộng 70k")).json()
    assert [e["amount_cents"] for e in body["entries"]] == [25_000, 35_000]
    assert (body["total_amount"], body["total_mismatch"]) == (70_000, True)


async def test_category_source_tells_rule_from_cache_from_unknown():
    use_cache(zxqv_gizmo="shopping")
    rule = (await post("ăn phở bò 45k")).json()["entry"]
    cached = (await post("zxqv gizmo 50k")).json()["entry"]
    unknown = (await post("qwrt plugh 50k")).json()["entry"]
    assert (rule["category_source"], cached["category_source"], unknown["category_source"]) == ("rule", "cache", None)
