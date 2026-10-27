"""Định dạng thông báo theo nhà cung cấp (``kb_provider_formats``): nạp pattern cho bộ khớp, cổng kích hoạt, nhân bản phiên bản, nạp dữ liệu mặc định.

Vòng đời một thay đổi regex: ``clone`` (bản draft từ bản mới nhất) -> sửa pattern/trường/mẫu của bản draft -> ``check_format`` -> ``activate``. Bản ``active`` đang chạy không bị đụng tới
cho đến khi bản mới qua cổng; sai thì ``archive`` bản mới, bản cũ vẫn còn đó.

Cổng kích hoạt ở tầng ứng dụng (không phải ràng buộc DB): UPDATE ``status`` thẳng bằng SQL vượt được nó, nên người vận hành dùng ``scripts/provider_format.py``.
"""

from __future__ import annotations

import uuid
from collections.abc import Sequence
from typing import Any

import regex
import structlog
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.anonymizer import sha256_hex
from app.db.models.bill import BillRuleAudit
from app.db.models.provider_format import ProviderFormat, ProviderFormatField, ProviderFormatSample
from app.db.models.provider_pattern import ProviderPattern
from app.utils.provider_patterns import _FLAGS, CONTRACT_GROUP, ExtraField, PatternMatch, PatternMatcher, PatternSpec

logger = structlog.get_logger(__name__)

# Mã băm của "người" thực hiện khi là hệ thống (seed lúc khởi động, migration): cố định để lọc được trong nhật ký.
SYSTEM_ACTOR = sha256_hex("system")

# Kiểu dữ liệu và bộ chuẩn hóa mặc định của từng trường hợp đồng khi dựng khai báo từ regex.
_FIELD_DEFAULTS: dict[str, tuple[str, str]] = {
    "amount_cents": ("amount", "vnd_amount"),
    "balance_after_cents": ("amount", "vnd_amount"),
    "transacted_at": ("datetime", "clean_text"),
    "transaction_type": ("text", "clean_text"),
    "merchant_name": ("text", "clean_text"),
    "description": ("text", "clean_text"),
}


class FormatError(Exception):
    """Việc không làm được, kèm danh sách lý do để người vận hành sửa một lượt."""

    def __init__(self, problems: Sequence[str]) -> None:
        super().__init__("; ".join(problems))
        self.problems = list(problems)


# ---------------------------------------------------------------- dựng spec cho bộ khớp
def specs_for_format(
    fmt: ProviderFormat, patterns: Sequence[ProviderPattern], fields: Sequence[ProviderFormatField]
) -> list[PatternSpec]:
    """Mỗi pattern thành một ``PatternSpec`` mang ánh xạ group và trường phụ của định dạng. Trường nhạy cảm bị bỏ khỏi trường phụ ở đây, một chỗ duy nhất."""
    groups = tuple(
        (f.maps_to, f.source_ref)
        for f in fields
        if f.maps_to and f.source_kind == "regex_group" and f.source_ref != CONTRACT_GROUP[f.maps_to]
    )
    extras = tuple(
        ExtraField(key=f.field_key, source_kind=f.source_kind, source_ref=f.source_ref, data_type=f.data_type, normalizer=f.normalizer)
        for f in fields
        if f.maps_to is None
    )
    return [
        PatternSpec(
            provider_key=fmt.provider_key,
            package_name=fmt.package_name,
            pattern_name=p.pattern_name,
            regex=p.regex,
            transaction_type=p.transaction_type,
            priority=p.priority,
            sample_text=p.sample_text,
            pattern_id=p.id,
            format_priority=fmt.priority,
            groups=groups,
            extras=extras,
        )
        for p in patterns
    ]


