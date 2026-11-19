"""Hiểu câu ĐẶT MỤC TIÊU TIẾT KIỆM: "đặt mục tiêu vào 2 tháng sau tôi sẽ mua được xe máy khoảng 50 triệu".

Chỉ ĐỌC câu và trả bản nháp (tên, số tiền, hạn); backend/app hỏi xác nhận rồi mới tạo qua ``POST /saving-goals``, không bao giờ tự tạo.

Kích hoạt hẹp có chủ ý: chỉ khi câu có từ đặt mục tiêu rõ ràng. "mua xe máy 50 triệu" vẫn là khoản chi, "chuyển 2 triệu sang tiết kiệm" vẫn là chuyển tiền.

Hạn (giờ Việt Nam, chốt 23:59:59 của ngày đó):
  "N tháng sau/nữa/tới" = cùng ngày + N tháng (ngày không tồn tại thì lùi về cuối tháng: 31/8 + 1 tháng = 30/9)
  "tháng sau/tháng tới/tháng tiếp theo" = CUỐI tháng sau (khác "1 tháng sau")
  "N tuần/ngày sau", "nửa năm", "cuối tháng", "cuối năm", "năm sau" (31/12), "trước 30/12" (năm sau nếu năm nay đã qua)
  "tháng trước", "hôm qua" bị từ chối: hạn phải ở tương lai.

``normalize()`` (bỏ dấu, chữ thường) giữ nguyên độ dài so với câu gốc đã NFC, nên vị trí khớp trên chữ bỏ dấu dùng lại được trên câu gốc.
"""

from __future__ import annotations

import calendar
import re
import unicodedata
from dataclasses import dataclass
from datetime import datetime, timedelta

from app.services import intent_cues
from app.services.amount_normalizer import normalize, parse_amount
from app.services.vi_text import fold


def _bind(cues: intent_cues.Cues) -> None:
    """Gắn từ đặt mục tiêu, mốc đã qua và chữ không thuộc tên mục tiêu (app/data/intent_cues.json, hoặc bản admin kích hoạt trong DB) vào các tên dùng ở module này.
    - ``_STRONG``: từ đặt mục tiêu rõ ràng, đủ để hỏi lại số tiền nếu thiếu; ``_WEAK``: cách nói yếu hơn, chỉ tính khi có số tiền.
    - ``_STOPWORDS``: chữ không thuộc TÊN mục tiêu (đã bỏ dấu): bỏ chúng cùng từ đặt mục tiêu, số tiền và hạn thì phần còn lại là tên.
    Chỉ gọi với bản đã qua intent_cues.build."""
    global _STRONG, _WEAK, _PAST, _STOPWORDS, _ACCENTED_STOPWORDS, _AMBIGUOUS_PARTICLES, _P, _NUMBER_VALUES, _NEXT_WORDS
    _STRONG, _WEAK, _PAST = cues.patterns["goal_strong"], cues.patterns["goal_weak"], cues.patterns["goal_past"]
    _STOPWORDS = frozenset(fold(word) for word in cues.lists["goal_stopwords"])
    _ACCENTED_STOPWORDS = frozenset(unicodedata.normalize("NFC", word.lower()) for word in cues.lists["goal_stopwords_accented"])
    _AMBIGUOUS_PARTICLES = frozenset(fold(word) for word in cues.lists["goal_ambiguous_particles"])
    # Mẫu hạn (goal_date, goal_relative, goal_within, ...) và từ vựng của chúng nằm hết ở intent_cues.json; ở đây chỉ gắn tên để ``_parse_deadline`` dùng
    _P = {name: pattern for name, pattern in cues.patterns.items() if name.startswith("goal_")}
    _NUMBER_VALUES = {fold(word): value for word, value in intent_cues.valued(cues, "goal_number_words").items()}
    _NEXT_WORDS = frozenset(fold(word) for word in cues.lists["goal_next_words"])


_bind(intent_cues.current())
intent_cues.subscribe(_bind)

_UNITS = ("year", "month", "week", "day")  # tên nhóm đơn vị trong đoạn goal_unit của intent_cues.json (hợp đồng giữa mẫu và code, không phải từ vựng)


@dataclass(frozen=True)
class GoalDraft:
    name: str
    name_is_default: bool
    target: int  # đồng VND
    deadline: datetime | None  # giờ Việt Nam, 23:59:59 của ngày hạn; None = không hạn
    deadline_text: str | None  # cụm người dùng nói ("2 tháng sau"): app hiện kèm ngày cụ thể để người dùng soát
    ai_assisted: bool = False  # hạn/tên do AI đọc hộ (luật không hiểu): app không tự tạo, để người dùng soát rồi xác nhận


