"""Thao tác của API quản trị định dạng thông báo (``/api/v1/admin/provider-formats``): đọc, soạn bản draft, chạy thử, kích hoạt, lưu trữ.

Chỉ bản ``draft`` sửa được (pattern, trường, mẫu, ưu tiên): bản ``active`` đang chạy không bị đụng tới; muốn sửa thì ``clone`` thành draft mới, sửa, kiểm, kích hoạt
(``app.services.provider_formats``). Mọi lỗi nghiệp vụ là ``RuleError`` (404/409/422), cùng dạng JSON với ``/admin/rules``. Hàm ở đây ``flush`` chứ không ``commit``: router quyết định.

Ghi nhật ký ``kb_audit_log`` (``resource = 'provider_format'``): ``create`` (kèm ``clone``), ``activate``, ``deactivate``, và ``update`` cho mọi chỉnh sửa bản draft. Nhật ký chỉ ghi TÊN thứ bị
sửa, không ghi nội dung mẫu. Không bao giờ log ``text`` của ``try_text``.
"""

from __future__ import annotations

import uuid
from typing import Any

import regex
from sqlalchemy import func, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.rules_admin import RuleError
from app.db.models.bill import BillRuleAudit
from app.db.models.provider_format import ProviderFormat, ProviderFormatField, ProviderFormatSample
from app.db.models.provider_pattern import ProviderPattern
from app.schemas.provider_formats import FieldCreate, FieldUpdate, FormatCreate, FormatUpdate, PatternCreate, PatternUpdate, SampleCreate
from app.services import provider_formats
from app.services.provider_formats import FormatError
from app.utils.provider_patterns import _FLAGS, PatternMatcher

_CHILDREN = {"patterns": ProviderPattern, "fields": ProviderFormatField, "samples": ProviderFormatSample}


def _iso(value) -> str | None:
    return value.isoformat() if value is not None else None


def _not_found(what: str) -> RuleError:
    return RuleError(404, "not_found", f"Không tìm thấy {what}.")


def _conflict(code: str, message: str) -> RuleError:
    return RuleError(409, code, message)


def _invalid(code: str, message: str) -> RuleError:
    return RuleError(422, code, message)


# ------------------------------------------------------------------ biểu diễn
def format_dict(fmt: ProviderFormat) -> dict[str, Any]:
    return {
        "id": str(fmt.id), "provider_key": fmt.provider_key, "package_name": fmt.package_name, "version": fmt.version, "status": fmt.status,
        "priority": fmt.priority, "notes": fmt.notes, "created_by": fmt.created_by, "created_at": _iso(fmt.created_at), "updated_at": _iso(fmt.updated_at),
    }


def _pattern(p: ProviderPattern) -> dict[str, Any]:
    return {
        "id": str(p.id), "pattern_name": p.pattern_name, "regex": p.regex, "transaction_type": p.transaction_type, "priority": p.priority,
        "sample_text": p.sample_text, "is_active": p.is_active, "notes": p.notes,
    }


def _field(f: ProviderFormatField) -> dict[str, Any]:
    return {
        "id": str(f.id), "field_key": f.field_key, "maps_to": f.maps_to, "data_type": f.data_type, "is_required": f.is_required, "source_kind": f.source_kind,
        "source_ref": f.source_ref, "normalizer": f.normalizer, "is_sensitive": f.is_sensitive,
    }


def _sample(s: ProviderFormatSample) -> dict[str, Any]:
    return {"id": str(s.id), "sample_text": s.sample_text, "expected_fields": s.expected_fields}


# ------------------------------------------------------------------ đọc
async def list_formats(session: AsyncSession, *, package_name: str | None, status: str | None, limit: int, offset: int) -> dict[str, Any]:
    where = []
    if package_name:
        where.append(ProviderFormat.package_name == package_name)
    if status:
        where.append(ProviderFormat.status == status)
    total = (await session.execute(select(func.count()).select_from(ProviderFormat).where(*where))).scalar_one()
    rows = (
        await session.execute(
            select(ProviderFormat).where(*where).order_by(ProviderFormat.package_name, ProviderFormat.version.desc()).limit(limit).offset(offset)
        )
    ).scalars().all()
    items = []
    for fmt in rows:
        counts = {
            key: (await session.execute(select(func.count()).select_from(model).where(model.format_id == fmt.id))).scalar_one()
            for key, model in _CHILDREN.items()
        }
        items.append({**format_dict(fmt), **counts})
    return {"items": items, "total": total}


