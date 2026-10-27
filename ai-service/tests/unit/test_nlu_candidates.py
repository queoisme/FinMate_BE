"""Câu chat chưa rõ gửi về để người duyệt: bộ lọc dữ liệu cá nhân và hai route ``/chat/unclear`` (không cần DB)."""

import httpx
import pytest

from app.api.v1.bill import get_session_factory
from app.core.config import settings
from app.main import app
from app.services import nlu_candidates

HASH = "b" * 64


@pytest.mark.parametrize(
    ("text", "reason"),
    [
        ("ghi giúp mình khoản này", None),
        ("   ", "empty"),
        ("a" * 201, "too_long"),
        ("dòng 1\ndòng 2", "too_long"),
        ("ăn phở [45k](amount)", "brackets"),  # cú pháp thực thể của Rasa: câu người dùng không được mang theo
        ("gọi mình số 0912 345 678 nhé", "personal_data"),
        ("chuyển vào tài khoản 1234567890123", "personal_data"),
        ("gửi hóa đơn về an.nguyen@example.com", "personal_data"),
        ("xem https://example.com/bill", "personal_data"),
        ("ăn phở 45k", None),  # số tiền thường không bị coi là dữ liệu cá nhân
        ("chuyển 2 triệu 500 nghìn", None),
    ],
)
def test_reject_reason(text, reason):
    assert nlu_candidates.reject_reason(text, "low_confidence") == reason


def test_a_cancelled_sentence_is_never_learned():
    assert nlu_candidates.reject_reason("ăn phở 45k rồi hủy", "cancelled") == "not_learnable"


class FakeSession:
    async def __aenter__(self):
        return self

    async def __aexit__(self, *exc):
        return False


@pytest.fixture
def stored(monkeypatch):
    calls = []

    async def fake_add(session, **kwargs):
        calls.append(kwargs)
        return calls[-1].get("force_reason")

    monkeypatch.setattr(nlu_candidates, "add_pending", fake_add)
    app.dependency_overrides[get_session_factory] = lambda: FakeSession
    yield calls
    app.dependency_overrides.pop(get_session_factory, None)


async def post(path, body, auth=True):
    transport = httpx.ASGITransport(app=app)  # không chạy lifespan nên không cần Postgres
    headers = {"Authorization": f"Bearer {settings.internal_api_key}"} if auth else {}
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        return await client.post(f"/api/v1{path}", json=body, headers=headers)


def body(**extra):
    return {"text": "mai tôi phải trả cho bác kia chưa nhỉ", "user_id_hash": HASH, "unclear_reason": "not_understood", **extra}


async def test_requires_the_internal_key(stored):
    assert (await post("/chat/unclear", body(), auth=False)).status_code == 401
    assert (await post("/chat/unclear/erase", {"user_id_hash": HASH}, auth=False)).status_code == 401
    assert stored == []


async def test_a_learnable_sentence_is_stored_with_the_machine_guess_kept_as_a_guess(stored):
    response = await post("/chat/unclear", body(predicted_intent="out_of_scope", predicted_confidence=0.62))
    assert response.status_code == 200 and response.json() == {"stored": True, "reason": None}
    assert stored[0]["predicted_intent"] == "out_of_scope" and stored[0]["user_id_hash"] == HASH


async def test_a_refusal_comes_back_with_its_reason(monkeypatch, stored):
    async def refuse(session, **kwargs):
        return "personal_data"

    monkeypatch.setattr(nlu_candidates, "add_pending", refuse)
    assert (await post("/chat/unclear", body())).json() == {"stored": False, "reason": "personal_data"}


async def test_a_database_failure_is_never_a_500(monkeypatch, stored):
    async def boom(session, **kwargs):
        raise RuntimeError("db down")

    monkeypatch.setattr(nlu_candidates, "add_pending", boom)
    response = await post("/chat/unclear", body())
    assert response.status_code == 200 and response.json() == {"stored": False, "reason": "error"}


@pytest.mark.parametrize("bad", [{"text": ""}, {"text": "a" * 501}, {"user_id_hash": "short"}, {"unclear_reason": "whatever"}, {"predicted_confidence": 1.5}])
async def test_bad_input_is_rejected(stored, bad):
    assert (await post("/chat/unclear", body(**bad))).status_code == 422


async def test_erase_returns_how_many_were_deleted(monkeypatch, stored):
    async def fake_delete(session, user_id_hash):
        assert user_id_hash == HASH
        return 3

    monkeypatch.setattr(nlu_candidates, "delete_by_user_hash", fake_delete)
    response = await post("/chat/unclear/erase", {"user_id_hash": HASH})
    assert response.status_code == 200 and response.json() == {"deleted": 3}
