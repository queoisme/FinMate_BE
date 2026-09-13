"""Đọc hóa đơn giấy: ảnh → text → các trường giao dịch.

Docx phương thức 3 yêu cầu bóc **Tổng tiền thanh toán, Tên đơn vị bán hàng, Ngày giao
dịch**. Ba trường đó chính là ba trường mà Extractor và Categorizer sẵn có đã xử lý, nên
stage này chỉ làm đúng phần chưa có: biến ảnh thành text, rồi chọn ĐÚNG con số trong một
tờ giấy đầy con số.

Engine OCR nằm sau một protocol. Nó là biên I/O phụ thuộc một binary hệ điều hành, còn giá
trị thật của stage nằm ở phần đọc text — tách ra thì phần đó test được trên mọi máy, kể cả
máy không cài tesseract.
"""

from __future__ import annotations

import io
import re
from dataclasses import dataclass
from datetime import datetime
from typing import Protocol

from app.core.enums import TransactionType
from app.utils.text_utils import (
    find_amount_candidates,
    normalize_whitespace,
    parse_datetime,
)

# Nhãn đứng cạnh TỔNG TIỀN trên hóa đơn Việt Nam. Đây là thứ phân biệt tổng tiền với hàng
# chục con số khác trên cùng tờ giấy (đơn giá, thành tiền từng dòng, tiền khách đưa, tiền
# thối lại) — thiếu nó thì chỉ còn cách đoán, và đoán sai số tiền là lỗi đắt nhất ở đây.
_TOTAL_MARKERS: tuple[str, ...] = (
    "tong cong",
    "tổng cộng",
    "tong tien",
    "tổng tiền",
    "tong thanh toan",
    "tổng thanh toán",
    "thanh tien",
    "thành tiền",
    "tong so tien",
    "tổng số tiền",
    "khach phai tra",
    "khách phải trả",
    "phai thanh toan",
    "phải thanh toán",
    "t.cong",
    "t.cộng",
    "total",
    "grand total",
    "amount due",
)

# Nhãn của những con số KHÔNG phải tổng tiền nhưng hay lớn ngang tổng tiền.
_NOT_TOTAL_MARKERS: tuple[str, ...] = (
    "tien khach dua",
    "tiền khách đưa",
    "khach dua",
    "khách đưa",
    "tien mat",
    "tiền mặt",
    "tien thoi",
    "tiền thối",
    "tra lai",
    "trả lại",
    "cash",
    "change",
)

# Dòng đầu hóa đơn hay là tên cửa hàng, nhưng cũng hay là những thứ này.
_MERCHANT_NOISE = re.compile(
    r"^(hoa don|hóa đơn|phieu|phiếu|invoice|receipt|bill|so hd|số hđ|ngay|ngày|"
    r"dia chi|địa chỉ|mst|dt|đt|tel|hotline|website|www\.|http)",
    re.IGNORECASE,
)

_MIN_MERCHANT_LENGTH = 3
_MAX_MERCHANT_LENGTH = 120


# --psm 6 = "một khối văn bản đồng nhất". Chế độ MẶC ĐỊNH (--psm 3) tự dò cột và nó cắt hóa
# đơn thành hai cột: nhãn một bên, số tiền một bên — "Tong cong:" và "110.000" rơi ra hai dòng
# khác nhau, thế là mất hẳn liên kết giữa nhãn và con số của nó. Lỗi này chỉ lộ ra khi chạy
# tesseract THẬT; text hóa đơn viết tay trong test đơn vị luôn đã đúng hàng đúng lối.
_TESSERACT_CONFIG = "--psm 6"


class OcrEngine(Protocol):
    """Ảnh (bytes) → text. Một phương thức, vì đó đúng là tất cả những gì stage này cần."""

    def read_text(self, image: bytes) -> str: ...


class TesseractOcrEngine:
    """Bọc binary ``tesseract`` qua pytesseract.

    Tiền xử lý tối thiểu — xám hoá và tăng tương phản. Ảnh chụp hóa đơn nhiệt bằng điện
    thoại thường nhạt và ám vàng; bỏ qua bước này thì tesseract trả về ký tự rác mà vẫn
    không báo lỗi gì.
    """

    def __init__(self, language: str = "vie+eng") -> None:
        self._language = language

    def read_text(self, image: bytes) -> str:
        import pytesseract
        from PIL import Image, ImageOps

        with Image.open(io.BytesIO(image)) as opened:
            # EXIF orientation: ảnh chụp dọc bằng điện thoại thường nằm ngang trong file.
            prepared = ImageOps.exif_transpose(opened)
            prepared = ImageOps.grayscale(prepared)
            prepared = ImageOps.autocontrast(prepared)
            return pytesseract.image_to_string(
                prepared, lang=self._language, config=_TESSERACT_CONFIG
            )


