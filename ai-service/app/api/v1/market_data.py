"""``POST /api/v1/market-data/extract``: backend gửi text một bài báo công khai, nhận thông tin giá cả có trích dẫn. Cần khóa nội bộ. Xem ``docs/market-data-api.md``."""

from dataclasses import asdict
from typing import Annotated, Literal

import structlog
from fastapi import APIRouter, Depends
from pydantic import BaseModel, Field

from app.api.deps import verify_internal_api_key
from app.bill.gemini import GeminiClient, GeminiFailed
from app.core.config import settings
from app.services import secret_store
from app.services.market_extractor import MAX_TEXT_CHARS, MAX_TITLE_CHARS, MarketExtractionFailed, extract_signals

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

# Client riêng: bài báo đọc theo lô không được làm hóa đơn hay chat mất AI khi cầu dao mở, và ngược lại.
_market_gemini = GeminiClient(
    secret_store.getter("gemini_api_key"),
    settings.gemini_model,
    settings.gemini_timeout_seconds,
    fallback_models=tuple(m.strip() for m in settings.gemini_fallback_models.split(",") if m.strip()),
    health_channel="market",
)


def get_market_gemini_client() -> GeminiClient:
    """Test thay bằng bản giả qua ``dependency_overrides``."""
    return _market_gemini


class MarketTopicIn(BaseModel):
    key: str = Field(pattern=r"^[a-z][a-z_]{1,28}[a-z]$")
    description: str = Field(min_length=1, max_length=200)


class MarketExtractRequest(BaseModel):
    title: str = Field(default="", max_length=MAX_TITLE_CHARS)
    text: str = Field(min_length=1, max_length=MAX_TEXT_CHARS)
    published_at: str | None = Field(default=None, max_length=40)
    topics: list[MarketTopicIn] = Field(min_length=1, max_length=20)


class MarketSignalOut(BaseModel):
    topic: str
    direction: str
    fact_status: str
    change_percent: float | None
    effective_date: str | None
    evidence: str


class MarketExtractResponse(BaseModel):
    status: Literal["ok", "not_configured", "failed"]
    signals: list[MarketSignalOut] = []


@router.post("/market-data/extract", response_model=MarketExtractResponse)
async def extract(
    body: MarketExtractRequest,
    client: Annotated[GeminiClient, Depends(get_market_gemini_client)],
) -> MarketExtractResponse:
    # Không có key thì báo rõ để bài ở lại chờ. Không đoán, và không tính là lỗi của nhà cung cấp.
    if not client.primary_configured:
        return MarketExtractResponse(status="not_configured")
    try:
        signals = await extract_signals(client, body.title, body.text, body.published_at, {t.key: t.description for t in body.topics})
    except (GeminiFailed, MarketExtractionFailed) as error:
        logger.warning("market_extract_failed", error=error.args[0] if error.args else "unknown")
        return MarketExtractResponse(status="failed")
    return MarketExtractResponse(status="ok", signals=[MarketSignalOut(**asdict(s)) for s in signals])
