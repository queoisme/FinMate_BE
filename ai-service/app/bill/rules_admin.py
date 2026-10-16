"""Quản trị luật phân loại qua API: thương hiệu (``bill_brands``), từ khóa (``bill_keywords``), bộ nhớ tên món (``bill_item_cache``) và chỉ dẫn
cho Gemini (``bill_ai_instructions``). Mọi thay đổi ghi một dòng ``bill_rule_audit`` (trước/sau) nên xem được lịch sử và hoàn tác được.

Chỉ động tới DỮ LIỆU: logic (đọc số tiền, ngày, chọn tên cửa hàng/tổng tiền trên OCR) vẫn nằm trong code. Từ khóa loại ``chat`` không được nạp lúc chạy
(từ điển chat nằm trong code) nên không cho sửa ở đây. Người thực hiện luôn là ``actor`` = SHA-256 id admin, không bao giờ là id thật.
Không log nội dung luật.
"""

from __future__ import annotations

import uuid
from typing import Any

from sqlalchemy import func, select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.item_classifier import _rememberable, item_key
from app.bill.knowledge import merchant_key
from app.db.models.bill import (
    BILL_CATEGORY_GROUPS,
    BILL_CHOICE_SLUGS,
    BillAiInstruction,
    BillBrand,
    BillItemCache,
    BillKeyword,
    BillRuleAudit,
)

RUNTIME_KEYWORD_KINDS = ("merchant", "product")  # chỉ hai loại này được nạp vào bộ phân loại lúc chạy
MAX_KEYWORD_CHARS = 60
MIN_SAFE_KEYWORD_CHARS = 4  # ngắn hơn: dễ trùng vào từ khác (đã từng có lỗi "lau" / "lẩu")
MAX_DISPLAY_CHARS = 120
MAX_INSTRUCTION_CHARS = 20_000
DEFAULT_KEYWORD_PRIORITY = 100


class RuleError(Exception):
    """Lỗi nghiệp vụ của API quản trị; ``status`` là mã HTTP tương ứng (404 không có, 409 trùng, 422 dữ liệu sai)."""

    def __init__(self, status: int, code: str, message: str) -> None:
        super().__init__(message)
        self.status, self.code, self.message = status, code, message


def _invalid(code: str, message: str) -> RuleError:
    return RuleError(422, code, message)


def _not_found(what: str) -> RuleError:
    return RuleError(404, "not_found", f"Không tìm thấy {what}.")


# ------------------------------------------------------------------ biểu diễn

def _iso(value) -> str | None:
    return value.isoformat() if value is not None else None


def _brand(row: BillBrand) -> dict[str, Any]:
    return {
        "brand_key": row.brand_key, "category_slug": row.category_slug, "display_name": row.display_name, "whole_word": row.whole_word,
        "is_active": row.is_active, "source": row.source, "updated_at": _iso(row.updated_at),
    }


def _keyword(row: BillKeyword) -> dict[str, Any]:
    return {
        "id": str(row.id), "kind": row.kind, "category_slug": row.category_slug, "keyword": row.keyword,
        "category_priority": row.category_priority, "is_active": row.is_active, "source": row.source,
    }


def _cache(row: BillItemCache) -> dict[str, Any]:
    return {"item_key": row.item_key, "category_slug": row.category_slug, "hits": row.hits, "updated_at": _iso(row.updated_at)}


def _instruction(row: BillAiInstruction) -> dict[str, Any]:
    return {"id": str(row.id), "instruction_text": row.instruction_text, "is_active": row.is_active, "updated_at": _iso(row.updated_at)}


def _history(row: BillRuleAudit) -> dict[str, Any]:
    return {
        "id": str(row.id), "resource": row.resource, "resource_key": row.resource_key, "action": row.action, "before": row.before,
        "after": row.after, "actor_hash": row.actor_hash, "reason": row.reason, "created_at": _iso(row.created_at),
    }


def _log(session: AsyncSession, resource: str, key: str, action: str, before: dict | None, after: dict | None, actor: str, reason: str | None) -> None:
    session.add(BillRuleAudit(resource=resource, resource_key=key, action=action, before=before, after=after, actor_hash=actor, reason=reason))