@dataclass(frozen=True)
class ReceiptReading:
    text: str
    amount_cents: int | None
    merchant_name: str | None
    transacted_at: datetime | None
    # Nhãn "tổng tiền" nào đã quyết định con số — để gỡ lỗi khi số tiền ra sai, giống vai
    # trò của matched_pattern_name ở luồng thông báo.
    total_marker: str | None
    confidence: float


def read_receipt(
    engine: OcrEngine, image: bytes, received_at: datetime
) -> ReceiptReading:
    text = engine.read_text(image)
    lines = [normalize_whitespace(line) for line in text.splitlines()]
    lines = [line for line in lines if line]

    amount, marker = _find_total(lines)
    merchant = _find_merchant(lines)
    transacted_at = _find_date(lines, received_at)

    return ReceiptReading(
        text=text,
        amount_cents=amount,
        merchant_name=merchant,
        transacted_at=transacted_at,
        total_marker=marker,
        confidence=_confidence(amount, marker, merchant, transacted_at),
    )


def _find_total(lines: list[str]) -> tuple[int | None, str | None]:
    """Số tiền đứng cùng dòng với một nhãn tổng tiền; nhãn đứng SAU thắng.

    Hóa đơn liệt kê từ trên xuống và tổng cộng nằm ở cuối, nên khi có nhiều dòng cùng mang
    nhãn ("thành tiền" từng dòng rồi "tổng cộng" ở cuối) thì dòng cuối mới là con số cần.
    """
    for line in reversed(lines):
        lowered = line.lower()
        if any(marker in lowered for marker in _NOT_TOTAL_MARKERS):
            continue

        marker = next((m for m in _TOTAL_MARKERS if m in lowered), None)
        if marker is None:
            continue

        # Chỉ xét phần SAU nhãn: "Tổng cộng 3 món 250.000" không được lấy số 3.
        tail = lowered.split(marker, 1)[1]
        offset = len(line) - len(tail)
        candidates = [c for c in find_amount_candidates(line[offset:]) if c.value > 0]
        if candidates:
            return max(candidates, key=lambda c: c.value).value, marker

    # Không nhãn nào đọc được — trên hóa đơn, tổng tiền gần như luôn là con số lớn nhất.
    # Suy đoán này đúng phần lớn nhưng không phải luôn đúng, nên nó hạ hẳn confidence
    # xuống dưới ngưỡng xác nhận một chạm, xem _confidence.
    #
    # Chỉ xét con số TRÔNG GIỐNG TIỀN: có dấu ngăn nhóm hoặc có đơn vị. Hóa đơn đầy dãy số
    # trần — mã số thuế, số hóa đơn, số điện thoại — và chúng thường lớn hơn tổng tiền.
    # Thiếu bộ lọc này, "MST: 0312345678" biến thành một giao dịch 312 triệu đồng.
    everything = [
        c
        for c in find_amount_candidates(" ".join(lines))
        if c.value > 0 and (c.has_grouping or c.has_marker)
    ]
    if everything:
        return max(everything, key=lambda c: c.value).value, None
    return None, None


def _find_merchant(lines: list[str]) -> str | None:
    """Dòng đầu tiên ở đầu hóa đơn trông giống tên cửa hàng."""
    for line in lines[:6]:
        if _MERCHANT_NOISE.match(line):
            continue
        # Dòng toàn số/ký hiệu là mã hóa đơn hay số điện thoại, không phải tên.
        if not re.search(r"[^\W\d_]{2,}", line, re.UNICODE):
            continue
        if len(line) < _MIN_MERCHANT_LENGTH:
            continue
        return line[:_MAX_MERCHANT_LENGTH]
    return None


def _find_date(lines: list[str], received_at: datetime) -> datetime | None:
    for line in lines:
        parsed = parse_datetime(line, received_at)
        if parsed is not None:
            return parsed
    return None


def _confidence(
    amount: int | None,
    marker: str | None,
    merchant: str | None,
    transacted_at: datetime | None,
) -> float:
    """Luôn dưới 0,85.

    Docx Flow 1 bước 5.2 dành mốc đó cho xác nhận một chạm, mà OCR hóa đơn thì docx lại
    yêu cầu tường minh "hiển thị ảnh kèm thông số để người dùng rà soát trước khi lưu" —
    tức là luôn có bước người xem lại. Trả về một con số nằm trong vùng tự-động-xác-nhận sẽ
    mâu thuẫn với chính yêu cầu đó.
    """
    if amount is None:
        return 0.0

    score = 0.55 if marker else 0.35
    if merchant:
        score += 0.15
    if transacted_at:
        score += 0.10
    return round(min(score, 0.80), 2)


def guess_transaction_type() -> TransactionType:
    """Hóa đơn mua hàng luôn là tiền ra. Không có ca nào khác đáng cân nhắc ở đây."""
    return TransactionType.DEBIT
