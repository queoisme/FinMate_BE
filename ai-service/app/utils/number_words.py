"""Đọc số tiền viết bằng CHỮ trong tiếng Việt.

Cần cho luồng Voice: Speech-to-Text trả về đúng những gì người dùng nói, tức là
*"bốn mươi lăm ngàn"* chứ không phải *"45.000"* — chính ví dụ trong
``FinMate_Core_User_Flows.docx`` phương thức 2. Parser chữ số ở ``text_utils`` không đọc
được dạng này, nên không có module này thì câu nói mẫu của tài liệu sẽ ra ``uncertain``.

**Khớp trên chữ CÓ DẤU, không bỏ dấu.** Bỏ dấu tiếng Việt làm sập hàng loạt phân biệt mà
chính bài toán này sống nhờ nó:

======  ==========================  =========================================
bỏ dấu  các từ bị gộp làm một       hậu quả
======  ==========================  =========================================
muoi    **mười** (10) / **mươi**    "tháng chín mười lăm triệu" đọc thành
        (nhân 10)                   95,4 triệu thay vì 15 triệu
tu      **tư** (4) / **từ**         "từ công ty" biến thành số 4
ty      **tỷ** / "công **ty**"      "công ty" biến thành hàng tỷ
lam     **lăm** (5) / **làm**       "làm" biến thành số 5
nam     **năm** (5) / **nam**       mọi chữ "nam" thành số 5
sau     **sáu** (6) / **sau**       "sau đó" thành số 6
======  ==========================  =========================================

STT tiếng Việt trả về chữ có dấu, nên khớp có dấu là đúng nguồn dữ liệu thật. Người gõ tay
không dấu vẫn đi được đường chữ số ("45k") như trước.

**Giới hạn có chủ ý:** không tự nhân 1000 cho cụm không có đơn vị. "hai trăm rưỡi" trả về
250, không phải 250.000 — dù trong đời sống người Việt nói vậy thường là 250 nghìn. Đoán
thêm một dấu nhân ở đây là đúng cái loại lỗi tệ nhất của ứng dụng này: một con số tiền sai
gấp 1000 lần mà vẫn hợp lệ. Cụm có đơn vị ("hai trăm rưỡi nghìn") thì đọc đúng bình thường.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

# Chữ số. "mốt"/"lăm"/"nhăm"/"tư" là biến thể của 1/5/4 khi đứng sau hàng chục —
# "hai mươi mốt" = 21, "hai mươi lăm" = 25, "hai mươi tư" = 24.
_DIGITS: dict[str, int] = {
    "không": 0,
    "một": 1,
    "mốt": 1,
    "hai": 2,
    "ba": 3,
    "bốn": 4,
    "tư": 4,
    "năm": 5,
    "lăm": 5,
    "nhăm": 5,
    "sáu": 6,
    "bảy": 7,
    "bẩy": 7,
    "tám": 8,
    "chín": 9,
}

# "mươi"/"chục" NHÂN chữ số đứng trước: "hai mươi" = 20.
_TENS_MULTIPLIERS = {"mươi", "chục"}

# "mười" tự nó LÀ 10 và mở đầu một cụm: "mười lăm" = 15, không phải 10×5.
_TEN = "mười"

_HUNDREDS = {"trăm"}

# "linh"/"lẻ" chỉ là chỗ nối cho hàng chục bằng 0: "một trăm lẻ năm" = 105.
_ZERO_FILLERS = {"linh", "lẻ"}

_SCALES: dict[str, int] = {
    "nghìn": 1_000,
    "nghin": 1_000,
    "ngàn": 1_000,
    "triệu": 1_000_000,
    "tỷ": 1_000_000_000,
    "tỉ": 1_000_000_000,
}

_HALF = "rưỡi"

_WORDS = (
    set(_DIGITS)
    | _TENS_MULTIPLIERS
    | {_TEN}
    | _HUNDREDS
    | _ZERO_FILLERS
    | set(_SCALES)
    | {_HALF}
)

# \w với re.UNICODE bắt được chữ cái có dấu; loại chữ số ra để không lẫn với đường chữ số.
_WORD_SPAN = re.compile(r"[^\W\d_]+", re.UNICODE)


@dataclass(frozen=True)
class NumberWordMatch:
    value: int
    start: int
    end: int
    # Có đơn vị (nghìn/triệu/tỷ) đi kèm hay không. Cụm không đơn vị đáng ngờ hơn hẳn:
    # "ba" trong "ba ly cà phê" là số lượng, không phải tiền.
    has_scale: bool


def _parse_tokens(tokens: list[str]) -> tuple[int, bool] | None:
    """Chuỗi từ số → (giá trị, có đơn vị). Trả None nếu không đọc được."""
    total = 0
    section = 0
    digit: int | None = None
    last_place = 1
    has_scale = False
    # Đã có từ chỉ hàng nào sau lần gặp đơn vị gần nhất chưa — quyết định cách hiểu chữ số
    # đứng lẻ ở cuối câu, xem phần "một triệu hai" bên dưới.
    place_since_scale = False
    consumed = False

    for token in tokens:
        if token in _DIGITS:
            if digit is not None:
                # Hai chữ số liền nhau không có hàng ở giữa ("hai ba") không phải một số
                # đọc theo chuẩn — dừng lại thay vì đoán bừa.
                return None
            digit = _DIGITS[token]
            consumed = True
        elif token == _TEN:
            # "mười" là số 10, không phải toán tử. Nó chỉ hợp lệ khi mở đầu một cụm —
            # "chín mười" không phải cách đọc số nào của tiếng Việt (90 là "chín mươi").
            if digit is not None or section != 0:
                return None
            section = 10
            last_place = 10
            place_since_scale = True
            consumed = True
        elif token in _TENS_MULTIPLIERS:
            if digit is None:
                return None
            section += digit * 10
            last_place = 10
            place_since_scale = True
            digit = None
        elif token in _HUNDREDS:
            if digit is None:
                return None
            section += digit * 100
            last_place = 100
            place_since_scale = True
            digit = None
        elif token in _ZERO_FILLERS:
            continue
        elif token in _SCALES:
            scale = _SCALES[token]
            section += digit or 0
            if section == 0:
                return None
            total += section * scale
            section = 0
            digit = None
            last_place = scale
            place_since_scale = False
            has_scale = True
            consumed = True
        elif token == _HALF:
            # "rưỡi" = một nửa của hàng vừa dùng: "hai triệu rưỡi" = 2.500.000,
            # "hai trăm rưỡi" = 250.
            #
            # Cộng vào SECTION chứ không vào total: đơn vị đứng sau còn phải nhân cả phần
            # rưỡi này nữa. Cộng thẳng vào total thì "hai trăm rưỡi nghìn" ra 200.050
            # (= 200×1000 + 50) thay vì 250.000.
            if last_place < 10:
                return None
            section += last_place // 2
            consumed = True
        else:
            return None

    if digit is not None:
        if section == 0 and has_scale and not place_since_scale:
            # "một triệu hai" — chữ số đứng lẻ ngay sau đơn vị là PHẦN MƯỜI của đơn vị đó,
            # không phải hàng đơn vị. Hiểu nhầm chỗ này ra 1.000.002 thay vì 1.200.000.
            total += digit * (last_place // 10)
        else:
            section += digit

    total += section
    return (total, has_scale) if consumed and total > 0 else None


def find_number_words(text: str) -> list[NumberWordMatch]:
    """Mọi cụm số đọc bằng chữ trong câu, theo thứ tự xuất hiện.

    Lấy cụm DÀI NHẤT đọc được từ mỗi vị trí: "một trăm năm mươi ngàn" phải ra 150.000, chứ
    không phải 1 rồi 100 rồi 50.
    """
    lowered = text.lower()
    spans = [(m.start(), m.end(), m.group(0)) for m in _WORD_SPAN.finditer(lowered)]
    matches: list[NumberWordMatch] = []

    index = 0
    while index < len(spans):
        if spans[index][2] not in _WORDS:
            index += 1
            continue

        best: tuple[int, bool, int] | None = None
        for end in range(index + 1, len(spans) + 1):
            if spans[end - 1][2] not in _WORDS:
                break
            parsed = _parse_tokens([span[2] for span in spans[index:end]])
            if parsed is not None:
                best = (parsed[0], parsed[1], end)

        if best is None:
            index += 1
            continue

        value, has_scale, end_index = best
        matches.append(
            NumberWordMatch(
                value=value,
                start=spans[index][0],
                end=spans[end_index - 1][1],
                has_scale=has_scale,
            )
        )
        index = end_index

    return matches


def parse_number_words(text: str) -> int | None:
    """Giá trị đáng tin nhất trong câu, hoặc None.

    Ưu tiên cụm CÓ đơn vị: trong "ba ly cà phê bốn mươi lăm ngàn" thì "ba" là số lượng còn
    "bốn mươi lăm ngàn" mới là tiền.
    """
    matches = find_number_words(text)
    if not matches:
        return None
    scaled = [m for m in matches if m.has_scale]
    pool = scaled or matches
    return max(pool, key=lambda m: m.value).value
