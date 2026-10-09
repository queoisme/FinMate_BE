"""Đọc hóa đơn qua OCR service riêng: đổi câu trả lời của nó về ReceiptReading, quy đổi danh mục sang slug backend."""

from datetime import UTC, datetime

import httpx
import pytest

from app.core.config import settings
from app.core.enums import CATEGORY_SLUGS
from app.pipeline.stages import remote_ocr
from app.pipeline.stages.remote_ocr import (
    SLUG_BY_OCR_CATEGORY,
    OcrServiceError,
    read_receipt_remote,
)

RECEIVED_AT = datetime(2026, 9, 12, 5, 0, tzinfo=UTC)
USER_HASH = "a" * 64


def body(**overrides) -> dict:
    base = {
        "merchant": "HIGHLANDS COFFEE",
        "merchant_display": "Highlands Coffee",
        "total_amount": 16000,
        "category": "dining",
        "category_source": "brand",
        "category_confidence": 0.8,
        "total_source": "text",
        "needs_review": False,
        "lines": ["HIGHLANDS COFFEE", "Ngay: 11/04/2026 13:16", "Tong tien: 16.000"],
    }
    return base | overrides


def client_for(handler) -> httpx.AsyncClient:
    return httpx.AsyncClient(transport=httpx.MockTransport(handler))


@pytest.fixture(autouse=True)
def configured(monkeypatch):
    monkeypatch.setattr(settings, "ocr_service_url", "http://ocr-service:8000")
    monkeypatch.setattr(settings, "ocr_service_api_key", "k" * 40)


async def read(handler):
    return await read_receipt_remote(
        b"img", "hoa-don.jpg", "image/jpeg", USER_HASH, RECEIVED_AT, client_for(handler)
    )


def test_every_mapped_slug_is_a_real_backend_slug():
    assert set(SLUG_BY_OCR_CATEGORY.values()) <= set(CATEGORY_SLUGS)


async def test_success_becomes_a_reading_with_the_backend_slug():
    seen = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["auth"] = request.headers["authorization"]
        seen["user"] = request.headers["x-user-hash"]
        seen["url"] = str(request.url)
        return httpx.Response(200, json=body())

    result = await read(handler)
    assert seen == {"auth": "Bearer " + "k" * 40, "user": USER_HASH, "url": "http://ocr-service:8000/analyze-bill"}
    assert result.reading.amount_cents == 16000
    assert result.reading.merchant_name == "Highlands Coffee"
    assert result.reading.transacted_at is not None and result.reading.transacted_at.day == 11  # bóc từ dòng chữ
    assert result.category_slug == "food" and result.category_confidence == 0.8
    assert result.reading.confidence < 0.85  # luôn cần người xem lại


async def test_utilities_and_healthcare_map_to_bills_and_health():
    assert (await read(lambda r: httpx.Response(200, json=body(category="utilities")))).category_slug == "bills"
    assert (await read(lambda r: httpx.Response(200, json=body(category="healthcare")))).category_slug == "health"


async def test_unknown_category_leaves_the_slug_empty_so_the_route_falls_back():
    for override in ({"category_source": "none"}, {"category": "weird"}):
        result = await read(lambda r, o=override: httpx.Response(200, json=body(**o)))
        assert result.category_slug is None


async def test_needs_review_lowers_the_confidence():
    sure = await read(lambda r: httpx.Response(200, json=body()))
    unsure = await read(lambda r: httpx.Response(200, json=body(needs_review=True)))
    assert unsure.reading.confidence <= 0.50 < sure.reading.confidence


async def test_missing_total_gives_a_reading_without_amount():
    result = await read(lambda r: httpx.Response(200, json=body(total_amount=None)))
    assert result.reading.amount_cents is None and result.reading.confidence == 0.0 and result.reading.text


async def test_not_a_receipt_is_an_empty_reading():
    result = await read(lambda r: httpx.Response(422, json={"error": "x", "code": "not_a_receipt"}))
    assert result.reading.text == "" and result.category_slug is None


@pytest.mark.parametrize("status", [401, 422, 500, 503])
async def test_other_failures_raise(status):
    with pytest.raises(OcrServiceError):
        await read(lambda r: httpx.Response(status, json={"error": "x"}))


async def test_network_error_raises():
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("down")

    with pytest.raises(OcrServiceError):
        await read(handler)


async def test_unconfigured_service_raises(monkeypatch):
    monkeypatch.setattr(settings, "ocr_service_api_key", None)
    with pytest.raises(OcrServiceError):
        await read(lambda r: httpx.Response(200, json=body()))
