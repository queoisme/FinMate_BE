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
    global _STRONG, _WEAK, _PAST, _STOPWORDS
    _STRONG, _WEAK, _PAST = cues.patterns["goal_strong"], cues.patterns["goal_weak"], cues.patterns["goal_past"]
    _STOPWORDS = frozenset(fold(word) for word in cues.lists["goal_stopwords"])


_bind(intent_cues.current())
intent_cues.subscribe(_bind)

_NUMBER_WORDS = {"mot": 1, "hai": 2, "ba": 3, "bon": 4, "tu": 4, "nam": 5, "sau": 6, "bay": 7, "tam": 8, "chin": 9, "muoi": 10}
_NUM = r"(?P<n>\d{1,3}|" + "|".join(sorted(_NUMBER_WORDS, key=len, reverse=True)) + r")"
_DAYS_PER_UNIT = {"ngay": 1, "tuan": 7}

_DATE = re.compile(r"(?<![\d/])(?P<d>\d{1,2})/(?P<m>\d{1,2})(?:/(?P<y>\d{4}|\d{2}))?(?![\d/])")
_RELATIVE = re.compile(rf"(?<![a-z\d]){_NUM}\s+(?P<u>thang|tuan|ngay|nam)\s+(?:sau|nua|toi|tiep theo)(?![a-z])")
_HALF_YEAR = re.compile(r"(?<![a-z])nua nam(?: nua| sau| toi)?(?![a-z])")
_NEXT = re.compile(r"(?<![a-z])(?P<u>thang|tuan|nam)\s+(?:sau|toi|tiep theo)(?![a-z])")
_END_OF = re.compile(r"(?<![a-z])cuoi\s+(?P<u>thang|nam)(?:\s+nay)?(?![a-z])")


@dataclass(frozen=True)
class GoalDraft:
    name: str
    name_is_default: bool
    target: int  # đồng VND
    deadline: datetime | None  # giờ Việt Nam, 23:59:59 của ngày hạn; None = không hạn
    deadline_text: str | None  # cụm người dùng nói ("2 tháng sau"): app hiện kèm ngày cụ thể để người dùng soát


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
    return int(token) if token.isdigit() else _NUMBER_WORDS[token]


def _parse_deadline(norm: str, now: datetime) -> _Deadline:
    if m := _PAST.search(norm):
        return _Deadline(None, m.start(), m.end(), past=True)

    if m := _DATE.search(norm):
        day, month = int(m["d"]), int(m["m"])
        year = now.year if not m["y"] else int(m["y"]) + (2000 if len(m["y"]) == 2 else 0)
        try:
            when = _end_of_day(now.replace(year=year, month=month, day=day))
            if not m["y"] and when <= now:
                when = _end_of_day(now.replace(year=year + 1, month=month, day=day))  # "trước 5/3" khi đã qua 5/3 năm nay: năm sau
        except ValueError:
            return _Deadline(None)
        return _Deadline(when, m.start(), m.end(), past=when <= now)

    if m := _RELATIVE.search(norm):
        n, unit = _number(m["n"]), m["u"]
        if unit == "thang":
            when = _add_months(now, n)
        elif unit == "nam":
            when = _add_months(now, 12 * n)
        else:
            when = now + timedelta(days=n * _DAYS_PER_UNIT[unit])
        return _Deadline(_end_of_day(when), m.start(), m.end())

    if m := _HALF_YEAR.search(norm):
        return _Deadline(_end_of_day(_add_months(now, 6)), m.start(), m.end())

    if m := _END_OF.search(norm):
        when = _end_of_day(_last_day_of_month(now, 0) if m["u"] == "thang" else now.replace(month=12, day=31))
        return _Deadline(when, m.start(), m.end(), past=when <= now)

    if m := _NEXT.search(norm):
        unit = m["u"]
        if unit == "thang":
            when = _last_day_of_month(now, 1)  # "tháng sau" là cả tháng sau, nên hạn là cuối tháng đó
        elif unit == "nam":
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
        if plain and plain not in _STOPWORDS:
            kept.append(word.group(0).strip(".,;:!?"))
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