async def _get(session: AsyncSession, format_id: uuid.UUID) -> ProviderFormat:
    fmt = await session.get(ProviderFormat, format_id)
    if fmt is None:
        raise _not_found(f"định dạng {format_id}")
    return fmt


async def _draft(session: AsyncSession, format_id: uuid.UUID) -> ProviderFormat:
    fmt = await _get(session, format_id)
    if fmt.status != "draft":
        raise _conflict("format_not_draft", f"Chỉ sửa được định dạng draft (hiện là {fmt.status}). Nhân bản nó thành bản draft mới rồi sửa.")
    return fmt


async def detail(session: AsyncSession, format_id: uuid.UUID) -> dict[str, Any]:
    fmt = await _get(session, format_id)
    await session.refresh(fmt)  # updated_at có thể vừa được đặt bằng func.now(): đọc lại giá trị thật trước khi dựng phản hồi
    patterns = (await session.execute(select(ProviderPattern).where(ProviderPattern.format_id == format_id).order_by(ProviderPattern.priority, ProviderPattern.pattern_name))).scalars()
    fields = (await session.execute(select(ProviderFormatField).where(ProviderFormatField.format_id == format_id).order_by(ProviderFormatField.field_key))).scalars()
    samples = (await session.execute(select(ProviderFormatSample).where(ProviderFormatSample.format_id == format_id).order_by(ProviderFormatSample.created_at, ProviderFormatSample.id))).scalars()
    return {**format_dict(fmt), "patterns": [_pattern(p) for p in patterns], "fields": [_field(f) for f in fields], "samples": [_sample(s) for s in samples]}


# ------------------------------------------------------------------ nhật ký
def _audit(session: AsyncSession, fmt: ProviderFormat, action: str, after: dict | None, actor: str, reason: str | None = None, before: dict | None = None) -> None:
    session.add(BillRuleAudit(resource="provider_format", resource_key=str(fmt.id), action=action, before=before, after=after, actor_hash=actor, reason=reason))


async def _flush(session: AsyncSession, message: str) -> None:
    """``flush`` mà ràng buộc duy nhất / khóa ngoại bị vi phạm thì thành 409 (và hoàn tác phần chưa ghi)."""
    try:
        await session.flush()
    except IntegrityError as error:
        await session.rollback()
        raise _conflict("conflict", message) from error


def _compile(pattern: str) -> None:
    try:
        regex.compile(pattern, _FLAGS)
    except regex.error as error:
        raise _invalid("invalid_regex", f"Regex không biên dịch được: {error}") from error


# ------------------------------------------------------------------ định dạng
async def create_blank(session: AsyncSession, body: FormatCreate, actor: str) -> dict[str, Any]:
    """Bản draft trống: phiên bản tiếp theo của ``package_name`` (v1 nếu chưa có). ``provider_key`` phải khớp với các phiên bản đã có của package đó."""
    existing = (await session.execute(select(ProviderFormat.provider_key, ProviderFormat.version).where(ProviderFormat.package_name == body.package_name))).all()
    if any(row[0] != body.provider_key for row in existing):
        raise _invalid("provider_key_mismatch", f"Package {body.package_name!r} đã có định dạng với provider_key khác ({existing[0][0]!r}).")
    fmt = ProviderFormat(
        provider_key=body.provider_key, package_name=body.package_name, version=max((row[1] for row in existing), default=0) + 1,
        status="draft", priority=body.priority, notes=body.notes, created_by=actor,
    )
    session.add(fmt)
    await _flush(session, "Trùng phiên bản của package này.")
    _audit(session, fmt, "create", provider_formats._snapshot(fmt), actor, body.reason)
    return await detail(session, fmt.id)


async def clone(session: AsyncSession, package_name: str, actor: str, reason: str | None) -> dict[str, Any]:
    try:
        new = await provider_formats.clone(session, package_name, actor_hash=actor, reason=reason)
    except FormatError as error:
        raise _not_found(f"định dạng nào của package {package_name!r} để nhân bản") from error
    return await detail(session, new.id)


