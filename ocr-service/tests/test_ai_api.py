"""Máy khách AI Service (storage/ai_api.py): đúng đường dẫn, đúng khóa, lỗi thành AiApiError; và AI Service giả (fake_ai_api.py) không lệch khỏi máy khách thật."""
import inspect
import pathlib
import sys

import httpx
import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from storage.ai_api import AiApi, AiApiError, api_from_env, who  # noqa: E402
from tests.fake_ai_api import FakeAiApi  # noqa: E402


def _api(handler):
    transport = httpx.MockTransport(handler)
    client = httpx.Client(base_url="http://ai", headers={"Authorization": "Bearer k"}, transport=transport)
    return AiApi("http://ai", "k", client=client)


def test_fake_has_the_same_methods_and_signatures_as_the_real_client():
    public = lambda cls: {n: f for n, f in inspect.getmembers(cls, inspect.isfunction) if not n.startswith("_")}  # noqa: E731
    real, fake = public(AiApi), public(FakeAiApi)
    missing = set(real) - set(fake)
    assert not missing, f"FakeAiApi thiếu: {missing}"
    for name, function in real.items():
        assert list(inspect.signature(function).parameters) == list(inspect.signature(fake[name]).parameters), name


def test_requests_carry_the_path_params_and_body():
    seen = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append((request.method, request.url.path, dict(request.url.params), request.content))
        return httpx.Response(200, json={"items": [], "created": True, "result": "marked", "updated": 2, "job_id": "j1"})

    api = _api(handler)
    assert api.knowledge(True) == {"items": [], "created": True, "result": "marked", "updated": 2, "job_id": "j1"}
    assert api.propose_brand({"brand_key": "x"}) is True
    assert api.claim_gold(100, "dining", dry_run=True) == "marked"
    assert api.rename_gold("a", "b") == 2
    assert api.start_training("bill_category", {}) == "j1"
    assert seen[0][:3] == ("GET", "/api/v1/ocr-store/knowledge", {"include_inactive": "true"})
    assert seen[1][:2] == ("POST", "/api/v1/ocr-store/brands/vote")
    assert b'"dry_run":true' in seen[2][3].replace(b" ", b"")
    assert seen[4][:2] == ("POST", "/api/v1/ocr-store/training/jobs")


def test_labeled_samples_are_read_page_by_page():
    pages = {0: [{"i": 0}, {"i": 1}], 2: [{"i": 2}]}

    def handler(request: httpx.Request) -> httpx.Response:
        offset = int(request.url.params["offset"])
        return httpx.Response(200, json={"items": pages.get(offset, []), "total": 3})

    assert _api(handler).list_labeled() == [{"i": 0}, {"i": 1}, {"i": 2}]


def test_http_error_becomes_ai_api_error_with_code():
    api = _api(lambda r: httpx.Response(422, json={"error": {"code": "invalid_category", "message": "sai nhóm"}}))
    with pytest.raises(AiApiError) as caught:
        api.put_merchant_memory("a" * 64, "k", None, "income")
    assert (caught.value.status, caught.value.code) == (422, "invalid_category")


def test_network_failure_becomes_ai_api_error():
    def handler(request):
        raise httpx.ConnectError("down")

    with pytest.raises(AiApiError) as caught:
        _api(handler).knowledge()
    assert caught.value.status == 0 and caught.value.code == "unreachable"


def test_config_document_is_zero_until_an_admin_activates_one():
    assert _api(lambda r: httpx.Response(200, json={"source": "default"})).config_document("thresholds") == (0, None)
    assert _api(lambda r: httpx.Response(200, json={"source": "db", "version": 3, "content": {"a": 1}})).config_document("thresholds") == (3, {"a": 1})


def test_api_from_env_needs_both_url_and_key(monkeypatch):
    monkeypatch.setenv("AI_SERVICE_URL", "http://ai")
    monkeypatch.delenv("AI_SERVICE_API_KEY", raising=False)
    assert api_from_env() is None
    monkeypatch.setenv("AI_SERVICE_API_KEY", "k")
    assert isinstance(api_from_env(), AiApi)


def test_user_codes_are_always_sha256_hex():
    assert len(who("local")) == 64 and who("local") == who("local")
    assert who("a" * 64) == "a" * 64