async def _page(session: AsyncSession, query, count_query, limit: int, offset: int):
    total = (await session.execute(count_query)).scalar_one()
    rows = (await session.execute(query.limit(limit).offset(offset))).scalars().all()
    return rows, total


# ------------------------------------------------------------------ kiểm tra đầu vào (dùng chung với ô thử trước khi lưu)

def clean_brand(brand_key: str, category_slug: str, display_name: str = "") -> tuple[str, str, str]:
    """(khóa đã chuẩn hóa, nhóm, tên hiển thị) hoặc ``RuleError`` 422."""
    key = merchant_key(brand_key)
    if not key:
        raise _invalid("blank_key", "Tên thương hiệu không được để trống.")
    if category_slug not in BILL_CATEGORY_GROUPS:
        raise _invalid("invalid_category", f"Nhóm phải là một trong: {', '.join(BILL_CATEGORY_GROUPS)}.")
    display = display_name.strip() or key
    if len(display) > MAX_DISPLAY_CHARS:
        raise _invalid("display_too_long", f"Tên hiển thị tối đa {MAX_DISPLAY_CHARS} ký tự.")
    return key, category_slug, display


def clean_keyword(kind: str, category_slug: str, keyword: str) -> tuple[str, str, str]:
    """(loại, nhóm, từ khóa đã chuẩn hóa) hoặc ``RuleError`` 422."""
    if kind not in RUNTIME_KEYWORD_KINDS:
        raise _invalid("invalid_kind", f"Loại phải là một trong: {', '.join(RUNTIME_KEYWORD_KINDS)} (từ điển chat nằm trong code).")
    if category_slug not in BILL_CATEGORY_GROUPS:
        raise _invalid("invalid_category", f"Nhóm phải là một trong: {', '.join(BILL_CATEGORY_GROUPS)}.")
    word = merchant_key(keyword)
    if not word:
        raise _invalid("blank_keyword", "Từ khóa không được để trống.")
    if len(word) > MAX_KEYWORD_CHARS:
        raise _invalid("keyword_too_long", f"Từ khóa tối đa {MAX_KEYWORD_CHARS} ký tự.")
    if not any(c.isalpha() for c in word):
        raise _invalid("keyword_without_letters", "Từ khóa phải có chữ cái.")
    return kind, category_slug, word


# ------------------------------------------------------------------ thương hiệu

async def list_brands(session, *, q: str | None = None, category_slug: str | None = None, active: bool | None = None, limit: int = 50, offset: int = 0):
    where = []
    if q and (key := merchant_key(q)):
        where.append(BillBrand.brand_key.ilike(f"%{key}%"))
    if category_slug:
        where.append(BillBrand.category_slug == category_slug)
    if active is not None:
        where.append(BillBrand.is_active == active)
    rows, total = await _page(session, select(BillBrand).where(*where).order_by(BillBrand.brand_key), select(func.count()).select_from(BillBrand).where(*where), limit, offset)
    return [_brand(r) for r in rows], total


async def _brand_row(session: AsyncSession, key: str) -> BillBrand | None:
    return (await session.execute(select(BillBrand).where(BillBrand.brand_key == key))).scalar_one_or_none()


async def upsert_brand(session, brand_key: str, *, category_slug: str, display_name: str, whole_word: bool, actor: str, reason: str | None = None) -> dict:
    """Thêm hoặc sửa một thương hiệu. Sửa không đổi trạng thái bật/tắt (dùng ``set_brand_active``)."""
    key, category_slug, display = clean_brand(brand_key, category_slug, display_name)

    row = await _brand_row(session, key)
    before = _brand(row) if row else None
    if row is None:
        row = BillBrand(brand_key=key, category_slug=category_slug, display_name=display, whole_word=whole_word, source="admin")
        session.add(row)
    else:
        row.category_slug, row.display_name, row.whole_word, row.source, row.updated_at = category_slug, display, whole_word, "admin", func.now()
    await session.flush()
    await session.refresh(row)
    _log(session, "brand", key, "update" if before else "create", before, _brand(row), actor, reason)
    await session.commit()
    return _brand(row)


