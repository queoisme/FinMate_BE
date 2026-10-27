"""API quản trị luật phân loại (nội bộ): chỉ backend gọi, sau khi đã xác thực admin. Xác thực ở đây là khóa nội bộ; người thực hiện đi kèm ở tiêu đề
``X-Actor-Hash`` (SHA-256 id admin) và được ghi vào nhật ký. Mọi thay đổi có hiệu lực NGAY (không chờ nhịp nạp lại 60 giây).

Chỉ chỉnh DỮ LIỆU luật (thương hiệu, từ khóa hóa đơn, bộ nhớ tên món, chỉ dẫn cho Gemini); logic và từ điển chat nằm trong code. Không log nội dung luật."""

import uuid
from typing import Annotated

from fastapi import APIRouter, Depends, Header, Query, Response, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.api.v1 import bill as bill_api
from app.api.v1.bill import get_bill_knowledge
from app.bill import rules_admin as rules
from app.bill.item_classifier import item_key
from app.bill.knowledge import BillKnowledge
from app.bill.rules_preview import preview
from app.db.models.bill import BILL_CATEGORY_GROUPS, BillItemCache
from app.services import category_registry
from app.schemas.rules_admin import (
    ActiveBody,
    BrandUpsert,
    InstructionCreate,
    ItemCachePut,
    KeywordCreate,
    PreviewRequest,
    RevertBody,
)

router = APIRouter(prefix="/admin/rules", tags=["rules-admin"], dependencies=[Depends(verify_internal_api_key)])

Session = Annotated[AsyncSession, Depends(get_db)]
Knowledge = Annotated[BillKnowledge, Depends(get_bill_knowledge)]
Actor = Annotated[str, Header(alias="X-Actor-Hash", min_length=64, max_length=64, pattern=r"^[0-9a-f]{64}$")]
Limit = Annotated[int, Query(ge=1, le=200)]
Offset = Annotated[int, Query(ge=0)]


def _page(items: list, total: int, limit: int, offset: int) -> dict:
    return {"items": items, "total": total, "limit": limit, "offset": offset}


def _refresh_rules(knowledge: BillKnowledge) -> None:
    """Bộ phân loại nạp lại bảng luật ở lần dùng kế tiếp thay vì chờ hết nhịp 60 giây."""
    if mark_stale := getattr(knowledge, "mark_stale", None):
        mark_stale()


@router.get("/meta")
async def meta() -> dict:
    """Giá trị hợp lệ để dựng ô chọn trên giao diện quản trị."""
    return {
        "category_groups": list(BILL_CATEGORY_GROUPS),  # nhóm của thương hiệu và từ khóa hóa đơn
        "backend_slugs": category_registry.spend_list(),  # nhóm của bộ nhớ tên món (gồm danh mục backend mới đồng bộ)
        "keyword_kinds": list(rules.RUNTIME_KEYWORD_KINDS),
        "max_keyword_chars": rules.MAX_KEYWORD_CHARS,
        "max_instruction_chars": rules.MAX_INSTRUCTION_CHARS,
    }


# ---------------------------------------------------------------- thương hiệu

@router.get("/brands")
async def list_brands(session: Session, q: str | None = None, category_slug: str | None = None, active: bool | None = None, limit: Limit = 50, offset: Offset = 0) -> dict:
    items, total = await rules.list_brands(session, q=q, category_slug=category_slug, active=active, limit=limit, offset=offset)
    return _page(items, total, limit, offset)


@router.put("/brands/{brand_key}")
async def upsert_brand(brand_key: str, body: BrandUpsert, actor: Actor, session: Session, knowledge: Knowledge) -> dict:
    row = await rules.upsert_brand(session, brand_key, category_slug=body.category_slug, display_name=body.display_name, whole_word=body.whole_word, actor=actor, reason=body.reason)
    _refresh_rules(knowledge)
    return row


@router.patch("/brands/{brand_key}/active")
async def set_brand_active(brand_key: str, body: ActiveBody, actor: Actor, session: Session, knowledge: Knowledge) -> dict:
    row = await rules.set_brand_active(session, brand_key, body.is_active, actor=actor, reason=body.reason)
    _refresh_rules(knowledge)
    return row


# ---------------------------------------------------------------- từ khóa

@router.get("/keywords")
async def list_keywords(
    session: Session, kind: str | None = None, category_slug: str | None = None, q: str | None = None, active: bool | None = None, limit: Limit = 50, offset: Offset = 0,
) -> dict:
    items, total = await rules.list_keywords(session, kind=kind, category_slug=category_slug, q=q, active=active, limit=limit, offset=offset)
    return _page(items, total, limit, offset)


