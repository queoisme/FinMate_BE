"""Kho key bên thứ ba: mã hóa đúng, không lộ giá trị, đọc theo lúc gọi, thử kết nối không rò key vào URL."""
import asyncio
import base64
import os

import httpx
import pytest
from cryptography.exceptions import InvalidTag

from app.bill.gemini import GeminiClient
from app.bill.rules_admin import RuleError
from app.services import secret_store

MASTER = base64.urlsafe_b64encode(os.urandom(32)).decode()
KEY = "AIzaSyD-example-key-0123456789abcdefXYZ9"


@pytest.fixture(autouse=True)
def isolated(monkeypatch):
    monkeypatch.setattr(secret_store.settings, "secrets_master_key", MASTER)
    monkeypatch.setattr(secret_store.settings, "gemini_api_key", None)
    monkeypatch.setattr(secret_store.settings, "openrouter_api_key", None)
    secret_store._values.clear()
    yield
    secret_store._values.clear()


def test_encrypt_round_trips_and_never_stores_the_plaintext():
    ciphertext, nonce = secret_store.encrypt("gemini_api_key", KEY)
    assert KEY.encode() not in ciphertext and len(nonce) == secret_store.NONCE_BYTES
    assert secret_store.decrypt("gemini_api_key", ciphertext, nonce) == KEY


def test_each_save_uses_a_fresh_nonce():
    (first, n1), (second, n2) = secret_store.encrypt("gemini_api_key", KEY), secret_store.encrypt("gemini_api_key", KEY)
    assert n1 != n2 and first != second  # cùng một key mã hóa hai lần ra hai bản mã khác nhau


def test_a_row_copied_to_another_name_cannot_be_decrypted():
    ciphertext, nonce = secret_store.encrypt("gemini_api_key", KEY)
    with pytest.raises(InvalidTag):
        secret_store.decrypt("openrouter_api_key", ciphertext, nonce)  # tên là dữ liệu xác thực kèm


def test_without_a_master_key_saving_is_refused_instead_of_stored_in_plaintext(monkeypatch):
    monkeypatch.setattr(secret_store.settings, "secrets_master_key", None)
    with pytest.raises(RuleError) as info:
        secret_store.encrypt("gemini_api_key", KEY)
    assert info.value.code == "secrets_unavailable" and not secret_store.encryption_available()


@pytest.mark.parametrize("bad", ["short", "a" * 31, "not base64 !!!"])
def test_a_master_key_of_the_wrong_size_is_rejected(monkeypatch, bad):
    monkeypatch.setattr(secret_store.settings, "secrets_master_key", bad)
    with pytest.raises(RuleError) as info:
        secret_store.encrypt("gemini_api_key", KEY)
    assert info.value.code == "secrets_key_invalid"


@pytest.mark.parametrize("bad", ["", "short", "has space inside the key", "tab\tchar12345", "x" * 513, None, 12345678])
def test_an_invalid_key_is_rejected_without_echoing_it(bad):
    with pytest.raises(RuleError) as info:
        secret_store._clean(bad)
    assert info.value.code == "invalid_secret"
    assert str(bad) not in info.value.message or str(bad) == ""  # thông điệp không nhắc lại giá trị


def test_the_database_value_wins_over_the_environment_and_removal_falls_back(monkeypatch):
    monkeypatch.setattr(secret_store.settings, "gemini_api_key", "env-key-0000")
    assert secret_store.current("gemini_api_key") == "env-key-0000"
    secret_store._values["gemini_api_key"] = KEY
    assert secret_store.current("gemini_api_key") == KEY
    secret_store._values.pop("gemini_api_key")
    assert secret_store.current("gemini_api_key") == "env-key-0000"


def test_an_unknown_secret_name_is_a_404():
    with pytest.raises(RuleError) as info:
        secret_store.current("../etc/passwd")
    assert info.value.status == 404


def test_the_gemini_client_picks_up_a_changed_key_without_a_restart():
    client = GeminiClient(secret_store.getter("gemini_api_key"), "model", 5.0, backup_key=secret_store.getter("openrouter_api_key"), backup_model="m")
    assert not client.available  # chưa có key nào
    secret_store._values["gemini_api_key"] = KEY
    assert client.available and client._key == KEY
    secret_store._values["gemini_api_key"] = "rotated-key-123456"
    assert client._key == "rotated-key-123456"
    secret_store._values.clear()
    secret_store._values["openrouter_api_key"] = "or-key-123456"
    assert client._backup_key == "or-key-123456" and client.available  # chỉ còn dự phòng vẫn dùng được


def test_a_plain_string_key_still_works_for_existing_callers():
    assert GeminiClient("plain-key-123", "model", 5.0)._key == "plain-key-123"


def _use_transport(monkeypatch, handler):
    monkeypatch.setattr(secret_store, "_client", lambda: httpx.AsyncClient(transport=httpx.MockTransport(handler)))


def test_testing_a_key_sends_it_in_a_header_never_in_the_url(monkeypatch):
    seen = {}

    def handler(request):
        seen["url"], seen["headers"] = str(request.url), request.headers
        return httpx.Response(200, json={"models": []})

    _use_transport(monkeypatch, handler)
    secret_store._values["gemini_api_key"] = KEY
    assert asyncio.run(secret_store.test("gemini_api_key")) == {"ok": True, "detail": "Key hoạt động."}
    assert KEY not in seen["url"] and seen["headers"]["x-goog-api-key"] == KEY


@pytest.mark.parametrize("status,ok", [(200, True), (400, False), (401, False), (403, False), (500, False)])
def test_the_test_result_only_says_whether_the_provider_accepted_the_key(monkeypatch, status, ok):
    _use_transport(monkeypatch, lambda request: httpx.Response(status, text="body that must not be echoed " + KEY))
    secret_store._values["openrouter_api_key"] = KEY
    result = asyncio.run(secret_store.test("openrouter_api_key"))
    assert result["ok"] is ok and KEY not in result["detail"] and "echoed" not in result["detail"]


def test_testing_without_a_key_or_with_the_provider_down_is_a_clean_failure(monkeypatch):
    assert asyncio.run(secret_store.test("gemini_api_key"))["ok"] is False

    def handler(request):
        raise httpx.ConnectError("down")

    _use_transport(monkeypatch, handler)
    secret_store._values["gemini_api_key"] = KEY
    assert asyncio.run(secret_store.test("gemini_api_key")) == {"ok": False, "detail": "Không kết nối được tới nhà cung cấp."}
