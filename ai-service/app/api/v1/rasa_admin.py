"""Quản trị Rasa (nội bộ): chỉ backend gọi, sau khi đã xác thực admin. Rasa nằm trong mạng docker (không mở cổng ra ngoài) nên trình duyệt không tới được; đường này chỉ ĐỌC trạng thái và thử một câu.

Huấn luyện và sửa dữ liệu mẫu KHÔNG có API ở đây có chủ ý: chúng đi qua ``scripts/nlu_model.py train|eval|promote`` để có cổng đo trước khi thay model (xem ``rasa/README.md``).
Rasa tắt hoặc chưa cấu hình thì trả ``available: false`` (200), không ném lỗi: trang admin hiện trạng thái thay vì báo hỏng."""

from typing import Annotated

import httpx
from fastapi import APIRouter, Depends, Header
from pydantic import BaseModel, Field
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db, verify_internal_api_key
from app.core.config import settings
from app.services import nlu_models

router = APIRouter(prefix="/admin/rasa", tags=["rasa-admin"], dependencies=[Depends(verify_internal_api_key)])

TIMEOUT_SECONDS = 3.0
MAX_RANKING = 5


class ParseBody(BaseModel):
    text: Annotated[str, Field(min_length=1, max_length=500)]


class ActivateModelBody(BaseModel):
    """``expected_active``: bản đang chạy mà người dùng trang admin nhìn thấy (None = không kiểm). ``force`` bỏ qua cổng đo, bắt buộc kèm ``reason``."""

    force: bool = False
    expected_active: Annotated[str | None, Field(max_length=64)] = None
    reason: Annotated[str | None, Field(max_length=300)] = None


Session = Annotated[AsyncSession, Depends(get_db)]
Actor = Annotated[str, Header(alias="X-Actor-Hash", min_length=64, max_length=64, pattern=r"^[0-9a-f]{64}$")]


def _client() -> httpx.AsyncClient:
    return httpx.AsyncClient(timeout=TIMEOUT_SECONDS)


@router.get("/status")
async def status() -> dict:
    if not settings.rasa_url:
        return {"configured": False, "available": False}
    base = settings.rasa_url.rstrip("/")
    try:
        async with _client() as client:
            version = (await client.get(f"{base}/version")).json()
            state = (await client.get(f"{base}/status")).json()
    except (httpx.HTTPError, ValueError):
        return {"configured": True, "available": False}
    return {
        "configured": True,
        "available": True,
        "version": version.get("version"),
        "model_file": state.get("model_file"),
        "model_id": state.get("model_id"),
        "num_active_training_jobs": state.get("num_active_training_jobs"),
    }


@router.get("/models")
async def list_models(session: Session) -> dict:
    """Mọi phiên bản model đã đăng ký (sổ ``nlu_model_versions``) kèm số đo, bản đang chạy theo sổ và theo Rasa, và từng bản có qua cổng đo để thay bản đang chạy không."""
    return await nlu_models.overview(session)


@router.post("/models/{version}/activate", status_code=202)
async def activate_model(version: str, body: ActivateModelBody, session: Session, actor: Actor) -> dict:
    """Bắt đầu đưa một phiên bản thành model đang chạy (kể cả quay về bản cũ): 202 ngay, Rasa nạp nóng ở nền (vài phút, không khởi động lại, chat chạy bằng luật trong lúc nạp).
    Tiến trình và lỗi nằm ở ``activation`` của ``GET /models``. Không qua cổng đo thì 422 ``blocked_by_gate``; ``force: true`` kèm ``reason`` để ép."""
    return await nlu_models.activate(session, version, actor=actor, expected_active=body.expected_active, force=body.force, reason=body.reason)


@router.post("/parse")
async def parse(body: ParseBody) -> dict:
    """Ý định, xếp hạng và thực thể Rasa đọc được từ một câu. Không ghi gì, không log nội dung."""
    if not settings.rasa_url:
        return {"configured": False, "available": False}
    try:
        async with _client() as client:
            response = await client.post(f"{settings.rasa_url.rstrip('/')}/model/parse", json={"text": body.text})
            response.raise_for_status()
            data = response.json()
    except (httpx.HTTPError, ValueError):
        return {"configured": True, "available": False}
    return {
        "configured": True,
        "available": True,
        "intent": data.get("intent"),
        "intent_ranking": (data.get("intent_ranking") or [])[:MAX_RANKING],
        "entities": [{"entity": e.get("entity"), "value": e.get("value")} for e in data.get("entities") or []],
    }