@dataclass(frozen=True)
class GoalResult:
    """``draft`` có thì app hiện thẻ xác nhận; không thì ``reply`` là câu hỏi lại."""

    draft: GoalDraft | None
    reply: str | None


@dataclass(frozen=True)
class _Deadline:
    when: datetime | None
    start: int = -1
    end: int = -1
    past: bool = False


def _end_of_day(day: datetime) -> datetime:
    return day.replace(hour=23, minute=59, second=59, microsecond=0)


def _add_months(day: datetime, months: int) -> datetime:
    year, month = divmod(day.year * 12 + day.month - 1 + months, 12)
    month += 1
    return day.replace(year=year, month=month, day=min(day.day, calendar.monthrange(year, month)[1]))


def _last_day_of_month(day: datetime, months_ahead: int) -> datetime:
    target = _add_months(day.replace(day=1), months_ahead)
    return target.replace(day=calendar.monthrange(target.year, target.month)[1])


def _number(token: str) -> int:
    return int(token) if token.isdigit() else _NUMBER_VALUES[token]


def _unit_of(match: re.Match) -> str | None:
    """Đơn vị người dùng nói (year | month | week | day): nhóm nào trong đoạn goal_unit khớp."""
    return next((unit for unit in _UNITS if match.groupdict().get(unit)), None)


def deadline_after(n: int, unit: str, now: datetime, half: bool = False) -> datetime:
    """Hạn = hôm nay + n đơn vị (``unit``: year | month | week | day), chốt 23:59:59; ``half`` = "rưỡi" (chỉ có nghĩa với năm: 3 năm rưỡi = 42 tháng).
    Dùng chung cho luật và cho bước AI đọc hộ hạn, nên hai đường ra cùng một ngày."""
    if unit == "month":
        when = _add_months(now, n)
    elif unit == "year":
        when = _add_months(now, 12 * n + (6 if half else 0))
    else:
        when = now + timedelta(days=n * (7 if unit == "week" else 1))
    return _end_of_day(when)


def _plausible_year(year: int) -> bool:
    limits = intent_cues.valued(intent_cues.current(), "goal_year_range")
    return limits["min"] <= year <= limits["max"]


def deadline_end_of_year(year: int, now: datetime) -> datetime | None:
    """Hạn "đến năm 2029" = 31/12 năm đó (cùng múi giờ với ``now``); None nếu năm không hợp lý."""
    if not _plausible_year(year):
        return None
    return _end_of_day(now.replace(year=year, month=12, day=31))


def deadline_end_of_month(year: int, month: int, now: datetime) -> datetime | None:
    """Hạn "tháng 6 năm 2028" = ngày cuối tháng đó; None nếu năm/tháng không hợp lý."""
    if not (_plausible_year(year) and 1 <= month <= 12):
        return None
    return _end_of_day(_last_day_of_month(now.replace(year=year, month=1, day=1), month - 1))


def _parse_deadline(norm: str, now: datetime) -> _Deadline:
    if m := _PAST.search(norm):
        return _Deadline(None, m.start(), m.end(), past=True)

    if m := _P["goal_date"].search(norm):
        day, month = int(m["d"]), int(m["m"])
        year = now.year if not m["y"] else int(m["y"]) + (2000 if len(m["y"]) == 2 else 0)
        try:
            when = _end_of_day(now.replace(year=year, month=month, day=day))
            if not m["y"] and when <= now:
                when = _end_of_day(now.replace(year=year + 1, month=month, day=day))  # "trước 5/3" khi đã qua 5/3 năm nay: năm sau
        except ValueError:
            return _Deadline(None)
        return _Deadline(when, m.start(), m.end(), past=when <= now)

    if m := _P["goal_quarter"].search(norm):
        year = int(m["y"]) if m["y"] else now.year + (1 if m["rel"] in _NEXT_WORDS else 0)
        end = _end_of_day(_last_day_of_month(now.replace(year=year, month=1, day=1), 3 * int(m["q"]) - 1))
        if not m["y"] and not m["rel"] and end <= now:
            end = _end_of_day(_last_day_of_month(now.replace(year=year + 1, month=1, day=1), 3 * int(m["q"]) - 1))  # "quý 1" khi đã qua quý 1 năm nay: năm sau
        return _Deadline(end, m.start(), m.end(), past=end <= now)

    if m := _P["goal_month_year"].search(norm):
        month = int(m["m"])
        if not 1 <= month <= 12:
            return _Deadline(None)
        when = _end_of_day(_last_day_of_month(now.replace(year=int(m["y"]), month=1, day=1), month - 1))
        return _Deadline(when, m.start(), m.end(), past=when <= now)

    if m := _P["goal_end_of"].search(norm):
        year = int(m["y"]) if m["y"] else now.year
        when = _end_of_day(_last_day_of_month(now.replace(year=year, day=1), 0) if m["month"] and not m["y"] else now.replace(year=year, month=12, day=31))
        return _Deadline(when, m.start(), m.end(), past=when <= now)

    if m := _P["goal_year_only"].search(norm):
        when = _end_of_day(now.replace(year=int(m["y"]), month=12, day=31))
        return _Deadline(when, m.start(), m.end(), past=when <= now)

    if m := _P["goal_relative"].search(norm) or _P["goal_within"].search(norm):
        return _Deadline(deadline_after(_number(m["n"]), _unit_of(m), now, bool(m["half"])), m.start(), m.end())

    if m := _P["goal_half_year"].search(norm):
        return _Deadline(_end_of_day(_add_months(now, 6)), m.start(), m.end())

    if m := _P["goal_next"].search(norm):
        if m["month"]:
            when = _last_day_of_month(now, 1)  # "tháng sau" là cả tháng sau, nên hạn là cuối tháng đó
        elif m["year"]:
            when = now.replace(year=now.year + 1, month=12, day=31)
        else:
            when = now + timedelta(days=7)
        return _Deadline(_end_of_day(when), m.start(), m.end())

    return _Deadline(None)