@router.post("/keywords", status_code=status.HTTP_201_CREATED)
async def create_keyword(body: KeywordCreate, actor: Actor, session: Session, knowledge: Knowledge) -> dict:
    row = await rules.create_keyword(session, kind=body.kind, category_slug=body.category_slug, keyword=body.keyword, actor=actor, reason=body.reason)
    _refresh_rules(knowledge)
    return row


@router.patch("/keywords/{keyword_id}/active")
async def set_keyword_active(keyword_id: uuid.UUID, body: ActiveBody, actor: Actor, session: Session, knowledge: Knowledge) -> dict:
    row = await rules.set_keyword_active(session, keyword_id, body.is_active, actor=actor, reason=body.reason)
    _refresh_rules(knowledge)
    return row


# ---------------------------------------------------------------- bộ nhớ tên món

@router.get("/item-cache")
async def list_item_cache(session: Session, q: str | None = None, limit: Limit = 50, offset: Offset = 0) -> dict:
    items, total = await rules.list_item_cache(session, q=q, limit=limit, offset=offset)
    return _page(items, total, limit, offset)


@router.put("/item-cache")
async def put_item_cache(body: ItemCachePut, actor: Actor, session: Session) -> dict:
    return await rules.put_item_cache(session, body.name, body.category_slug, actor=actor, reason=body.reason)


@router.delete("/item-cache", status_code=status.HTTP_204_NO_CONTENT)
async def delete_item_cache(name: Annotated[str, Query(max_length=200)], actor: Actor, session: Session) -> Response:
    await rules.delete_item_cache(session, name, actor=actor)
    return Response(status_code=status.HTTP_204_NO_CONTENT)


# ---------------------------------------------------------------- chỉ dẫn Gemini

@router.get("/ai-instructions")
async def list_instructions(session: Session) -> dict:
    items = await rules.list_instructions(session)
    return _page(items, len(items), len(items), 0)


@router.post("/ai-instructions", status_code=status.HTTP_201_CREATED)
async def create_instruction(body: InstructionCreate, actor: Actor, session: Session) -> dict:
    row = await rules.create_instruction(session, body.instruction_text, actor=actor, reason=body.reason)
    bill_api.invalidate_ai_instruction()
    return row


@router.post("/ai-instructions/{instruction_id}/activate")
async def activate_instruction(instruction_id: uuid.UUID, actor: Actor, session: Session) -> dict:
    row = await rules.activate_instruction(session, instruction_id, actor=actor)
    bill_api.invalidate_ai_instruction()
    return row


# ---------------------------------------------------------------- thử trước khi lưu

@router.post("/preview")
async def preview_rules(body: PreviewRequest, session: Session) -> dict:
    """Luật hiện tại trả lời gì cho cửa hàng/dòng chữ/tên món này, và sẽ trả lời gì nếu thêm hoặc tắt các luật đề xuất. Không ghi gì."""
    result = preview(
        merchant_name=body.merchant_name, lines=body.lines, item_name=body.item_name,
        proposed_brands=[b.model_dump() for b in body.proposed_brands], proposed_keywords=[k.model_dump() for k in body.proposed_keywords],
    )
    if body.item_name and (key := item_key(body.item_name)):
        cached = (await session.execute(select(BillItemCache.category_slug).where(BillItemCache.item_key == key))).scalar_one_or_none()
        for side in ("current", "proposed"):
            if result[side] and result[side]["item"] is not None:
                result[side]["item"]["cache_slug"] = cached  # bộ nhớ tên món không đổi theo đề xuất; hiện để thấy nó có đè luật không
    return result


# ---------------------------------------------------------------- lịch sử và hoàn tác

@router.get("/history")
async def history(session: Session, resource: str | None = None, resource_key: str | None = None, limit: Limit = 50, offset: Offset = 0) -> dict:
    items, total = await rules.list_history(session, resource=resource, resource_key=resource_key, limit=limit, offset=offset)
    return _page(items, total, limit, offset)


@router.post("/history/{audit_id}/revert")
async def revert(audit_id: uuid.UUID, actor: Actor, session: Session, knowledge: Knowledge, body: RevertBody | None = None) -> dict:
    result = await rules.revert(session, audit_id, actor=actor, reason=body.reason if body else None)
    _refresh_rules(knowledge)
    return result
