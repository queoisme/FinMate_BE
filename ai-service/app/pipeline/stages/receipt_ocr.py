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
import unicodedata
from difflib import SequenceMatcher
from dataclasses import dataclass
from datetime import datetime
from typing import Protocol

from app.core import confidence as table  # tên khác: dataclass và hàm dưới có trường và biến `confidence`
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

_TARGET_WIDTH = 1600


# Tesseract đọc chữ tiếng Việt có dấu chồng rất hay sai ("Tổng thanh toán" → "Tôäg thanh toán",
# "Tiền khách đưa" → "Tiéh khách đưa"), nên so khớp nhãn bằng chuỗi con chính xác làm mất cả
# nhãn TỔNG TIỀN lẫn nhãn TIỀN KHÁCH ĐƯA — và khi đó chỉ còn cách đoán "số lớn nhất", thứ hay
# chọn nhầm tiền khách đưa. So khớp mờ trên bản đã bỏ dấu sửa đúng chỗ này.
_FUZZY_THRESHOLD = 0.84

# Trần hợp lý cho một hóa đơn. Dãy số bị đọc thành tiền (mã số thuế, điện thoại) thường ra
# hàng chục tỷ; không ai quét hóa đơn giấy trên 500 triệu đồng.
_MAX_RECEIPT_VND = 500_000_000

# Dòng chứa định danh chứ không chứa tiền: số ở đây không bao giờ là tổng hóa đơn.
_IDENTIFIER_LINE = re.compile(
    r"(mst|ma so thue|dt|sdt|tel|hotline|fax|so hd|dia chi|dc|website|www|http)"
)


def _fold(text: str) -> str:
    """Chữ thường, bỏ dấu. "đ" không tách được bằng NFD nên đổi tay."""
    decomposed = unicodedata.normalize("NFD", text.lower().replace("đ", "d"))
    return "".join(ch for ch in decomposed if not unicodedata.combining(ch))


def _tokens(line: str) -> list[str]:
    # Tách cả ở dấu hai chấm: OCR hay dính "cộng:38.500" thành một token.
    return [t for t in re.split(r"[\s:]+", line) if t]


def _match_marker(
    tokens: list[str], markers: tuple[str, ...]
) -> tuple[str, int] | None:
    """Nhãn khớp nhất trong dòng (có dung sai OCR) và vị trí token ngay sau nhãn."""
    folded = [re.sub(r"[^\w.]", "", _fold(t)) for t in tokens]
    best: tuple[float, int, str, int] | None = None
    for marker in markers:
        width = len(marker.split())
        for start in range(len(folded) - width + 1):
            window = " ".join(folded[start : start + width])
            # Nhãn ngắn (total, cash) quá dễ khớp nhầm khi cho sai số — chỉ khớp đúng.
            ratio = 1.0 if window == marker else (
                SequenceMatcher(None, window, marker).ratio() if len(marker) > 5 else 0.0
            )
            if ratio < _FUZZY_THRESHOLD:
                continue
            candidate = (ratio, len(marker), marker, start + width)
            if best is None or candidate[:2] > best[:2]:
                best = candidate
    return (best[2], best[3]) if best else None


def _fold_markers(markers: tuple[str, ...]) -> tuple[str, ...]:
    return tuple(dict.fromkeys(_fold(m) for m in markers))


_TOTAL_FOLDED = _fold_markers(_TOTAL_MARKERS)

# "Thành tiền" là nhãn của TỪNG DÒNG hàng trên phần lớn hóa đơn, chỉ đôi khi là tổng. Nó
# không được thắng một nhãn tổng thật, và khi nó là nhãn duy nhất đọc được thì phải xét thêm
# các số khác trên tờ giấy — tổng không bao giờ nhỏ hơn một dòng hàng.
_WEAK_TOTAL_FOLDED = ("thanh tien",)
_STRONG_TOTAL_FOLDED = tuple(m for m in _TOTAL_FOLDED if m not in _WEAK_TOTAL_FOLDED)
_NOT_TOTAL_FOLDED = _fold_markers(_NOT_TOTAL_MARKERS)


class OcrEngine(Protocol):
    """Ảnh (bytes) → text. Một phương thức, vì đó đúng là tất cả những gì stage này cần."""

    def read_text(self, image: bytes) -> str: ...


