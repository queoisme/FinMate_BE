"""Kho key của bên thứ ba (Gemini, OpenRouter): admin nhập qua trang quản trị, lưu ĐÃ MÃ HÓA trong ``secret_store``, dịch vụ đọc theo lúc gọi nên đổi key không cần khởi động lại.

Quy tắc giữ an toàn (đừng nới):
- AES-256-GCM, nonce 12 byte ngẫu nhiên MỖI lần lưu, tên key làm dữ liệu xác thực kèm (AAD): chép một dòng sang tên khác thì không giải mã được.
- Khóa chủ ở biến môi trường ``SECRETS_MASTER_KEY``, KHÔNG ở DB. Chưa đặt thì từ chối lưu key (422) chứ không lưu thô; key trong biến môi trường cũ vẫn chạy như trước.
- Không API nào trả key về, kể cả cho admin: chỉ trả trạng thái và 4 ký tự cuối. Muốn đổi thì nhập key mới. Không log key, không log thân yêu cầu, không đưa key vào URL (nhà cung cấp nhận qua header).
- Nhật ký đổi key (``kb_audit_log``, ``resource = 'secret'``) chỉ ghi tên, hành động, 4 ký tự cuối và lý do.

Thứ tự ưu tiên: key trong DB, rồi biến môi trường (``GEMINI_API_KEY``...). Các bản chạy khác thấy key mới ở lần nạp lại kế tiếp (cùng chu kỳ ``config_runtime``)."""

from __future__ import annotations

import base64
import binascii
import os
import re
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime, timezone

import httpx
import structlog
from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.rules_admin import RuleError
from app.core.config import settings
from app.db.models.bill import BillRuleAudit
from app.db.models.secret import SecretRow
from app.db.session import AsyncSessionLocal

logger = structlog.get_logger(__name__)

KEY_VERSION = 1
NONCE_BYTES = 12
MASTER_KEY_BYTES = 32
MIN_SECRET_CHARS = 8
MAX_SECRET_CHARS = 512
TEST_TIMEOUT_SECONDS = 5.0
_PRINTABLE_NO_SPACE = re.compile(r"^[\x21-\x7e]+$")  # key của nhà cung cấp chỉ gồm ký tự in được, không khoảng trắng


@dataclass(frozen=True)
class SecretSpec:
    name: str
    title: str
    env_attr: str  # trường trong settings giữ key từ biến môi trường: bản dự phòng khi DB chưa có
    test_url: str  # một lệnh gọi chỉ-đọc, không tốn token
    test_headers: Callable[[str], dict[str, str]]


SECRETS: dict[str, SecretSpec] = {
    "gemini_api_key": SecretSpec(
        "gemini_api_key", "Gemini API key", "gemini_api_key",
        "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1", lambda key: {"x-goog-api-key": key},
    ),
    "openrouter_api_key": SecretSpec(
        "openrouter_api_key", "OpenRouter API key", "openrouter_api_key",
        "https://openrouter.ai/api/v1/auth/key", lambda key: {"Authorization": f"Bearer {key}"},
    ),
}

_values: dict[str, str] = {}  # key đã giải mã của TIẾN TRÌNH này, chỉ nằm trong bộ nhớ


def _spec(name: str) -> SecretSpec:
    if name not in SECRETS:
        raise RuleError(404, "not_found", f"Không có key '{name}'. Các key có: {sorted(SECRETS)}.")
    return SECRETS[name]


# ------------------------------------------------------------------ mã hóa
def _aead() -> AESGCM:
    raw = (settings.secrets_master_key or "").strip()
    if not raw:
        raise RuleError(422, "secrets_unavailable", "Chưa cấu hình khóa mã hóa (SECRETS_MASTER_KEY) nên chưa lưu được key qua trang quản trị.")
    try:
        key = base64.b64decode(raw.replace("-", "+").replace("_", "/") + "=" * (-len(raw) % 4))
    except (binascii.Error, ValueError):
        key = b""
    if len(key) != MASTER_KEY_BYTES:
        raise RuleError(422, "secrets_key_invalid", f"SECRETS_MASTER_KEY phải là {MASTER_KEY_BYTES} byte mã hóa base64.")
    return AESGCM(key)


def encrypt(name: str, value: str) -> tuple[bytes, bytes]:
    nonce = os.urandom(NONCE_BYTES)
    return _aead().encrypt(nonce, value.encode("utf-8"), name.encode("utf-8")), nonce


def decrypt(name: str, ciphertext: bytes, nonce: bytes) -> str:
    return _aead().decrypt(nonce, ciphertext, name.encode("utf-8")).decode("utf-8")


def encryption_available() -> bool:
    try:
        _aead()
    except RuleError:
        return False
    return True


# ------------------------------------------------------------------ đọc theo lúc gọi
def current(name: str) -> str | None:
    """Key đang dùng: giá trị trong DB (đã giải mã) nếu có, không thì biến môi trường. Đồng bộ và rẻ vì client Gemini hỏi mỗi lần gọi."""
    return _values.get(name) or getattr(settings, _spec(name).env_attr, None) or None


