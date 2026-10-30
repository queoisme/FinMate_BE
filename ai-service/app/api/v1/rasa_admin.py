"""Quản trị Rasa (nội bộ): chỉ backend gọi, sau khi đã xác thực admin. Rasa nằm trong mạng docker (không mở cổng ra ngoài) nên trình duyệt không tới được; đường này chỉ ĐỌC trạng thái và thử một câu.

Huấn luyện và sửa dữ liệu mẫu KHÔNG có API ở đây có chủ ý: chúng đi qua ``scripts/nlu_model.py train|eval|promote`` để có cổng đo trước khi thay model (xem ``rasa/README.md``).
Rasa tắt hoặc chưa cấu hình thì trả ``available: false`` (200), không ném lỗi: trang admin hiện trạng thái thay vì báo hỏng."""

from typing import Annotated

import httpx
from fastapi import APIRouter, Depends
from pydantic import BaseModel, Field

from app.api.deps import verify_internal_api_key
from app.core.config import settings

router = APIRouter(prefix="/admin/rasa", tags=["rasa-admin"], dependencies=[Depends(verify_internal_api_key)])

TIMEOUT_SECONDS = 3.0
MAX_RANKING = 5


class ParseBody(BaseModel):
    text: Annotated[str, Field(min_length=1, max_length=500)]


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
