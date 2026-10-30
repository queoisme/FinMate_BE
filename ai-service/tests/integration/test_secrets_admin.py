"""Kho key bên thứ ba trên Postgres thật: lưu mã hóa, không trả key về, không ghi key vào nhật ký, xóa thì quay về biến môi trường."""
import base64
import os

import pytest
import pytest_asyncio
from sqlalchemy import delete, select, text
from sqlalchemy.ext.asyncio import async_sessionmaker

from app.db.models.bill import BillRuleAudit
from app.db.models.secret import SecretRow
from app.services import secret_store

ACTOR = "c" * 64
KEY = "AIzaSyD-example-key-0123456789abcdefXYZ9"
URL = "/api/v1/admin/secrets"


@pytest_asyncio.fixture(autouse=True)
async def clean(session, monkeypatch):
    monkeypatch.setattr(secret_store.settings, "secrets_master_key", base64.urlsafe_b64encode(os.urandom(32)).decode())
    monkeypatch.setattr(secret_store.settings, "gemini_api_key", None)
    monkeypatch.setattr(secret_store.settings, "openrouter_api_key", None)
    monkeypatch.setattr(secret_store.settings, "google_vision_api_key", None)
    secret_store._values.clear()
    await session.execute(delete(SecretRow))
    await session.commit()  # kb_audit_log chỉ được thêm dòng (trigger chặn DELETE): các ca dưới đây đếm theo chênh lệch, không dọn nhật ký
    yield
    secret_store._values.clear()
    await session.execute(delete(SecretRow))
    await session.commit()


async def test_saving_a_key_stores_only_ciphertext_and_the_api_never_returns_it(client, auth_headers, session):
    headers = {**auth_headers, "X-Actor-Hash": ACTOR}

    put = await client.put(f"{URL}/gemini_api_key", headers=headers, json={"value": KEY, "reason": "đổi key quý 4"})

    assert put.status_code == 200
    body = put.json()
    assert (body["source"], body["last4"], body["configured"]) == ("db", KEY[-4:], True)
    assert KEY not in put.text  # phản hồi không chứa key

    listing = await client.get(URL, headers=auth_headers)
    assert listing.json()["available"] is True and KEY not in listing.text
    assert {item["name"]: item["source"] for item in listing.json()["items"]} == {"gemini_api_key": "db", "openrouter_api_key": "none", "google_vision_api_key": "none"}

    row = (await session.execute(select(SecretRow))).scalar_one()
    assert KEY.encode() not in row.ciphertext and len(row.nonce) == secret_store.NONCE_BYTES  # DB chỉ có bản mã
    assert secret_store.decrypt("gemini_api_key", row.ciphertext, row.nonce) == KEY
    assert secret_store.current("gemini_api_key") == KEY  # áp dụng ngay trong tiến trình, không cần khởi động lại


async def test_the_audit_row_names_the_key_and_the_last_four_but_never_the_value(client, auth_headers, session):
    headers = {**auth_headers, "X-Actor-Hash": ACTOR}
    await client.put(f"{URL}/gemini_api_key", headers=headers, json={"value": KEY, "reason": "lần đầu"})
    await client.put(f"{URL}/gemini_api_key", headers=headers, json={"value": KEY[:-4] + "ZZZZ", "reason": "đổi key"})

    newest = (await session.execute(select(BillRuleAudit).where(BillRuleAudit.resource == "secret").order_by(BillRuleAudit.created_at.desc()).limit(2))).scalars().all()
    rows = list(reversed(newest))

    assert [(r.action, r.after["last4"], r.reason) for r in rows] == [("update", KEY[-4:], "lần đầu"), ("update", "ZZZZ", "đổi key")]
    assert rows[1].before == {"set": True, "last4": KEY[-4:]}
    assert all(KEY[:-4] not in str(r.before) + str(r.after) for r in rows)
    raw = (await session.execute(text("SELECT before::text || after::text FROM kb_audit_log WHERE resource = 'secret'"))).scalars().all()
    assert not any(KEY[:12] in line for line in raw)  # kể cả khi đọc thẳng bằng SQL


async def test_deleting_the_key_falls_back_to_the_environment_value(client, auth_headers, monkeypatch):
    monkeypatch.setattr(secret_store.settings, "gemini_api_key", "env-key-from-dotenv-0000")
    headers = {**auth_headers, "X-Actor-Hash": ACTOR}
    await client.put(f"{URL}/gemini_api_key", headers=headers, json={"value": KEY})
    assert secret_store.current("gemini_api_key") == KEY

    deleted = await client.delete(f"{URL}/gemini_api_key", headers=headers, params={"reason": "dùng lại biến môi trường"})

    assert deleted.status_code == 200 and deleted.json()["source"] == "env" and deleted.json()["last4"] == "0000"
    assert secret_store.current("gemini_api_key") == "env-key-from-dotenv-0000"
    assert (await client.delete(f"{URL}/gemini_api_key", headers=headers)).status_code == 404  # đã xóa rồi