async def update_format(session: AsyncSession, format_id: uuid.UUID, body: FormatUpdate, actor: str) -> dict[str, Any]:
    fmt = await _draft(session, format_id)
    sent = body.model_fields_set
    changed = []
    if "priority" in sent and body.priority is not None:
        fmt.priority = body.priority
        changed.append("priority")
    if "notes" in sent:
        fmt.notes = body.notes
        changed.append("notes")
    if changed:
        fmt.updated_at = func.now()
        await session.flush()
        _audit(session, fmt, "update", {"change": "format", "fields": changed}, actor)
    return await detail(session, format_id)


async def check(session: AsyncSession, format_id: uuid.UUID) -> dict[str, Any]:
    await _get(session, format_id)
    problems = await provider_formats.check_format(session, format_id)
    return {"ready": not problems, "problems": problems}


async def activate(session: AsyncSession, format_id: uuid.UUID, actor: str, reason: str | None) -> dict[str, Any]:
    await _get(session, format_id)
    try:
        await provider_formats.activate(session, format_id, actor_hash=actor, reason=reason)
    except FormatError as error:
        raise RuleError(409, "format_not_ready", "Định dạng chưa đủ điều kiện kích hoạt.", details=error.problems) from error
    return await detail(session, format_id)


async def archive(session: AsyncSession, format_id: uuid.UUID, actor: str, reason: str | None) -> dict[str, Any]:
    await _get(session, format_id)
    try:
        await provider_formats.archive(session, format_id, actor_hash=actor, reason=reason)
    except FormatError as error:
        raise _conflict("already_archived", "; ".join(error.problems)) from error
    return await detail(session, format_id)


# ------------------------------------------------------------------ pattern
async def _pattern_row(session: AsyncSession, format_id: uuid.UUID, pattern_id: uuid.UUID) -> ProviderPattern:
    row = await session.get(ProviderPattern, pattern_id)
    if row is None or row.format_id != format_id:
        raise _not_found(f"pattern {pattern_id}")
    return row


async def add_pattern(session: AsyncSession, format_id: uuid.UUID, body: PatternCreate, actor: str) -> dict[str, Any]:
    fmt = await _draft(session, format_id)
    _compile(body.regex)
    row = ProviderPattern(format_id=format_id, **body.model_dump())
    session.add(row)
    await _flush(session, f"Định dạng này đã có pattern tên {body.pattern_name!r}.")
    _audit(session, fmt, "update", {"change": "pattern_add", "pattern_name": row.pattern_name}, actor)
    return _pattern(row)


async def update_pattern(session: AsyncSession, format_id: uuid.UUID, pattern_id: uuid.UUID, body: PatternUpdate, actor: str) -> dict[str, Any]:
    fmt = await _draft(session, format_id)
    row = await _pattern_row(session, format_id, pattern_id)
    values = {key: getattr(body, key) for key in body.model_fields_set}
    if values.get("regex") is not None:
        _compile(values["regex"])
    for key, value in values.items():
        if value is None and key in ("pattern_name", "regex", "priority", "is_active"):
            continue  # cột bắt buộc: null nghĩa là không đổi
        setattr(row, key, value)
    row.updated_at = func.now()
    await _flush(session, "Định dạng này đã có pattern cùng tên.")
    _audit(session, fmt, "update", {"change": "pattern_update", "pattern_name": row.pattern_name, "fields": sorted(values)}, actor)
    return _pattern(row)


async def delete_pattern(session: AsyncSession, format_id: uuid.UUID, pattern_id: uuid.UUID, actor: str) -> None:
    fmt = await _draft(session, format_id)
    row = await _pattern_row(session, format_id, pattern_id)
    name = row.pattern_name
    await session.delete(row)
    await session.flush()
    _audit(session, fmt, "update", {"change": "pattern_delete", "pattern_name": name}, actor)


# ------------------------------------------------------------------ trường
async def _field_row(session: AsyncSession, format_id: uuid.UUID, field_id: uuid.UUID) -> ProviderFormatField:
    row = await session.get(ProviderFormatField, field_id)
    if row is None or row.format_id != format_id:
        raise _not_found(f"trường {field_id}")
    return row


def _check_field(maps_to: str | None, is_sensitive: bool) -> None:
    if is_sensitive and maps_to is None:
        raise _invalid(
            "sensitive_field_unmapped",
            "Trường nhạy cảm không được là trường phụ (trường phụ được lưu vào nhật ký phân tích). Muốn nhận diện mà không lưu thì đừng khai báo trường đó.",
        )