async def set_brand_active(session, brand_key: str, is_active: bool, *, actor: str, reason: str | None = None) -> dict:
    key = merchant_key(brand_key)
    row = await _brand_row(session, key) if key else None
    if row is None:
        raise _not_found("thương hiệu")
    if row.is_active == is_active:
        return _brand(row)
    before = _brand(row)
    row.is_active, row.updated_at = is_active, func.now()
    await session.flush()
    await session.refresh(row)
    _log(session, "brand", key, "activate" if is_active else "deactivate", before, _brand(row), actor, reason)
    await session.commit()
    return _brand(row)


# ------------------------------------------------------------------ từ khóa

async def list_keywords(session, *, kind: str | None = None, category_slug: str | None = None, q: str | None = None, active: bool | None = None, limit: int = 50, offset: int = 0):
    where = [BillKeyword.kind.in_(RUNTIME_KEYWORD_KINDS)]
    if kind:
        where.append(BillKeyword.kind == kind)
    if category_slug:
        where.append(BillKeyword.category_slug == category_slug)
    if q and (key := merchant_key(q)):
        where.append(BillKeyword.keyword.ilike(f"%{key}%"))
    if active is not None:
        where.append(BillKeyword.is_active == active)
    query = select(BillKeyword).where(*where).order_by(BillKeyword.kind, BillKeyword.category_slug, BillKeyword.keyword)
    rows, total = await _page(session, query, select(func.count()).select_from(BillKeyword).where(*where), limit, offset)
    return [_keyword(r) for r in rows], total


async def create_keyword(session, *, kind: str, category_slug: str, keyword: str, actor: str, reason: str | None = None) -> dict:
    """Thêm một từ khóa. Trùng với từ khóa đang bật → 409; trùng với từ khóa đã tắt → bật lại. Xung đột nhóm chỉ là cảnh báo (``warnings``)."""
    kind, category_slug, word = clean_keyword(kind, category_slug, keyword)

    existing = (await session.execute(select(BillKeyword).where(BillKeyword.kind == kind, BillKeyword.category_slug == category_slug, BillKeyword.keyword == word))).scalar_one_or_none()
    if existing is not None and existing.is_active:
        raise RuleError(409, "duplicate", "Từ khóa này đã có và đang bật.")

    warnings: list[dict[str, Any]] = []
    others = (await session.execute(select(BillKeyword.category_slug).where(BillKeyword.kind == kind, BillKeyword.keyword == word, BillKeyword.is_active, BillKeyword.category_slug != category_slug))).scalars().all()
    if others:
        warnings.append({"code": "same_keyword_other_category", "message": "Từ khóa này đã có ở nhóm khác; kết quả phụ thuộc thứ tự ưu tiên giữa các nhóm.", "categories": sorted(others)})
    if len(word) < MIN_SAFE_KEYWORD_CHARS:
        warnings.append({"code": "short_keyword", "message": "Từ khóa rất ngắn, dễ khớp nhầm vào từ khác.", "categories": []})

    if existing is not None:
        before = _keyword(existing)
        existing.is_active = True
        await session.flush()
        row, action = existing, "activate"
    else:
        priority = (await session.execute(select(func.min(BillKeyword.category_priority)).where(BillKeyword.kind == kind, BillKeyword.category_slug == category_slug))).scalar_one()
        row = BillKeyword(kind=kind, category_slug=category_slug, keyword=word, category_priority=priority if priority is not None else DEFAULT_KEYWORD_PRIORITY, source="admin")
        session.add(row)
        await session.flush()
        before, action = None, "create"
    _log(session, "keyword", str(row.id), action, before, _keyword(row), actor, reason)
    await session.commit()
    return {**_keyword(row), "warnings": warnings}