def getter(name: str) -> Callable[[], str | None]:
    """Hàm không tham số trả key hiện tại: đưa cho client thay vì chuỗi key cố định để đổi key không cần khởi động lại."""
    _spec(name)
    return lambda: current(name)


async def reload(session_factory=AsyncSessionLocal) -> int:
    """Nạp lại mọi key từ DB (lúc khởi động và định kỳ). Dòng nào không giải mã được thì bỏ qua và ghi cảnh báo (chỉ loại lỗi): dịch vụ rơi về biến môi trường của key đó."""
    fresh: dict[str, str] = {}
    async with session_factory() as session:
        rows = (await session.scalars(select(SecretRow))).all()
    for row in rows:
        if row.name not in SECRETS:
            continue
        try:
            fresh[row.name] = decrypt(row.name, row.ciphertext, row.nonce)
        except (InvalidTag, RuleError, ValueError) as error:
            logger.warning("secret_decrypt_failed", name=row.name, error_type=type(error).__name__)
    _values.clear()
    _values.update(fresh)
    return len(fresh)


# ------------------------------------------------------------------ quản trị
def _clean(value: object) -> str:
    text = value.strip() if isinstance(value, str) else ""
    if not MIN_SECRET_CHARS <= len(text) <= MAX_SECRET_CHARS or not _PRINTABLE_NO_SPACE.match(text):
        # Thông điệp chung, không nhắc lại giá trị: đây là nơi dễ làm lộ key vào phản hồi hay log nhất.
        raise RuleError(422, "invalid_secret", f"Key không hợp lệ: dài {MIN_SECRET_CHARS}-{MAX_SECRET_CHARS} ký tự, chỉ gồm ký tự in được và không có khoảng trắng.")
    return text


async def status(session: AsyncSession) -> dict:
    """Trạng thái từng key: nguồn (db | env | none), 4 ký tự cuối và lúc đổi. KHÔNG bao giờ có giá trị."""
    rows = {row.name: row for row in (await session.scalars(select(SecretRow))).all()}
    items = []
    for spec in SECRETS.values():
        row, env = rows.get(spec.name), getattr(settings, spec.env_attr, None)
        source = "db" if row else "env" if env else "none"
        items.append({
            "name": spec.name, "title": spec.title, "source": source, "configured": source != "none",
            "last4": row.last4 if row else (env[-4:] if env else None),
            "updated_at": row.updated_at.isoformat() if row else None,
        })
    return {"available": encryption_available(), "items": items}


async def save(session: AsyncSession, name: str, value: object, actor: str, reason: str | None) -> None:
    """Mã hóa rồi ghi đè key; commit tại đây rồi mới áp dụng vào bộ nhớ, để bộ nhớ không bao giờ giữ một key mà DB không có."""
    _spec(name)
    text = _clean(value)
    ciphertext, nonce = encrypt(name, text)
    row = await session.get(SecretRow, name)
    before = {"set": row is not None, "last4": row.last4 if row else None}
    if row is None:
        session.add(SecretRow(name=name, ciphertext=ciphertext, nonce=nonce, key_version=KEY_VERSION, last4=text[-4:], updated_by=actor))
    else:
        row.ciphertext, row.nonce, row.key_version, row.last4, row.updated_by = ciphertext, nonce, KEY_VERSION, text[-4:], actor
        row.updated_at = datetime.now(timezone.utc)
    session.add(BillRuleAudit(resource="secret", resource_key=name, action="update", before=before, after={"set": True, "last4": text[-4:]}, actor_hash=actor, reason=reason))
    await session.commit()
    _values[name] = text


async def delete(session: AsyncSession, name: str, actor: str, reason: str | None) -> None:
    """Xóa key trong DB: dịch vụ quay về key ở biến môi trường (nếu có)."""
    _spec(name)
    row = await session.get(SecretRow, name)
    if row is None:
        raise RuleError(404, "not_found", f"Key '{name}' chưa được lưu trong DB.")
    before = {"set": True, "last4": row.last4}
    await session.delete(row)
    session.add(BillRuleAudit(resource="secret", resource_key=name, action="delete", before=before, after={"set": False, "last4": None}, actor_hash=actor, reason=reason))
    await session.commit()
    _values.pop(name, None)


def _client() -> httpx.AsyncClient:
    return httpx.AsyncClient(timeout=TEST_TIMEOUT_SECONDS)


async def test(name: str) -> dict:
    """Thử key đang dùng bằng một lệnh gọi chỉ-đọc tới nhà cung cấp. Kết quả chỉ nói ổn hay không, không chép lại phản hồi của nhà cung cấp."""
    spec = _spec(name)
    key = current(name)
    if not key:
        return {"ok": False, "detail": "Chưa có key để thử."}
    try:
        async with _client() as client:
            response = await client.get(spec.test_url, headers=spec.test_headers(key))
    except httpx.HTTPError:
        return {"ok": False, "detail": "Không kết nối được tới nhà cung cấp."}
    if response.status_code == 200:
        return {"ok": True, "detail": "Key hoạt động."}
    if response.status_code in (400, 401, 403):
        return {"ok": False, "detail": "Nhà cung cấp từ chối key này."}
    return {"ok": False, "detail": f"Nhà cung cấp trả mã {response.status_code}."}