async def test_without_a_master_key_a_save_is_refused_and_nothing_is_stored(client, auth_headers, session, monkeypatch):
    monkeypatch.setattr(secret_store.settings, "secrets_master_key", None)

    response = await client.put(f"{URL}/gemini_api_key", headers={**auth_headers, "X-Actor-Hash": ACTOR}, json={"value": KEY})

    assert response.status_code == 422 and response.json()["error"]["code"] == "secrets_unavailable"
    assert KEY not in response.text
    assert (await session.execute(select(SecretRow))).first() is None
    assert (await client.get(URL, headers=auth_headers)).json()["available"] is False


async def test_an_invalid_key_is_422_and_the_response_does_not_echo_it(client, auth_headers):
    headers = {**auth_headers, "X-Actor-Hash": ACTOR}
    for bad in ("short", "has a space inside it", "x" * 600):
        response = await client.put(f"{URL}/gemini_api_key", headers=headers, json={"value": bad})
        assert response.status_code == 422 and response.json()["error"]["code"] == "invalid_secret"
        assert bad not in response.text


async def test_an_unknown_name_is_404_and_a_write_without_an_actor_is_422(client, auth_headers):
    assert (await client.put(f"{URL}/not_a_secret", headers={**auth_headers, "X-Actor-Hash": ACTOR}, json={"value": KEY})).status_code == 404
    assert (await client.put(f"{URL}/gemini_api_key", headers=auth_headers, json={"value": KEY})).status_code == 422  # thiếu người thực hiện
    assert (await client.get(URL)).status_code in (401, 403)  # không có khóa nội bộ


async def test_reload_reads_every_key_back_and_skips_one_it_cannot_decrypt(client, auth_headers, session, engine):
    headers = {**auth_headers, "X-Actor-Hash": ACTOR}
    await client.put(f"{URL}/gemini_api_key", headers=headers, json={"value": KEY})
    await client.put(f"{URL}/openrouter_api_key", headers=headers, json={"value": "or-key-0123456789"})
    secret_store._values.clear()

    factory = async_sessionmaker(engine, expire_on_commit=False)

    assert await secret_store.reload(factory) == 2  # một bản chạy khác nạp được cả hai từ DB
    assert secret_store.current("gemini_api_key") == KEY and secret_store.current("openrouter_api_key") == "or-key-0123456789"

    row = await session.get(SecretRow, "openrouter_api_key")
    row.ciphertext = row.ciphertext[:-1] + bytes([row.ciphertext[-1] ^ 1])  # hỏng một dòng
    await session.commit()
    assert await secret_store.reload(factory) == 1  # dòng hỏng bị bỏ, dòng còn lại vẫn dùng được
    assert secret_store.current("gemini_api_key") == KEY


async def test_only_a_shared_key_reaches_another_service_and_never_through_the_admin_route(client, auth_headers):
    headers = {**auth_headers, "X-Actor-Hash": ACTOR}
    await client.put(f"{URL}/google_vision_api_key", headers=headers, json={"value": KEY, "reason": "đặt key Vision"})
    await client.put(f"{URL}/gemini_api_key", headers=headers, json={"value": KEY[:-4] + "ZZZZ", "reason": "đặt key Gemini"})
    shared = "/api/v1/internal/secrets"

    ok = await client.get(f"{shared}/google_vision_api_key", params={"consumer": "ocr"}, headers=auth_headers)
    assert (ok.status_code, ok.json()["value"], ok.headers["cache-control"]) == (200, KEY, "no-store")  # OCR Service nhận được giá trị rõ

    assert (await client.get(f"{shared}/gemini_api_key", params={"consumer": "ocr"}, headers=auth_headers)).status_code == 403  # key chỉ AI Service dùng thì không chia sẻ
    assert (await client.get(f"{shared}/google_vision_api_key", params={"consumer": "backend"}, headers=auth_headers)).status_code == 403  # sai người dùng
    assert (await client.get(f"{shared}/google_vision_api_key", params={"consumer": "ocr"})).status_code in (401, 403)  # thiếu khóa nội bộ
    assert KEY not in (await client.get(URL, headers=auth_headers)).text  # trang quản trị vẫn không thấy giá trị