async def set_keyword_active(session, keyword_id: uuid.UUID, is_active: bool, *, actor: str, reason: str | None = None) -> dict:
    row = (await session.execute(select(BillKeyword).where(BillKeyword.id == keyword_id, BillKeyword.kind.in_(RUNTIME_KEYWORD_KINDS)))).scalar_one_or_none()
    if row is None:
        raise _not_found("từ khóa")
    if row.is_active == is_active:
        return _keyword(row)
    before = _keyword(row)
    row.is_active = is_active
    await session.flush()
    _log(session, "keyword", str(row.id), "activate" if is_active else "deactivate", before, _keyword(row), actor, reason)
    await session.commit()
    return _keyword(row)


# ------------------------------------------------------- bộ nhớ tên món

async def list_item_cache(session, *, q: str | None = None, limit: int = 50, offset: int = 0):
    where = [BillItemCache.item_key.ilike(f"%{key}%")] if q and (key := item_key(q)) else []
    query = select(BillItemCache).where(*where).order_by(BillItemCache.hits.desc(), BillItemCache.item_key)
    rows, total = await _page(session, query, select(func.count()).select_from(BillItemCache).where(*where), limit, offset)
    return [_cache(r) for r in rows], total


def _cache_key(name: str) -> str:
    key = item_key(name)
    if not key or not _rememberable(name, key):
        raise _invalid("invalid_item", "Tên món phải giống tên hàng thật (không phải mã, không quá ngắn hoặc quá dài).")
    return key


async def put_item_cache(session, name: str, category_slug: str, *, actor: str, reason: str | None = None) -> dict:
    """Ghi đè nhóm của một tên món cho MỌI người dùng (thắng câu trả lời Gemini đã lưu)."""
    key = _cache_key(name)
    if category_slug not in BILL_CHOICE_SLUGS:
        raise _invalid("invalid_category", f"Nhóm phải là một trong: {', '.join(BILL_CHOICE_SLUGS)}.")
    row = (await session.execute(select(BillItemCache).where(BillItemCache.item_key == key))).scalar_one_or_none()
    before = _cache(row) if row else None
    if row is None:
        row = BillItemCache(item_key=key, category_slug=category_slug)
        session.add(row)
    elif row.category_slug == category_slug:
        return _cache(row)
    else:
        row.category_slug, row.updated_at = category_slug, func.now()
    await session.flush()
    await session.refresh(row)
    _log(session, "item_cache", key, "update" if before else "create", before, _cache(row), actor, reason)
    await session.commit()
    return _cache(row)


async def delete_item_cache(session, name: str, *, actor: str, reason: str | None = None) -> None:
    key = item_key(name)
    row = (await session.execute(select(BillItemCache).where(BillItemCache.item_key == key))).scalar_one_or_none() if key else None
    if row is None:
        raise _not_found("tên món")
    before = _cache(row)
    await session.delete(row)
    _log(session, "item_cache", key, "delete", before, None, actor, reason)
    await session.commit()


# ---------------------------------------------------------- chỉ dẫn Gemini

async def list_instructions(session) -> list[dict]:
    rows = (await session.execute(select(BillAiInstruction).order_by(BillAiInstruction.updated_at.desc()))).scalars().all()
    return [_instruction(r) for r in rows]


async def _deactivate_instructions(session: AsyncSession) -> str | None:
    """Tắt chỉ dẫn đang dùng (chỉ mục duy nhất cho phép ĐÚNG MỘT dòng bật), trả id của nó."""
    previous = (await session.execute(select(BillAiInstruction.id).where(BillAiInstruction.is_active))).scalar_one_or_none()
    if previous is not None:
        await session.execute(update(BillAiInstruction).where(BillAiInstruction.id == previous).values(is_active=False))
    return str(previous) if previous else None


async def create_instruction(session, text: str, *, actor: str, reason: str | None = None) -> dict:
    """Thêm một phiên bản chỉ dẫn và bật nó (bản cũ được giữ lại để bật lại). Phần nhiệm vụ/an toàn và schema kết quả do code giữ, không bị ảnh hưởng."""
    body = text.strip()
    if not body:
        raise _invalid("blank_instruction", "Chỉ dẫn không được để trống.")
    if len(body) > MAX_INSTRUCTION_CHARS:
        raise _invalid("instruction_too_long", f"Chỉ dẫn tối đa {MAX_INSTRUCTION_CHARS} ký tự.")
    previous = await _deactivate_instructions(session)
    row = BillAiInstruction(instruction_text=body, is_active=True)
    session.add(row)
    await session.flush()
    await session.refresh(row)
    _log(session, "instruction", str(row.id), "create", None, {**_instruction(row), "previous_active_id": previous}, actor, reason)
    await session.commit()
    return _instruction(row)