async def load_specs(session: AsyncSession) -> list[PatternSpec]:
    """Mọi pattern đang bật của mọi định dạng đang ``active``: đầu vào của ``PatternMatcher``. Định dạng draft/archived không bao giờ được dùng."""
    rows = (
        await session.execute(
            select(ProviderPattern, ProviderFormat)
            .join(ProviderFormat, ProviderFormat.id == ProviderPattern.format_id)
            .where(ProviderPattern.is_active.is_(True), ProviderFormat.status == "active")
            .order_by(ProviderFormat.priority, ProviderPattern.priority, ProviderPattern.pattern_name)
        )
    ).all()
    format_ids = {fmt.id for _, fmt in rows}
    fields_by_format: dict[uuid.UUID, list[ProviderFormatField]] = {}
    if format_ids:
        for field in (await session.execute(select(ProviderFormatField).where(ProviderFormatField.format_id.in_(format_ids)))).scalars():
            fields_by_format.setdefault(field.format_id, []).append(field)

    specs: list[PatternSpec] = []
    for pattern, fmt in rows:
        specs.extend(specs_for_format(fmt, [pattern], fields_by_format.get(fmt.id, [])))
    return specs


# ---------------------------------------------------------------- khai báo trường và mẫu dựng từ regex
def derive_fields(regexes: Sequence[str]) -> list[dict[str, Any]]:
    """Trường hợp đồng mà các regex thực sự dùng (theo tên group mặc định). ``amount`` luôn bắt buộc."""
    present: set[str] = set()
    for pattern in regexes:
        present |= set(regex.compile(pattern, _FLAGS).groupindex)
    fields = []
    for contract_key, group in CONTRACT_GROUP.items():
        if group in present:
            data_type, normalizer = _FIELD_DEFAULTS[contract_key]
            fields.append(
                {
                    "field_key": group,
                    "maps_to": contract_key,
                    "data_type": data_type,
                    "is_required": contract_key == "amount_cents",
                    "source_kind": "regex_group",
                    "source_ref": group,
                    "normalizer": normalizer,
                }
            )
    return fields


def expected_from_match(match: PatternMatch) -> dict[str, Any]:
    """Các trường bóc được, theo tên khóa của ``expected_fields`` (``transacted_at`` là chữ gốc, chưa phân tích)."""
    expected: dict[str, Any] = {"amount_cents": match.amount_cents}
    for key, value in (
        ("transaction_type", match.transaction_type.value if match.transaction_type else None),
        ("merchant_name", match.merchant_name),
        ("description", match.description),
        ("balance_after_cents", match.balance_after_cents),
        ("transacted_at", match.occurred_at_raw),
    ):
        if value is not None:
            expected[key] = value
    expected.update(match.extra_fields)
    return expected


# ---------------------------------------------------------------- kiểm tra và cổng kích hoạt
async def _load(session: AsyncSession, format_id: uuid.UUID):
    fmt = await session.get(ProviderFormat, format_id)
    if fmt is None:
        raise FormatError([f"không có định dạng {format_id}"])
    patterns = (
        await session.execute(
            select(ProviderPattern).where(ProviderPattern.format_id == format_id, ProviderPattern.is_active.is_(True)).order_by(ProviderPattern.priority, ProviderPattern.pattern_name)
        )
    ).scalars().all()
    fields = (await session.execute(select(ProviderFormatField).where(ProviderFormatField.format_id == format_id))).scalars().all()
    samples = (
        await session.execute(select(ProviderFormatSample).where(ProviderFormatSample.format_id == format_id).order_by(ProviderFormatSample.created_at, ProviderFormatSample.id))
    ).scalars().all()
    return fmt, patterns, fields, samples