async def add_field(session: AsyncSession, format_id: uuid.UUID, body: FieldCreate, actor: str) -> dict[str, Any]:
    fmt = await _draft(session, format_id)
    _check_field(body.maps_to, body.is_sensitive)
    row = ProviderFormatField(format_id=format_id, **body.model_dump())
    session.add(row)
    await _flush(session, "Trùng field_key, hoặc đã có trường khác ánh xạ vào cùng trường hợp đồng.")
    _audit(session, fmt, "update", {"change": "field_add", "field_key": row.field_key}, actor)
    return _field(row)


async def update_field(session: AsyncSession, format_id: uuid.UUID, field_id: uuid.UUID, body: FieldUpdate, actor: str) -> dict[str, Any]:
    fmt = await _draft(session, format_id)
    row = await _field_row(session, format_id, field_id)
    values = {key: getattr(body, key) for key in body.model_fields_set}
    maps_to = values["maps_to"] if "maps_to" in values else row.maps_to
    sensitive = values["is_sensitive"] if values.get("is_sensitive") is not None else row.is_sensitive
    _check_field(maps_to, sensitive)
    for key, value in values.items():
        if value is None and key in ("data_type", "is_required", "source_kind", "source_ref", "is_sensitive"):
            continue  # cột bắt buộc: null nghĩa là không đổi
        setattr(row, key, value)
    await _flush(session, "Trùng field_key, hoặc đã có trường khác ánh xạ vào cùng trường hợp đồng.")
    _audit(session, fmt, "update", {"change": "field_update", "field_key": row.field_key, "fields": sorted(values)}, actor)
    return _field(row)


async def delete_field(session: AsyncSession, format_id: uuid.UUID, field_id: uuid.UUID, actor: str) -> None:
    fmt = await _draft(session, format_id)
    row = await _field_row(session, format_id, field_id)
    key = row.field_key
    await session.delete(row)
    await session.flush()
    _audit(session, fmt, "update", {"change": "field_delete", "field_key": key}, actor)


# ------------------------------------------------------------------ mẫu kiểm tra
async def add_sample(session: AsyncSession, format_id: uuid.UUID, body: SampleCreate, actor: str) -> dict[str, Any]:
    fmt = await _draft(session, format_id)
    row = ProviderFormatSample(format_id=format_id, sample_text=body.sample_text, expected_fields=body.expected_fields)
    session.add(row)
    await session.flush()
    _audit(session, fmt, "update", {"change": "sample_add", "sample_id": str(row.id)}, actor)
    return _sample(row)


async def delete_sample(session: AsyncSession, format_id: uuid.UUID, sample_id: uuid.UUID, actor: str) -> None:
    fmt = await _draft(session, format_id)
    row = await session.get(ProviderFormatSample, sample_id)
    if row is None or row.format_id != format_id:
        raise _not_found(f"mẫu {sample_id}")
    await session.delete(row)
    await session.flush()
    _audit(session, fmt, "update", {"change": "sample_delete", "sample_id": str(sample_id)}, actor)


# ------------------------------------------------------------------ chạy thử
async def try_text(session: AsyncSession, format_id: uuid.UUID, text: str) -> dict[str, Any]:
    """Chạy các pattern ĐANG BẬT của định dạng (kể cả draft) trên ``text``: kết quả giống hệt lúc định dạng chạy thật. Không ghi gì."""
    fmt = await _get(session, format_id)
    patterns = (
        await session.execute(select(ProviderPattern).where(ProviderPattern.format_id == format_id, ProviderPattern.is_active.is_(True)).order_by(ProviderPattern.priority, ProviderPattern.pattern_name))
    ).scalars().all()
    fields = (await session.execute(select(ProviderFormatField).where(ProviderFormatField.format_id == format_id))).scalars().all()
    broken: list[str] = []
    usable: list[ProviderPattern] = []
    for pattern in patterns:
        try:
            regex.compile(pattern.regex, _FLAGS)
            usable.append(pattern)
        except regex.error:
            broken.append(pattern.pattern_name)
    matched = PatternMatcher(provider_formats.specs_for_format(fmt, usable, fields)).match(fmt.package_name, text)
    return {
        "matched": matched is not None,
        "pattern_name": matched.pattern_name if matched else None,
        "fields": provider_formats.expected_from_match(matched) if matched else None,
        "broken_patterns": broken,
    }