async def activate_instruction(session, instruction_id: uuid.UUID, *, actor: str, reason: str | None = None) -> dict:
    row = (await session.execute(select(BillAiInstruction).where(BillAiInstruction.id == instruction_id))).scalar_one_or_none()
    if row is None:
        raise _not_found("chỉ dẫn")
    if row.is_active:
        return _instruction(row)
    previous = await _deactivate_instructions(session)
    row.is_active, row.updated_at = True, func.now()
    await session.flush()
    await session.refresh(row)
    _log(session, "instruction", str(row.id), "activate", {"previous_active_id": previous}, _instruction(row), actor, reason)
    await session.commit()
    return _instruction(row)


# ------------------------------------------------------- lịch sử và hoàn tác

async def list_history(session, *, resource: str | None = None, resource_key: str | None = None, limit: int = 50, offset: int = 0):
    where = []
    if resource:
        where.append(BillRuleAudit.resource == resource)
    if resource_key:
        where.append(BillRuleAudit.resource_key == resource_key)
    query = select(BillRuleAudit).where(*where).order_by(BillRuleAudit.created_at.desc(), BillRuleAudit.id.desc())
    rows, total = await _page(session, query, select(func.count()).select_from(BillRuleAudit).where(*where), limit, offset)
    return [_history(r) for r in rows], total


async def revert(session, audit_id: uuid.UUID, *, actor: str, reason: str | None = None) -> dict:
    """Đưa dòng luật về trạng thái TRƯỚC thay đổi của mục nhật ký này (và ghi thêm một mục ``revert``). Thương hiệu/từ khóa vừa tạo thì bị tắt, không bị xóa."""
    entry = (await session.execute(select(BillRuleAudit).where(BillRuleAudit.id == audit_id))).scalar_one_or_none()
    if entry is None:
        raise _not_found("mục lịch sử")
    before = entry.before

    if entry.resource == "brand":
        row = await _brand_row(session, entry.resource_key)
        if row is None:
            raise _not_found("thương hiệu")
        current = _brand(row)
        if before is None:
            row.is_active = False
        else:
            row.category_slug, row.display_name, row.whole_word, row.is_active, row.source = (
                before["category_slug"], before["display_name"], before["whole_word"], before["is_active"], before["source"])
        row.updated_at = func.now()
        await session.flush()
        await session.refresh(row)
        state = _brand(row)
    elif entry.resource == "keyword":
        row = (await session.execute(select(BillKeyword).where(BillKeyword.id == uuid.UUID(entry.resource_key)))).scalar_one_or_none()
        if row is None:
            raise _not_found("từ khóa")
        current = _keyword(row)
        row.is_active = before["is_active"] if before else False
        await session.flush()
        state = _keyword(row)
    elif entry.resource == "item_cache":
        row = (await session.execute(select(BillItemCache).where(BillItemCache.item_key == entry.resource_key))).scalar_one_or_none()
        current = _cache(row) if row else None
        if before is None:
            if row is not None:
                await session.delete(row)
            state = None
        else:
            if row is None:
                row = BillItemCache(item_key=entry.resource_key, category_slug=before["category_slug"], hits=before["hits"])
                session.add(row)
            else:
                row.category_slug, row.updated_at = before["category_slug"], func.now()
            await session.flush()
            await session.refresh(row)
            state = _cache(row)
    else:
        raise _invalid("not_revertable", "Chỉ dẫn Gemini được đổi bằng cách bật lại một phiên bản cũ (activate), không dùng hoàn tác.")

    _log(session, entry.resource, entry.resource_key, "revert", current, state, actor, reason)
    await session.commit()
    return {"resource": entry.resource, "resource_key": entry.resource_key, "state": state}