class TesseractOcrEngine:
    """Bọc binary ``tesseract`` qua pytesseract.

    Tiền xử lý: xám hoá, tăng tương phản, PHÓNG TO rồi làm nét. Ảnh chụp hóa đơn nhiệt bằng
    điện thoại thường nhạt, ám vàng và chữ nhỏ; Tesseract đọc tốt nhất khi chiều cao chữ cỡ
    30px, nên ảnh hẹp được kéo lên bề ngang 1600px. Đo trên hóa đơn tổng hợp: riêng bước này
    đưa ảnh nhạt từ 56% lên 100% số tiền đúng.

    KHÔNG nhị phân hoá: Otsu toàn cục cho điểm cao hơn một chút trên ảnh tổng hợp nhưng ảnh
    chụp thật có bóng đổ, và một ngưỡng duy nhất biến nửa tờ giấy thành màu đen.
    """

    def __init__(self, language: str = "vie+eng") -> None:
        self._language = language

    def read_text(self, image: bytes) -> str:
        import pytesseract
        from PIL import Image, ImageFilter, ImageOps

        with Image.open(io.BytesIO(image)) as opened:
            # EXIF orientation: ảnh chụp dọc bằng điện thoại thường nằm ngang trong file.
            prepared = ImageOps.exif_transpose(opened)
            prepared = ImageOps.grayscale(prepared)
            prepared = ImageOps.autocontrast(prepared)
            if prepared.width < _TARGET_WIDTH:
                scale = _TARGET_WIDTH / prepared.width
                prepared = prepared.resize(
                    (_TARGET_WIDTH, round(prepared.height * scale)), Image.LANCZOS
                )
            prepared = prepared.filter(
                ImageFilter.UnsharpMask(radius=2, percent=150, threshold=3)
            )
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
    parsed = [(line, _tokens(line)) for line in lines]

    def labelled(markers: tuple[str, ...]) -> tuple[int, str] | None:
        for line, tokens in reversed(parsed):
            if _match_marker(tokens, _NOT_TOTAL_FOLDED):
                continue
            hit = _match_marker(tokens, markers)
            if hit is None:
                continue
            marker, after = hit
            # Chỉ xét phần SAU nhãn: "Tổng cộng 3 món 250.000" không được lấy số 3.
            candidates = [
                c
                for c in find_amount_candidates(" ".join(tokens[after:]))
                if 0 < c.value <= _MAX_RECEIPT_VND
            ]
            if candidates:
                return max(candidates, key=lambda c: c.value).value, marker
        return None

    strong = labelled(_STRONG_TOTAL_FOLDED)
    if strong:
        return strong
    weak = labelled(_WEAK_TOTAL_FOLDED)

    # Không nhãn nào đọc được — trên hóa đơn, tổng tiền gần như luôn là con số lớn nhất.
    # Suy đoán này đúng phần lớn nhưng không phải luôn đúng, nên nó hạ hẳn confidence
    # xuống dưới ngưỡng xác nhận một chạm, xem _confidence.
    #
    # Xét TỪNG DÒNG, bỏ dòng định danh (MST, điện thoại, địa chỉ) và dòng tiền khách đưa/tiền
    # thối, và chỉ lấy con số TRÔNG GIỐNG TIỀN: có dấu ngăn nhóm hoặc có đơn vị. Nối cả tờ
    # thành một chuỗi rồi quét sẽ dính số của dòng này sang dòng khác — đã từng ra giao dịch
    # hàng chục tỷ đồng từ "MST: 0312345678 ĐT: 028...".
    everything = []
    for line, tokens in parsed:
        if _IDENTIFIER_LINE.search(_fold(line)) or _match_marker(tokens, _NOT_TOTAL_FOLDED):
            continue
        everything.extend(
            c
            for c in find_amount_candidates(line)
            if 0 < c.value <= _MAX_RECEIPT_VND and (c.has_grouping or c.has_marker)
        )
    if weak:
        value, marker = weak
        return max([value, *(c.value for c in everything)]), marker
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

    score = table.get("receipt.base_with_marker" if marker else "receipt.base_without_marker")
    if merchant:
        score += table.get("receipt.merchant_bonus")
    if transacted_at:
        score += table.get("receipt.date_bonus")
    return round(min(score, table.get("receipt.max")), 2)


def guess_transaction_type() -> TransactionType:
    """Hóa đơn mua hàng luôn là tiền ra. Không có ca nào khác đáng cân nhắc ở đây."""
    return TransactionType.DEBIT
