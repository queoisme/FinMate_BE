"""API lưu trữ dành cho ocr-service (nội bộ, xác thực bằng khóa nội bộ): thay cho việc ocr-service tự kết nối và chạy SQL trên DB của AI Service.

Nhóm endpoint: kiến thức (thương hiệu, từ khóa) và phiếu bầu thương hiệu; cửa hàng người dùng đã chốt; bill đã gắn nhãn và bộ chuẩn (gold); bill lạ chờ duyệt;
lịch sử huấn luyện mô hình danh mục bill. Tài liệu cấu hình (từ vựng, ngưỡng) đọc ở ``/api/v1/admin/config/documents/{name}`` như trước.
Lỗi là JSON ``{"error": {"code", "message"}}`` cùng mã HTTP (404/422), giống ``/admin/rules``. Không log nội dung chữ hóa đơn hay tên cửa hàng."""

import uuid
from typing import Annotated

from fastapi import APIRouter, Depends, Query
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.bill.rules_admin import RuleError
from app.schemas.ocr_store import (
    BrandVote,
    GoldClaim,
    GoldRename,
    KnowledgeSeed,
    LabeledSampleIn,
    LabelSource,
    MerchantMemoryPut,
    TrainingFail,
    TrainingFinish,
    TrainingStart,
    UnknownScanIn,
    UnknownScanLabel,
)
from app.services import ocr_store as store

router = APIRouter(prefix="/ocr-store", tags=["ocr-store"], dependencies=[Depends(verify_internal_api_key)])

Session = Annotated[AsyncSession, Depends(get_db)]
UserHash = Annotated[str, Query(pattern=r"^[0-9a-f]{64}$")]


# ------------------------------------------------------------------ kiến thức
@router.get("/knowledge")
async def get_knowledge(session: Session, include_inactive: bool = False) -> dict:
    """Thương hiệu và từ khóa đang bật (hoặc tất cả khi ``include_inactive``)."""
    return await store.knowledge(session, include_inactive)


@router.post("/knowledge/seed")
async def seed_knowledge(body: KnowledgeSeed, session: Session) -> dict:
    """Chỉ THÊM mục còn thiếu; trả số dòng mới."""
    added = await store.seed_knowledge(session, body.brands, body.keywords)
    await session.commit()
    return added


@router.post("/brands/vote")
async def propose_brand(body: BrandVote, session: Session) -> dict:
    created = await store.propose_brand(session, body)
    await session.commit()
    return {"created": created}


@router.post("/brands/activate-vote")
async def activate_voted_brand(body: UnknownScanLabel, session: Session) -> dict:
    """Bật thương hiệu chờ duyệt do phiếu bầu thêm; ``merchant_key`` ở đây là ``brand_key``."""
    activated = await store.activate_voted_brand(session, body.merchant_key)
    await session.commit()
    return {"activated": activated}


@router.get("/consensus/merchants")
async def get_merchant_consensus(session: Session) -> dict:
    return {"items": await store.merchant_consensus(session)}


# ------------------------------------------------------------------ cửa hàng người dùng đã chốt
@router.get("/merchant-memory")
async def get_merchant_memory(user_id_hash: UserHash, session: Session) -> dict:
    return {"items": await store.merchant_memory(session, user_id_hash)}


@router.put("/merchant-memory")
async def put_merchant_memory(body: MerchantMemoryPut, session: Session) -> dict:
    await store.put_merchant_memory(session, body)
    await session.commit()
    return {"ok": True}


# ------------------------------------------------------------------ bill đã gắn nhãn, bộ chuẩn
@router.post("/labeled-samples")
async def append_labeled_sample(body: LabeledSampleIn, session: Session) -> dict:
    await store.append_labeled(session, body)
    await session.commit()
    return {"ok": True}


@router.get("/labeled-samples")
async def list_labeled_samples(session: Session, limit: Annotated[int, Query(ge=1, le=1000)] = 500, offset: Annotated[int, Query(ge=0)] = 0) -> dict:
    return await store.list_labeled(session, limit, offset)


@router.get("/labeled-samples/count")
async def count_labeled_samples(label_source: LabelSource, session: Session) -> dict:
    return {"count": await store.count_labeled_by(session, label_source)}


@router.post("/gold/claim")
async def claim_gold(body: GoldClaim, session: Session) -> dict:
    result = await store.claim_gold(session, body.total, body.category_slug, body.dry_run)
    await session.commit()
    return {"result": result}


@router.get("/gold/merchant-names")
async def get_gold_merchant_names(session: Session) -> dict:
    return await store.gold_merchant_names(session)


@router.post("/gold/rename")
async def rename_gold(body: GoldRename, session: Session) -> dict:
    updated = await store.rename_gold(session, body.old, body.new)
    await session.commit()
    return {"updated": updated}


# ------------------------------------------------------------------ bill lạ chờ duyệt
@router.post("/unknown-scans")
async def record_unknown_scan(body: UnknownScanIn, session: Session) -> dict:
    await store.record_unknown_scan(session, body)
    await session.commit()
    return {"ok": True}


@router.post("/unknown-scans/label")
async def label_unknown_scans(body: UnknownScanLabel, session: Session) -> dict:
    updated = await store.label_unknown_scans(session, body.merchant_key)
    await session.commit()
    return {"updated": updated}


# ------------------------------------------------------------------ lịch sử huấn luyện
@router.post("/training/jobs")
async def start_training(body: TrainingStart, session: Session) -> dict:
    job_id = await store.start_training(session, body)
    await session.commit()
    return {"job_id": job_id}


@router.post("/training/jobs/{job_id}/fail")
async def fail_training(job_id: uuid.UUID, body: TrainingFail, session: Session) -> dict:
    if not await store.fail_training(session, str(job_id), body.error):
        raise RuleError(404, "not_found", "Không có lần huấn luyện này.")
    await session.commit()
    return {"ok": True}


@router.post("/training/jobs/{job_id}/finish")
async def finish_training(job_id: uuid.UUID, body: TrainingFinish, session: Session) -> dict:
    version_id = await store.finish_training(session, str(job_id), body)
    if version_id is None:
        raise RuleError(404, "not_found", "Không có lần huấn luyện này.")
    await session.commit()
    return {"model_version_id": version_id}
