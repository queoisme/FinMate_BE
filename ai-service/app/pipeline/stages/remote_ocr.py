"""Đọc hóa đơn qua OCR service riêng (PaddleOCR + VietOCR) thay cho Tesseract chạy trong tiến trình này.

OCR service nhận ảnh, trả tổng tiền, tên cửa hàng, danh mục và các dòng chữ ĐÃ lọc dữ liệu cá nhân.
Stage này đổi câu trả lời đó về đúng ``ReceiptReading`` mà route ``/ocr`` đã dùng, nên contract
Backend ↔ AI Service không đổi. Ngày giao dịch không do OCR service bóc: nó vẫn lấy từ các dòng chữ
bằng đúng bộ đọc ngày cũ.

Danh mục của OCR service (9 nhóm) được quy đổi sang slug của backend (CATEGORY_SLUGS). Slug lạ làm
giao dịch mất danh mục một cách im lặng, nên bảng quy đổi chỉ chứa slug hợp lệ.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime

import httpx

from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.core.config import settings
from app.core.enums import CATEGORY_SLUGS
from app.pipeline.stages.receipt_ocr import ReceiptReading, _confidence, _find_date

SLUG_BY_OCR_CATEGORY = SLUG_BY_LOCAL_CATEGORY  # giữ tên cũ cho test và code đã import

# Kết quả luôn cần người xem lại; trần này khớp trần của _confidence ở receipt_ocr.
_MAX_CATEGORY_CONFIDENCE = 0.80
# Bill OCR service tự đánh dấu cần xem lại (ảnh mờ, số không khớp nhau...) thì không được tỏ ra chắc chắn.
_REVIEW_CONFIDENCE_CAP = 0.50

_NOT_A_RECEIPT_CODE = "not_a_receipt"


class OcrServiceError(Exception):
    """OCR service không trả lời được (mạng, 5xx, khóa sai). Route đổi thành 503."""


@dataclass(frozen=True)
class RemoteReceipt:
    reading: ReceiptReading
    # None khi OCR service không biết cửa hàng/danh mục: route rơi về Categorizer như luồng Tesseract.
    category_slug: str | None
    category_confidence: float


def _empty_reading() -> ReceiptReading:
    return ReceiptReading(
        text="", amount_cents=None, merchant_name=None, transacted_at=None, total_marker=None, confidence=0.0
    )


def _to_remote_receipt(body: dict, received_at: datetime) -> RemoteReceipt:
    lines = [line for line in body.get("lines", []) if isinstance(line, str)]
    amount = body.get("total_amount")
    amount_cents = amount if isinstance(amount, int) and amount > 0 else None
    merchant = body.get("merchant_display") or body.get("merchant")
    transacted_at = _find_date(lines, received_at)
    marker = "ocr_service" if body.get("total_source") == "text" else None

    confidence = _confidence(amount_cents, marker, merchant, transacted_at)
    if body.get("needs_review"):
        confidence = min(confidence, _REVIEW_CONFIDENCE_CAP)

    reading = ReceiptReading(
        text="\n".join(lines),
        amount_cents=amount_cents,
        merchant_name=merchant,
        transacted_at=transacted_at,
        total_marker=marker,
        confidence=confidence,
    )

    slug = SLUG_BY_OCR_CATEGORY.get(str(body.get("category")))
    if slug not in CATEGORY_SLUGS or body.get("category_source") == "none":
        return RemoteReceipt(reading, None, 0.0)
    category_confidence = min(float(body.get("category_confidence") or 0.0), _MAX_CATEGORY_CONFIDENCE)
    return RemoteReceipt(reading, slug, category_confidence)


async def read_receipt_remote(
    image: bytes,
    filename: str | None,
    content_type: str | None,
    user_id_hash: str,
    received_at: datetime,
    client: httpx.AsyncClient | None = None,
) -> RemoteReceipt:
    if not settings.ocr_service_url or not settings.ocr_service_api_key:
        raise OcrServiceError("OCR_SERVICE_URL hoặc OCR_SERVICE_API_KEY chưa được cấu hình")

    url = settings.ocr_service_url.rstrip("/") + "/analyze-bill"
    headers = {"Authorization": f"Bearer {settings.ocr_service_api_key}", "X-User-Hash": user_id_hash}
    files = {"image": (filename or "receipt", image, content_type or "application/octet-stream")}
    owns_client = client is None
    http = client or httpx.AsyncClient(timeout=settings.ocr_service_timeout_seconds)
    try:
        response = await http.post(url, headers=headers, files=files)
    except httpx.HTTPError as error:
        raise OcrServiceError(f"Không gọi được OCR service: {type(error).__name__}") from error
    finally:
        if owns_client:
            await http.aclose()

    if response.status_code == 422 and _error_code(response) == _NOT_A_RECEIPT_CODE:
        return RemoteReceipt(_empty_reading(), None, 0.0)  # không có chữ / không giống hóa đơn: "unreadable"
    if response.status_code != 200:
        raise OcrServiceError(f"OCR service trả {response.status_code}")
    return _to_remote_receipt(response.json(), received_at)


def _error_code(response: httpx.Response) -> str | None:
    try:
        return response.json().get("code")
    except ValueError:
        return None
