"""Chuẩn hoá văn bản và bóc số tiền / thời gian từ thông báo tiếng Việt.

Thuần hàm, không chạm DB và không chạm model — đây là phần dễ test nhất và cũng là phần
sai thì hỏng nặng nhất của pipeline.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone

from app.utils.number_words import find_number_words

# App chỉ phục vụ Việt Nam nên dùng UTC+7 cố định, giống hệt lý do phía backend viết
# Common/VietnamTime thay vì TimeZoneInfo (xem note biên chu kỳ ở TASKS.md Phase 5).
VIETNAM_TZ = timezone(timedelta(hours=7))

_MAGNITUDES: tuple[tuple[str, int], ...] = (
    ("tỷ", 1_000_000_000),
    ("ty", 1_000_000_000),
    ("triệu", 1_000_000),
    ("trieu", 1_000_000),
    ("tr", 1_000_000),
    ("nghìn", 1_000),
    ("nghin", 1_000),
    ("ngàn", 1_000),
    ("ngan", 1_000),
    ("k", 1_000),
)

# "2tr5" = 2.500.000, "75k5" = 75.500 — chữ số sau đơn vị là phần thập phân của đơn vị đó.
_SHORTHAND = re.compile(
    r"^(?P<head>\d+)\s*(?P<unit>tỷ|ty|triệu|trieu|tr|nghìn|nghin|ngàn|ngan|k)\s*(?P<tail>\d*)$",
    re.IGNORECASE,
)

_CURRENCY_NOISE = re.compile(r"(?:vnđ|vnd|đ|d|₫)\.?$", re.IGNORECASE)

# Một "ứng viên số tiền": cụm số, kèm hậu tố đơn vị (75k, 2tr5, 12 triệu) hoặc ký hiệu
# tiền tệ (850.000đ, 75,000 VND) nếu có. Bắt được hậu tố là điều kiện sống còn của nhánh
# generic — "45k" mà chỉ đọc phần số sẽ ra 45 đồng.
_AMOUNT_CANDIDATE = re.compile(
    r"(?P<number>\d[\d.,]*)"
    r"(?:\s*(?P<unit>tỷ|ty|triệu|trieu|tr|nghìn|nghin|ngàn|ngan|k)(?P<tail>\d*))?"
    # "d" là cách gõ "đ" không dấu, rất phổ biến. \b là bắt buộc: thiếu nó thì "ngay 15
    # de tranh" biến "15" thành một số tiền có ký hiệu tiền tệ.
    r"(?:\s*(?P<currency>vnđ|vnd|₫|đ|d)\b)?",
    re.IGNORECASE,
)


def normalize_whitespace(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def parse_vnd_amount(raw: str | None) -> int | None:
    """Số tiền dạng chuỗi → số nguyên ĐỒNG. Trả None nếu không đọc được.

    Bẫy lớn nhất: tiếng Việt dùng ``.`` ngăn nghìn và ``,`` thập phân, ngược với en-US, mà
    app ngân hàng thì viết cả hai kiểu. Đọc nhầm "75.000" thành 75 làm sai số tiền 1000
    lần nhưng vẫn trả về một con số hợp lệ, tức là hỏng im lặng.

    Cách phân biệt không dựa vào ký tự ngăn cách mà dựa vào ĐỘ DÀI nhóm cuối: ba chữ số
    sau dấu ngăn cuối cùng thì mọi dấu đều là ngăn nghìn; một hoặc hai chữ số thì dấu cuối
    là dấu thập phân.
    """
    if raw is None:
        return None

    text = normalize_whitespace(str(raw)).lower()
    text = text.replace("+", "").replace("-", "").strip()
    text = _CURRENCY_NOISE.sub("", text).strip()
    text = re.sub(r"\s+", "", text)
    if not text:
        return None

    shorthand = _SHORTHAND.match(text)
    if shorthand:
        return _from_shorthand(shorthand)

    for suffix, multiplier in _MAGNITUDES:
        if text.endswith(suffix):
            head = text[: -len(suffix)].strip()
            value = _parse_decimal(head)
            return None if value is None else round(value * multiplier)

    value = _parse_decimal(text)
    return None if value is None else round(value)


def _from_shorthand(match: re.Match[str]) -> int | None:
    unit = match.group("unit")
    multiplier = next((m for s, m in _MAGNITUDES if s == unit), None)
    if multiplier is None:
        return None
    head = int(match.group("head"))
    tail = match.group("tail")
    fraction = float(f"0.{tail}") if tail else 0.0
    return round((head + fraction) * multiplier)


def _parse_decimal(text: str) -> float | None:
    if not re.fullmatch(r"\d[\d.,]*", text):
        return None

    separators = [i for i, ch in enumerate(text) if ch in ".,"]
    if not separators:
        return float(text)

    last = separators[-1]
    tail_len = len(text) - last - 1
    digits_only = re.sub(r"[.,]", "", text)

    if tail_len in (1, 2):
        # Dấu cuối là thập phân: "75,000.00" hoặc "1.234,56".
        head = re.sub(r"[.,]", "", text[:last])
        tail = text[last + 1 :]
        return float(f"{head or '0'}.{tail}")

    return float(digits_only)


@dataclass(frozen=True)
class AmountCandidate:
    value: int
    # Có hậu tố đơn vị hoặc ký hiệu tiền tệ đi kèm. Ứng viên "có dấu" đáng tin hơn hẳn:
    # trong "nhận lương tháng 9 15tr" thì "9" là số thứ tự tháng, "15tr" mới là tiền.
    has_marker: bool
    # Có dấu ngăn nhóm ("110.000"). Người Việt viết tiền là luôn có dấu ngăn, nên một dãy số
    # trần dài — mã số thuế, số hóa đơn, số điện thoại — phân biệt được với tiền nhờ điều này.
    has_grouping: bool
    start: int
    end: int


def find_amount_candidates(text: str) -> list[AmountCandidate]:
    """Mọi cụm có thể là số tiền trong câu, theo thứ tự xuất hiện.

    Gồm cả số viết bằng CHỮ ("bốn mươi lăm ngàn"), vì Speech-to-Text của luồng Voice trả về
    đúng những gì người dùng nói. Gộp hai nguồn ở đây chứ không ở Extractor để mọi tầng phía
    sau — tách số dư, lấy merchant sau giới từ, chọn ứng viên đáng tin nhất — dùng chung một
    đường mà không phải biết con số đến từ chữ số hay từ chữ.
    """
    candidates: list[AmountCandidate] = []
    for match in _AMOUNT_CANDIDATE.finditer(text):
        raw = match.group(0).strip()
        value = parse_vnd_amount(raw)
        if value is None or value <= 0:
            continue
        has_marker = bool(match.group("unit") or match.group("currency"))
        has_grouping = any(ch in match.group("number") for ch in ".,")
        candidates.append(
            AmountCandidate(value, has_marker, has_grouping, match.start(), match.end())
        )

    digit_spans = [(c.start, c.end) for c in candidates]
    for word_match in find_number_words(text):
        # Bỏ qua cụm chữ nằm chồng lên một cụm chữ số đã bắt được — "45 nghìn" khớp cả hai
        # đường, và đếm hai lần sẽ làm cùng một số tiền cạnh tranh với chính nó.
        if any(
            word_match.start < end and start < word_match.end
            for start, end in digit_spans
        ):
            continue
        candidates.append(
            AmountCandidate(
                value=word_match.value,
                # Đơn vị (nghìn/triệu/tỷ) đóng vai trò y hệt hậu tố "k"/"tr" ở chữ số.
                has_marker=word_match.has_scale,
                # Số đọc bằng chữ không có dấu ngăn nhóm, mà cũng không thể là mã số thuế.
                has_grouping=False,
                start=word_match.start,
                end=word_match.end,
            )
        )

    candidates.sort(key=lambda c: c.start)
    return candidates


# Cụm đứng ngay trước một con số để báo rằng đó là SỐ DƯ chứ không phải số tiền giao dịch.
_BALANCE_MARKERS: tuple[str, ...] = (
    "so du",
    "số dư",
    "sd:",
    "sd ",
    "so du vi",
    "số dư ví",
    "balance",
    "con lai",
    "còn lại",
    "kha dung",
    "khả dụng",
)

# Số dư hầu như luôn nằm sát ngay sau nhãn của nó; 24 ký tự đủ phủ "So du TK VCB ".
_BALANCE_CONTEXT_CHARS = 24


@dataclass(frozen=True)
class AmountSelection:
    amount: AmountCandidate | None
    balance: AmountCandidate | None


def select_amounts(text: str) -> AmountSelection:
    """Tách số tiền giao dịch khỏi số dư trong cùng một câu.

    Không có bước này thì "thanh toan 320,000 VND. So du 1,000,000 VND" sẽ ra 1 triệu:
    số dư gần như luôn LỚN HƠN số tiền giao dịch, nên chọn theo giá trị lớn nhất là chọn
    đúng con số sai. Đây là nhánh dành cho ngân hàng chưa có pattern, tức là đúng lúc
    không ai kiểm chứng giúp.
    """
    candidates = find_amount_candidates(text)
    if not candidates:
        return AmountSelection(None, None)

    lowered = text.lower()
    balances: list[AmountCandidate] = []
    others: list[AmountCandidate] = []
    for candidate in candidates:
        prefix = lowered[
            max(0, candidate.start - _BALANCE_CONTEXT_CHARS) : candidate.start
        ]
        (balances if any(m in prefix for m in _BALANCE_MARKERS) else others).append(
            candidate
        )

    marked = [c for c in others if c.has_marker]
    pool = marked or [c for c in others if c.value >= 1_000] or others
    amount = max(pool, key=lambda c: c.value) if pool else None
    balance = max(balances, key=lambda c: c.value) if balances else None
    return AmountSelection(amount, balance)


def pick_amount(text: str) -> AmountCandidate | None:
    """Chỉ số tiền giao dịch — tiện dụng khi không cần số dư."""
    return select_amounts(text).amount


# Ngân hàng viết ngày bằng cả "/" lẫn "-" (VCB dùng gạch), và năm có khi chỉ 2 chữ số.
# Thiếu một biến thể ở đây KHÔNG nổ lỗi ở đâu cả: parse_datetime trả None rồi Extractor
# lặng lẽ lấy thời điểm NHẬN thông báo thay cho thời điểm GIAO DỊCH — giao dịch rơi nhầm
# ngày, nhầm chu kỳ ngân sách, và không có gì báo.
_SEP = r"[-/.]"
_YEAR = r"\d{2,4}"
_TIME = r"(\d{1,2}):(\d{2})(?::(\d{2}))?"
_DAY_MONTH = rf"(\d{{1,2}}){_SEP}(\d{{1,2}})"

_DATETIME_PATTERNS: tuple[tuple[re.Pattern[str], str], ...] = (
    (re.compile(r"\b(\d{4})-(\d{2})-(\d{2})[T ](\d{1,2}):(\d{2})(?::(\d{2}))?"), "ymd"),
    (re.compile(rf"\b{_DAY_MONTH}{_SEP}({_YEAR})\s+{_TIME}"), "dmy_time"),
    (re.compile(rf"\b{_TIME}\s+{_DAY_MONTH}{_SEP}({_YEAR})"), "time_dmy"),
    (re.compile(rf"\b{_TIME}\s+{_DAY_MONTH}\b"), "time_dm"),
    (re.compile(rf"\b{_DAY_MONTH}\s+{_TIME}"), "dm_time"),
    (re.compile(rf"\b{_DAY_MONTH}{_SEP}({_YEAR})\b"), "dmy"),
)


def parse_datetime(raw: str | None, reference: datetime) -> datetime | None:
    """Thời điểm giao dịch ghi trong thông báo → datetime ở giờ Việt Nam.

    ``reference`` là lúc thông báo tới (``received_at``), dùng để suy năm khi thông báo chỉ
    ghi ngày/tháng. Suy năm phải xét cả chiều ngược: thông báo "31/12 23:50" nhận được lúc
    01/01 mà lấy năm hiện tại sẽ nhảy tới gần một năm trong tương lai.
    """
    if not raw:
        return None

    reference_vn = reference.astimezone(VIETNAM_TZ)
    for pattern, kind in _DATETIME_PATTERNS:
        match = pattern.search(raw)
        if match is None:
            continue
        parts = _extract_parts(match, kind, reference_vn)
        if parts is None:
            continue
        try:
            moment = datetime(*parts, tzinfo=VIETNAM_TZ)
        except ValueError:
            continue
        if kind in ("time_dm", "dm_time") and moment - reference_vn > timedelta(days=2):
            moment = moment.replace(year=moment.year - 1)
        return moment
    return None


def _extract_parts(
    match: re.Match[str], kind: str, reference: datetime
) -> tuple[int, int, int, int, int, int] | None:
    groups = match.groups()

    def as_int(value: str | None) -> int:
        return int(value) if value else 0

    def as_year(value: str) -> int:
        """ "24" nghĩa là 2024. Hiểu thành năm 24 sau Công nguyên thì datetime vẫn dựng
        được bình thường, chỉ có giao dịch biến mất khỏi mọi báo cáo."""
        year = int(value)
        return year + 2000 if year < 100 else year

    if kind == "ymd":
        year, month, day, hour, minute, second = groups
        return (
            as_year(year),
            int(month),
            int(day),
            int(hour),
            int(minute),
            as_int(second),
        )
    if kind == "dmy_time":
        day, month, year, hour, minute, second = groups
        return (
            as_year(year),
            int(month),
            int(day),
            int(hour),
            int(minute),
            as_int(second),
        )
    if kind == "time_dmy":
        hour, minute, second, day, month, year = groups
        return (
            as_year(year),
            int(month),
            int(day),
            int(hour),
            int(minute),
            as_int(second),
        )
    if kind == "time_dm":
        hour, minute, second, day, month = groups
        return (
            reference.year,
            int(month),
            int(day),
            int(hour),
            int(minute),
            as_int(second),
        )
    if kind == "dm_time":
        day, month, hour, minute, second = groups
        return (
            reference.year,
            int(month),
            int(day),
            int(hour),
            int(minute),
            as_int(second),
        )
    if kind == "dmy":
        day, month, year = groups
        return (as_year(year), int(month), int(day), 0, 0, 0)
    return None


_NUMBER_TOKEN = re.compile(r"\d[\d.,]*")


def normalize_for_model(text: str) -> str:
    """Chuẩn hoá đầu vào cho TF-IDF: hạ chữ thường và gộp mọi cụm số thành một token.

    Số tiền cụ thể là nhiễu với bài toán phân loại — "75.000" và "1.250.000" đều chỉ nói
    "ở đây có một con số". Giữ nguyên chúng làm từ vựng phình to và model học thuộc lòng
    các số xuất hiện trong tập train.
    """
    lowered = normalize_whitespace(text).lower()
    return _NUMBER_TOKEN.sub(" <num> ", lowered).strip()