async def check_format(session: AsyncSession, format_id: uuid.UUID) -> list[str]:
    """Mọi lý do định dạng này CHƯA đủ điều kiện chạy (rỗng = đủ). Không đổi dữ liệu."""
    fmt, patterns, fields, samples = await _load(session, format_id)
    problems: list[str] = []

    if not patterns:
        problems.append("không có pattern nào đang bật")
    usable: list[ProviderPattern] = []
    groups: set[str] = set()
    for pattern in patterns:
        try:
            groups |= set(regex.compile(pattern.regex, _FLAGS).groupindex)
        except regex.error as error:
            problems.append(f"pattern {pattern.pattern_name}: regex không biên dịch được ({error})")
            continue
        usable.append(pattern)

    for field in fields:
        if field.source_kind == "regex_group" and field.source_ref not in groups:
            problems.append(f"trường {field.field_key}: không pattern nào có named group {field.source_ref!r}")

    if not samples:
        problems.append("chưa có mẫu kiểm tra nào (cổng kích hoạt cần ít nhất một mẫu)")

    matcher = PatternMatcher(specs_for_format(fmt, usable, fields))
    required = [f.maps_to or f.field_key for f in fields if f.is_required]
    for number, sample in enumerate(samples, start=1):
        matched = matcher.match(fmt.package_name, sample.sample_text)
        if matched is None:
            problems.append(f"mẫu #{number}: không pattern nào khớp")
            continue
        actual = expected_from_match(matched)
        for key, wanted in sample.expected_fields.items():
            if actual.get(key) != wanted:
                problems.append(f"mẫu #{number} (khớp {matched.pattern_name}): {key} mong đợi {wanted!r}, nhận {actual.get(key)!r}")
        for key in required:
            if key not in actual:
                problems.append(f"mẫu #{number} (khớp {matched.pattern_name}): thiếu trường bắt buộc {key}")
    return problems


def _audit(session: AsyncSession, fmt: ProviderFormat, action: str, before: dict | None, after: dict | None, actor_hash: str, reason: str | None) -> None:
    session.add(BillRuleAudit(resource="provider_format", resource_key=str(fmt.id), action=action, before=before, after=after, actor_hash=actor_hash, reason=reason))


def _snapshot(fmt: ProviderFormat) -> dict[str, Any]:
    return {"package_name": fmt.package_name, "version": fmt.version, "status": fmt.status, "priority": fmt.priority}


async def activate(session: AsyncSession, format_id: uuid.UUID, *, actor_hash: str, reason: str | None = None) -> ProviderFormat:
    """``draft`` -> ``active`` nếu MỌI mẫu khớp. Không commit: người gọi quyết định."""
    fmt = await session.get(ProviderFormat, format_id)
    if fmt is None:
        raise FormatError([f"không có định dạng {format_id}"])
    if fmt.status != "draft":
        raise FormatError([f"chỉ định dạng draft mới kích hoạt được (hiện là {fmt.status}); muốn dùng lại bản cũ hãy clone nó"])
    if problems := await check_format(session, format_id):
        raise FormatError(problems)
    before = _snapshot(fmt)
    fmt.status, fmt.updated_at = "active", func.now()
    await session.flush()
    _audit(session, fmt, "activate", before, _snapshot(fmt), actor_hash, reason)
    return fmt


async def archive(session: AsyncSession, format_id: uuid.UUID, *, actor_hash: str, reason: str | None = None) -> ProviderFormat:
    """``draft``/``active`` -> ``archived``. Không commit. Gỡ bản active cuối cùng của một package nghĩa là package đó quay về nhánh generic."""
    fmt = await session.get(ProviderFormat, format_id)
    if fmt is None:
        raise FormatError([f"không có định dạng {format_id}"])
    if fmt.status == "archived":
        raise FormatError(["định dạng đã archived"])
    before = _snapshot(fmt)
    fmt.status, fmt.updated_at = "archived", func.now()
    await session.flush()
    _audit(session, fmt, "deactivate", before, _snapshot(fmt), actor_hash, reason)
    return fmt