def _name_from(nfc: str, norm: str, removed: list[tuple[int, int]]) -> tuple[str, bool]:
    """Tên mục tiêu = các chữ còn lại sau khi bỏ từ đặt mục tiêu, số tiền, hạn và chữ đệm; không còn gì thì dùng tên mặc định."""
    kept = []
    for word in re.finditer(r"\S+", nfc):
        if any(start < word.end() and word.start() < end for start, end in removed):
            continue
        plain = norm[word.start() : word.end()].strip(".,;:!?")
        typed = word.group(0).strip(".,;:!?")
        # Bỏ dấu thì "nhà" trùng "nha" (từ đệm), "cô" trùng "co"...: chữ CÓ dấu chỉ là từ đệm khi đúng dạng có dấu của từ đệm; chữ không dấu thì theo bản bỏ dấu như trước
        has_accent = fold(typed) != typed.lower()
        is_stopword = plain in _STOPWORDS and (not has_accent or unicodedata.normalize("NFC", typed.lower()) in _ACCENTED_STOPWORDS)
        # Từ đệm mơ hồ khi không dấu (goal_ambiguous_particles: "nha" vừa là từ đệm cuối câu vừa là "nhà"): chỉ là từ đệm khi đứng CUỐI câu
        if plain in _AMBIGUOUS_PARTICLES and not has_accent and word.end() < len(nfc.rstrip(" .,;:!?")):
            is_stopword = False
        if plain and not is_stopword:
            kept.append(typed)
    name = " ".join(kept)
    if len(name) < 2:
        return intent_cues.current().replies["goal_default_name"], True
    return name[:1].upper() + name[1:100], False


def parse_goal(text: str, now: datetime) -> GoalResult | None:
    """None nếu câu không phải đặt mục tiêu; còn lại là bản nháp hoặc câu hỏi lại (thiếu/mơ hồ số tiền, hạn đã qua)."""
    nfc = unicodedata.normalize("NFC", text)
    norm = normalize(text)
    strong, weak = _STRONG.search(norm), _WEAK.search(norm)
    if not strong and not weak:
        return None

    deadline = _parse_deadline(norm, now)
    removed = [(m.start(), m.end()) for m in (strong, weak) if m]
    if deadline.start >= 0:
        removed.append((deadline.start, deadline.end))

    # Số tiền đọc trên câu đã bỏ cụm hạn và từ đặt mục tiêu: "2 tháng sau" chứa số 2 nhưng không phải tiền.
    masked = "".join(" " if any(start <= i < end for start, end in removed) else c for i, c in enumerate(norm))
    match = parse_amount(masked)
    if match is None and not strong:
        return None  # "muốn tiết kiệm bớt tiền ăn": không có số tiền thì đây không phải đặt mục tiêu
    replies = intent_cues.current().replies  # đọc lúc gọi: bản admin vừa kích hoạt có hiệu lực ngay
    if match is None:
        return GoalResult(None, replies["goal_ask_amount"])
    if match.assumed:
        return GoalResult(None, replies["goal_needs_unit"])
    if deadline.past:
        return GoalResult(None, replies["goal_past_deadline"])

    removed.append((match.start, match.end))
    name, is_default = _name_from(nfc, norm, removed)
    said = nfc[deadline.start : deadline.end].strip() if deadline.start >= 0 else None
    return GoalResult(GoalDraft(name, is_default, match.value, deadline.when, said or None), None)