async def clone(session: AsyncSession, package_name: str, *, actor_hash: str, reason: str | None = None) -> ProviderFormat:
    """Bản ``draft`` mới (version + 1) sao từ phiên bản MỚI NHẤT của package, kèm pattern, trường và mẫu. Không commit."""
    source = (
        await session.execute(select(ProviderFormat).where(ProviderFormat.package_name == package_name).order_by(ProviderFormat.version.desc()).limit(1))
    ).scalar_one_or_none()
    if source is None:
        raise FormatError([f"package {package_name!r} chưa có định dạng nào để nhân bản"])

    new = ProviderFormat(
        provider_key=source.provider_key, package_name=source.package_name, version=source.version + 1, status="draft",
        priority=source.priority, notes=source.notes, created_by=actor_hash,
    )
    session.add(new)
    await session.flush()
    for pattern in (await session.execute(select(ProviderPattern).where(ProviderPattern.format_id == source.id))).scalars():
        session.add(ProviderPattern(
            format_id=new.id, pattern_name=pattern.pattern_name, regex=pattern.regex,
            transaction_type=pattern.transaction_type, priority=pattern.priority, sample_text=pattern.sample_text, is_active=pattern.is_active, notes=pattern.notes,
        ))
    for field in (await session.execute(select(ProviderFormatField).where(ProviderFormatField.format_id == source.id))).scalars():
        session.add(ProviderFormatField(
            format_id=new.id, field_key=field.field_key, maps_to=field.maps_to, data_type=field.data_type, is_required=field.is_required, source_kind=field.source_kind,
            source_ref=field.source_ref, normalizer=field.normalizer, normalizer_arg=field.normalizer_arg, is_sensitive=field.is_sensitive,
        ))
    for sample in (await session.execute(select(ProviderFormatSample).where(ProviderFormatSample.format_id == source.id))).scalars():
        session.add(ProviderFormatSample(format_id=new.id, sample_text=sample.sample_text, expected_fields=sample.expected_fields))
    await session.flush()
    _audit(session, new, "create", {"cloned_from": str(source.id), "version": source.version}, _snapshot(new), actor_hash, reason)
    return new


# ---------------------------------------------------------------- dữ liệu mặc định (seed)
async def format_for_seed(session: AsyncSession, row: dict[str, Any]) -> ProviderFormat:
    """Định dạng để gắn một pattern mặc định vào: bản ``active`` thấp nhất của package, không có thì bản mới nhất, chưa có gì thì tạo v1 ``active`` (seed là dữ liệu tin cậy)."""
    formats = (await session.execute(select(ProviderFormat).where(ProviderFormat.package_name == row["package_name"]).order_by(ProviderFormat.version))).scalars().all()
    active = [f for f in formats if f.status == "active"]
    if active:
        return active[0]
    if formats:
        return formats[-1]
    fmt = ProviderFormat(provider_key=row["provider_key"], package_name=row["package_name"], version=1, status="active", created_by=SYSTEM_ACTOR, notes="Tạo từ dữ liệu mặc định")
    session.add(fmt)
    await session.flush()
    return fmt


async def sync_fields_and_sample(session: AsyncSession, fmt: ProviderFormat, pattern: ProviderPattern) -> None:
    """Bổ sung khai báo trường còn thiếu (theo named group của pattern) và một mẫu kiểm tra dựng từ ``sample_text`` của pattern. Idempotent, chỉ thêm."""
    rows = (await session.execute(select(ProviderFormatField.field_key, ProviderFormatField.maps_to).where(ProviderFormatField.format_id == fmt.id))).all()
    have_keys = {row[0] for row in rows}
    have_maps = {row[1] for row in rows if row[1]}
    for field in derive_fields([pattern.regex]):
        if field["field_key"] in have_keys or field["maps_to"] in have_maps:
            continue
        session.add(ProviderFormatField(format_id=fmt.id, **field))
        have_keys.add(field["field_key"])
        have_maps.add(field["maps_to"])

    if not pattern.sample_text:
        await session.flush()
        return
    matched = PatternMatcher([PatternSpec(
        provider_key=fmt.provider_key, package_name=fmt.package_name, pattern_name=pattern.pattern_name, regex=pattern.regex,
        transaction_type=pattern.transaction_type, priority=pattern.priority,
    )]).match(fmt.package_name, pattern.sample_text)
    if matched is None:
        logger.warning("provider_format_sample_not_matching", pattern=pattern.pattern_name)
        await session.flush()
        return
    exists = (
        await session.execute(select(ProviderFormatSample.id).where(ProviderFormatSample.format_id == fmt.id, ProviderFormatSample.sample_text == pattern.sample_text))
    ).first()
    if exists is None:
        session.add(ProviderFormatSample(format_id=fmt.id, sample_text=pattern.sample_text, expected_fields=expected_from_match(matched)))
    await session.flush()
