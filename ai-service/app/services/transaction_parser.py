"""Hiểu một câu chat: "đi ăn 200k", "nhận lương 14 củ 5", "chuyển 2 triệu sang tiết kiệm", "tháng này tôi tiêu bao nhiêu?".

Chỉ ĐỌC câu và trả quyết định, không lưu gì và không giữ trạng thái giữa các câu: backend lưu giao dịch (bản nháp, xác nhận,
hoàn tác, sửa danh mục đều là việc của backend). Số liệu tổng hợp cũng do backend tính từ giao dịch của nó; ở đây chỉ hiểu người dùng
hỏi gì (loại, kỳ, danh mục).

Ý định lấy từ Rasa khi Rasa có mặt và đủ chắc, không thì từ luật. Hai bên bất đồng giữa chi tiêu/thu nhập/chuyển tiền thì tin dấu hiệu trong câu,
trừ khi Rasa rất chắc. Nhóm danh mục ở đây là 9 nhóm của bộ luật bill/chat; quy đổi sang slug backend làm ở route (app/core/category_map.py).
"""
from __future__ import annotations

import calendar
import re
import unicodedata
from collections.abc import Callable
from dataclasses import dataclass, replace
from datetime import date, datetime, timedelta

from app.services import vocab
from app.services import intent_cues
from app.services.amount_normalizer import normalize, parse_amount, remove_amount
from app.services.goal_parser import GoalDraft, parse_goal
from app.services.vi_text import clean_text, fold
from app.utils.amount_slang import SLANG
from app.services.clauses import apply_cancellations, extract_total, has_unresolved_cancel, split_amountless, split_clauses, split_on_amounts
from app.services.dates import VN, parse_when, remove_when
from app.core.category_map import LOAN_SLUG, SLUG_BY_LOCAL_CATEGORY
from app.services.category_resolver import resolve

_SLANG_FOLDED = frozenset(fold(word) for word in SLANG)  # củ, lít, chai, tỏi... đã bỏ dấu

_DEFAULT_PERIOD = "thang nay"  # kỳ mặc định của câu hỏi tổng khi câu không nói kỳ nào (cùng khóa với nhánh cuối của period_range)


def _bind(cues: intent_cues.Cues) -> None:
    """Gắn từ vựng, mẫu nhận dạng, ngưỡng và câu trả lời (app/data/intent_cues.json, hoặc bản admin kích hoạt trong DB) vào các tên dùng ở module này.
    Gọi lại mỗi khi có bản mới: các hàm bên dưới tra tên toàn cục lúc chạy nên có hiệu lực ngay, không cần khởi động lại. Chỉ gọi với bản đã qua intent_cues.build."""
    global MIN_NLU_CONFIDENCE, OUT_OF_SCOPE_OVERRIDE, KIND_TRUST, UNCERTAIN_BELOW, AUTO_CONFIDENCE
    global _GREET, _THANKS, _INCOME_SPECIFIC, _INCOME_ACCENTED, _INCOME_SPECIFIC_PLAIN, _INCOME_PLAIN, _RECEIVED, _NOT_INCOME
    global _DEBT_IN, _LEND_OUT, _FEE, _WITHDRAW, _OWN_WALLET, _OWN_ACCOUNT, _TRANSFER_CUE, _TO_OTHERS, _PAY_DEBT, _GIVING_AWAY, _PAYING
    global _QUERY, _BALANCE_Q, _INCOME_Q, _SPEND_Q, _MONEY_WORD, _PRICE_Q, _DATA_OPS, _AMBIGUOUS_SEND
    global _PERCENT, _STRAY_AMOUNT, _EXPENSE_VERB, _QUESTION_PARTICLES, _ANSWER_STARTS, _REPLIES
    global _THANKS_LEAD, _REPLY_CUES
    global _PERIOD_DAY, _PERIOD_DAY_ONLY, _PERIOD_RANGE, _PERIOD_LAST, _PERIOD_QUARTER, _PERIOD_DAY_BEFORE, _PERIOD_DAY_PART, _PERIOD_HINT
    global _PERIODS, _PERIOD_MONTH, _PERIOD_YEAR, _SELF_REFERENCE, _DETAIL_Q, _DETAIL_NOT, _REPLY_INTENTS, _DESCRIPTION_LEADING, _DESCRIPTION_TRAILING, _MERCHANT_IN, _QUESTION_LEAD
    thresholds, p = cues.thresholds, cues.patterns
    MIN_NLU_CONFIDENCE, OUT_OF_SCOPE_OVERRIDE, KIND_TRUST = thresholds["min_nlu_confidence"], thresholds["out_of_scope_override"], thresholds["kind_trust"]
    UNCERTAIN_BELOW, AUTO_CONFIDENCE = thresholds["uncertain_below"], thresholds["auto_confidence"]
    _GREET, _THANKS = cues.phrases["greet"], cues.phrases["thanks"]
    _THANKS_LEAD = p["thanks_lead"]
    _REPLY_CUES = {name: p[f"reply_cue_{name}"] for name in ("confirm", "deny", "cancel")}  # tên ý định của Rasa -> mẫu từ của lời đáp loại đó
    # Tiền vào có ba mức bằng chứng (xem _direction): CỤ THỂ, CHỦ ĐỀ (lương, thưởng...: đi cả hai chiều) và YẾU (anh/em gửi: người dùng hay tự xưng anh/em).
    _INCOME_SPECIFIC, _INCOME_ACCENTED = p["income_specific"], p["income_accented"]
    _INCOME_SPECIFIC_PLAIN, _INCOME_PLAIN = p["income_specific_plain"], p["income_plain"]
    _RECEIVED, _NOT_INCOME = p["received"], p["not_income"]
    _DEBT_IN, _LEND_OUT = p["debt_in"], p["lend_out"]
    _FEE, _WITHDRAW, _OWN_WALLET, _OWN_ACCOUNT = p["fee"], p["withdraw"], p["own_wallet"], p["own_account"]
    _TRANSFER_CUE, _TO_OTHERS, _PAY_DEBT = p["transfer_cue"], p["to_others"], p["pay_debt"]
    _GIVING_AWAY, _PAYING = p["giving_away"], p["paying"]
    # Hình thức câu: hỏi tổng, hỏi giá, lệnh quản lý dữ liệu, tiểu từ hỏi có/không.
    _QUERY, _BALANCE_Q, _INCOME_Q, _SPEND_Q = p["query"], p["balance_q"], p["income_q"], p["spend_q"]
    _MONEY_WORD, _PRICE_Q, _DATA_OPS, _AMBIGUOUS_SEND = p["money_word"], p["price_q"], p["data_ops"], p["ambiguous_send"]
    _PERCENT, _STRAY_AMOUNT, _EXPENSE_VERB = p["percent"], p["stray_amount"], p["expense_verb"]
    _QUESTION_PARTICLES, _ANSWER_STARTS = cues.question_particles, cues.answer_starts
    _QUESTION_LEAD, _REPLY_INTENTS, _PERIODS = p["question_lead"], cues.reply_intents, cues.period_labels
    _PERIOD_MONTH, _PERIOD_YEAR, _SELF_REFERENCE = p["period_month"], p["period_year"], p["self_reference"]
    _PERIOD_DAY, _PERIOD_DAY_ONLY, _PERIOD_RANGE, _PERIOD_LAST = p["period_day"], p["period_day_only"], p["period_range"], p["period_last"]
    _PERIOD_QUARTER, _PERIOD_DAY_BEFORE, _PERIOD_DAY_PART, _PERIOD_HINT = p["period_quarter"], p["period_day_before_yesterday"], p["period_day_part"], p["period_hint"]
    _DETAIL_Q, _DETAIL_NOT = p["detail_q"], p["detail_not"]
    _DESCRIPTION_LEADING, _DESCRIPTION_TRAILING, _MERCHANT_IN = p["description_leading"], p["description_trailing"], p["merchant_in"]
    _REPLIES = {key: text for key, text in cues.replies.items() if key != "pending_none"}
    _REPLIES.update(dict.fromkeys(("confirm", "deny", "cancel"), cues.replies["pending_none"]))


_bind(intent_cues.current())
intent_cues.subscribe(_bind)  # admin kích hoạt bản mới (intent_cues.apply) thì gắn lại tên, có hiệu lực ngay trong tiến trình này

_KIND_BY_INTENT = {"add_expense": "expense", "add_income": "income", "transfer_money": "transfer"}
_ENTRY_INTENTS = tuple(_KIND_BY_INTENT)


@dataclass(frozen=True)
class Entry:
    kind: str  # expense | income | transfer
    amount: int  # đồng VND
    description: str
    merchant: str | None
    category: str | None  # slug backend (cùng bộ phân loại với món trên bill); None khi chưa biết; thu nhập/chuyển tiền không có, trừ "loan" (cho vay/mượn/trả nợ, cả hai chiều)
    income_type: str | None  # salary | bonus | gift | side_income | refund | other_income (khi kind = income và đoán được)
    auto_save_ok: bool  # đủ chắc để ghi ngay không cần hỏi (chỉ với chi tiêu)
    to_others: bool  # "chuyển cho người khác / trả nợ": tiền rời khỏi người dùng nên tính là chi tiêu
    category_source: str | None = None  # rule | cache | None: từ khóa quyết định, bảng nhớ tên món gợi ý, hoặc chưa biết
    occurred_at: datetime | None = None  # ngày nói trong câu ("hôm qua", "ngày 5/10"); None = không nói, backend dùng giờ hiện tại


@dataclass(frozen=True)
class Query:
    metric: str  # expense | income | balance
    period_key: str
    period_label: str
    start: datetime
    end: datetime
    category: str | None
    income_type: str | None
    categories: tuple[str, ...] = ()  # hỏi về HAI danh mục trở lên ("giải trí và mua sắm"): mọi danh mục được gọi tên; rỗng thì chỉ xét ``category``
    detail: bool = False  # xin xem CHI TIẾT ("chi tiết hôm nay", "liệt kê các khoản chi tháng này"): backend trả danh sách từng khoản của kỳ, không chỉ tổng


@dataclass(frozen=True)
class ChatParse:
    intent: str
    entry: Entry | None
    query: Query | None
    reply_text: str | None
    nlu_source: str  # rasa | rules
    nlu_intent: str | None
    nlu_confidence: float | None
    unclear_reason: str | None  # low_confidence | disagree | not_understood | None
    entries: tuple[Entry, ...] = ()  # mọi khoản trong câu (nhiều khoản khi câu có nhiều số tiền); ``entry`` = khoản đầu
    total_amount: int | None = None  # tổng người dùng nói ("tổng cộng 60k"): để đối chiếu, không phải một khoản chi
    total_mismatch: bool = False  # tổng đã nói khác tổng các khoản đọc được
    skipped: tuple[str, ...] = ()  # vế nối bằng "và/rồi" không có số tiền: không thành khoản, báo cho người dùng
    goal: GoalDraft | None = None  # ý định create_goal: bản nháp mục tiêu tiết kiệm để người dùng xác nhận (None = đang hỏi lại, xem reply_text)


def period_range(key: str, now: datetime) -> tuple[datetime, datetime]:
    today = now.replace(hour=0, minute=0, second=0, microsecond=0)
    if key == "hom nay":
        return today, today + timedelta(days=1)
    if key == "hom qua":
        return today - timedelta(days=1), today
    week = today - timedelta(days=today.weekday())  # tuần bắt đầu từ thứ Hai
    if key == "tuan nay":
        return week, week + timedelta(days=7)
    if key == "tuan truoc":
        return week - timedelta(days=7), week
    if key in ("nam nay", "nam ngoai", "nam truoc"):
        year = today.year - (0 if key == "nam nay" else 1)
        return today.replace(year=year, month=1, day=1), today.replace(year=year + 1, month=1, day=1)
    if named := _SPAN_KEY.fullmatch(key):  # "tu 2025-03-01 den 2025-03-10": khoảng ngày (gồm cả hai đầu) do _span_period dựng
        first, last = date.fromisoformat(named[1]), date.fromisoformat(named[2])
        return today.replace(year=first.year, month=first.month, day=first.day), today.replace(year=last.year, month=last.month, day=last.day) + timedelta(days=1)
    if named := _YEAR_KEY.fullmatch(key):  # "nam 2025"
        year = int(named[1])
        return today.replace(year=year, month=1, day=1), today.replace(year=year + 1, month=1, day=1)
    if named := _MONTH_KEY.fullmatch(key):  # "thang 9/2025": khóa do _month_period dựng, luôn kèm năm đã quy đổi
        year, month_number = int(named[2]), int(named[1])
        start = today.replace(year=year, month=month_number, day=1)
        return start, (start.replace(year=year + 1, month=1) if month_number == 12 else start.replace(month=month_number + 1))
    month = today.replace(day=1)
    if key == "thang truoc":
        return (month - timedelta(days=1)).replace(day=1), month
    return month, (month + timedelta(days=32)).replace(day=1)  # "thang nay" (mặc định)


_MONTH_KEY = re.compile(r"thang (\d{1,2})/(\d{4})")  # khóa kỳ "tháng N năm Y" do chính _month_period dựng (định danh trong code, không phải từ vựng)
_YEAR_KEY = re.compile(r"nam (\d{4})")  # khóa kỳ "năm Y" do _year_period dựng
_SPAN_KEY = re.compile(r"tu (\d{4}-\d{2}-\d{2}) den (\d{4}-\d{2}-\d{2})")  # khóa kỳ "từ ngày A đến ngày B" do _span_period dựng: mọi kỳ theo ngày (một ngày, khoảng ngày, N ngày qua, quý) quy về dạng này


def _month_period(norm: str, now: datetime) -> tuple[str, str] | None:
    """Kỳ "tháng 9" / "tháng 9/2025" trong câu hỏi tổng: (khóa kèm năm, nhãn) hoặc None khi câu không nói tháng cụ thể.
    Không nói năm thì lấy năm gần nhất không ở tương lai ("tháng 12" hỏi lúc đang tháng 10 là tháng 12 năm ngoái). "ngày 5 tháng 10" là hỏi một NGÀY nên không
    phải kỳ tháng; tháng ngoài 1-12 cũng không phải ("tháng 13")."""
    found = _PERIOD_MONTH.search(norm)
    if found is None or found["day"]:
        return None
    month_number = int(found["month"])
    if not 1 <= month_number <= 12:
        return None
    year = int(found["year"]) if found["year"] else (now.year if month_number <= now.month else now.year - 1)
    label = _REPLIES["period_month" if year == now.year else "period_month_year"].format(month=month_number, year=year)
    return f"thang {month_number}/{year}", label


def _year_period(norm: str) -> tuple[str, str] | None:
    """Kỳ "năm 2025" đứng riêng (không kèm tháng): (khóa, nhãn) hoặc None."""
    found = _PERIOD_YEAR.search(norm)
    return (f"nam {found['year']}", _REPLIES["period_year"].format(year=found["year"])) if found else None


def _safe_date(year: int, month: int, day: int) -> date | None:
    try:
        return date(year, month, day)
    except ValueError:  # 31/2, 0/5...
        return None


def _latest_past(month: int, day: int, today: date) -> date | None:
    """Ngày D/M gần nhất không ở tương lai ("ngày 15/12" hỏi lúc đang tháng 10 là 15/12 năm ngoái)."""
    found = _safe_date(today.year, month, day)
    return found if found is None or found <= today else _safe_date(today.year - 1, month, day)


def _add_months(day: date, months: int) -> date:
    year, month0 = divmod(day.year * 12 + day.month - 1 + months, 12)
    return date(year, month0 + 1, min(day.day, calendar.monthrange(year, month0 + 1)[1]))


def _day_text(day: date, today: date) -> str:
    return f"{day.day}/{day.month}" + (f"/{day.year}" if day.year != today.year else "")


def _span(first: date, last: date, label: str) -> tuple[str, str]:
    return f"tu {first.isoformat()} den {last.isoformat()}", label


def _range_dates(found: re.Match, today: date) -> tuple[date | None, date | None]:
    """Hai đầu của "từ ngày 1 đến ngày 10" / "từ 25/9 đến 5/10". Thiếu tháng thì lấy tháng còn lại; không nói tháng nào thì tháng hiện tại (tháng trước nếu ngày đầu chưa tới)."""
    d1, d2 = int(found["d1"]), int(found["d2"])
    if found["m1"] or found["m2"]:
        m1, m2 = int(found["m1"] or found["m2"]), int(found["m2"] or found["m1"])
        year = int(found["year"]) if found["year"] else today.year
    else:
        base = today if d1 <= today.day else _add_months(today.replace(day=1), -1)
        m1 = m2 = base.month
        year = int(found["year"]) if found["year"] else base.year

    def build(y: int) -> tuple[date | None, date | None]:
        return _safe_date(y, m1, d1), _safe_date(y + (m2 < m1), m2, d2)  # "từ 25/12 đến 5/1" sang năm sau

    first, last = build(year)
    if first and not found["year"] and first > today:
        first, last = build(year - 1)
    return first, last


def _span_period(norm: str, now: datetime) -> tuple[str, str] | None:
    """Kỳ theo ngày: khoảng ngày, một ngày cụ thể, hôm kia, N ngày/tuần/tháng qua, quý. Mọi kỳ quy về khóa "tu A den B" (A, B gồm cả hai đầu); (khóa, nhãn) hoặc None."""
    today = now.astimezone(VN).date()
    amount = parse_amount(norm)  # bộ đọc số tiền chung: "từ 3 đến 5 triệu" có số cuối là tiền, không phải ngày
    if (found := _PERIOD_RANGE.search(norm)) and not (amount and amount.start < found.end("d2") and amount.end > found.start("d2")):
        first, last = _range_dates(found, today)
        if first and last and first <= last:
            return _span(first, last, _REPLIES["period_range"].format(start=_day_text(first, today), end=_day_text(last, today)))
    if found := _PERIOD_DAY.search(norm):
        day, month = int(found["day"]), int(found["month"])
        target = _safe_date(int(found["year"]), month, day) if found["year"] else _latest_past(month, day, today)
        if target:
            return _span(target, target, _REPLIES["period_day_year" if target.year != today.year else "period_day"].format(day=day, month=month, year=target.year))
    if found := _PERIOD_LAST.search(norm):
        if found["days"] and int(found["days"]) >= 1:
            n = int(found["days"])
            return _span(today - timedelta(days=n - 1), today, _REPLIES["period_last_days"].format(n=n))
        if found["weeks"] and int(found["weeks"]) >= 1:
            n = int(found["weeks"])
            return _span(today - timedelta(days=7 * n - 1), today, _REPLIES["period_last_weeks"].format(n=n))
        if found["months"] and int(found["months"]) >= 1:
            n = int(found["months"])
            return _span(_add_months(today, -n) + timedelta(days=1), today, _REPLIES["period_last_months"].format(n=n))
    if found := _PERIOD_QUARTER.search(norm):
        year = int(found["year"]) if found["year"] else today.year
        if found["previous"] or found["current"]:
            quarter = (today.month - 1) // 3 + 1 - bool(found["previous"])
            if quarter == 0:
                quarter, year = 4, year - 1
        else:
            quarter = int(found["quarter"])
            if not found["year"] and date(year, quarter * 3 - 2, 1) > today:
                year -= 1
        last_month = quarter * 3
        label = _REPLIES["period_quarter_year" if year != today.year else "period_quarter"].format(quarter=quarter, year=year)
        return _span(date(year, last_month - 2, 1), date(year, last_month, calendar.monthrange(year, last_month)[1]), label)
    if _PERIOD_DAY_BEFORE.search(norm):
        target = today - timedelta(days=2)
        return _span(target, target, _REPLIES["period_day_before_yesterday"])
    return None


def _day_of_month_period(norm: str, now: datetime) -> tuple[str, str] | None:
    """"ngày 15" không nói tháng: tháng hiện tại, hoặc tháng trước nếu ngày đó chưa tới."""
    found = _PERIOD_DAY_ONLY.search(norm)
    if found is None:
        return None
    today = now.astimezone(VN).date()
    day = int(found["day"])
    base = today if day <= today.day else _add_months(today.replace(day=1), -1)
    target = _safe_date(base.year, base.month, day)
    if target is None:
        return None
    return _span(target, target, _REPLIES["period_day_year" if target.year != today.year else "period_day"].format(day=day, month=target.month, year=target.year))


def _day_part_period(norm: str) -> tuple[str, str] | None:
    """"sáng/chiều/tối nay" là hôm nay, "đêm qua" là hôm qua: kỳ có sẵn, tính cả ngày."""
    found = _PERIOD_DAY_PART.search(norm)
    if found is None:
        return None
    key = "hom nay" if found["today"] else "hom qua"
    return key, dict(_PERIODS)[key]


def _resolve_period(norm: str, now: datetime) -> tuple[str, str] | None:
    """(khóa kỳ, nhãn) của kỳ câu hỏi nói tới, hoặc None khi câu không nói kỳ nào đọc được. Thứ tự: theo ngày, tháng, năm, kỳ có sẵn (hôm nay, tuần trước...), buổi trong ngày, "ngày N" trần."""
    return (_span_period(norm, now) or _month_period(norm, now) or _year_period(norm)
            or next(((key, label) for key, label in _PERIODS if key in norm), None) or _day_part_period(norm) or _day_of_month_period(norm, now))


def _unresolved_period(norm: str, now: datetime) -> bool:
    """Câu hỏi tổng có từ nghe như nói về thời gian mà không kỳ nào đọc được ("cuối tuần rồi", "ngày 31/2"): hỏi lại, đừng đoán "tháng này"."""
    return bool(_PERIOD_HINT.search(norm)) and _resolve_period(norm, now) is None


def _names_a_period(norm: str, now: datetime) -> bool:
    """Câu có nói một kỳ cụ thể (hôm nay, tuần trước, tháng 9, ngày 15/3, 7 ngày qua, năm 2025...)? Dùng làm bằng chứng cho câu hỏi tổng khi phân xử với Rasa."""
    return _resolve_period(norm, now) is not None  # (cùng ý với _mentions_period nhưng loại kỳ không hợp lệ như "tháng 13", "ngày 31/2")


def _label_index(labels: dict[str, str]) -> dict[str, str]:
    return {re.sub(r"\s+", " ", normalize(v)): k for k, v in labels.items()}


def _bind_vocab() -> None:
    """Dựng các giá trị dẫn xuất từ từ vựng chat (dictionaries/*.json hoặc bản admin kích hoạt trong DB). Gọi lại mỗi khi từ vựng đổi (``vocab.subscribe``):
    các hàm bên dưới tra tên toàn cục lúc chạy nên có hiệu lực ngay."""
    global CATEGORIES, INCOME_TYPES, _SLUG_BY_LABEL, _INCOME_BY_LABEL
    CATEGORIES, INCOME_TYPES = tuple(vocab.CATEGORY_LABELS), tuple(vocab.INCOME_LABELS)
    _SLUG_BY_LABEL, _INCOME_BY_LABEL = _label_index(vocab.CATEGORY_LABELS), _label_index(vocab.INCOME_LABELS)


_bind_vocab()
vocab.subscribe(_bind_vocab)


def category_from_text(norm: str, kind: str = "expense") -> str | None:
    """Danh mục (hoặc loại thu nhập nếu kind="income") được nhắc trong câu: tên danh mục hoặc từ vựng chat; từ khóa dài nhất thắng."""
    slugs = set(INCOME_TYPES) if kind == "income" else set(CATEGORIES)
    labels = _INCOME_BY_LABEL if kind == "income" else _SLUG_BY_LABEL
    best = None
    for label_norm, slug in labels.items():
        if re.search(rf"(?<![a-z]){re.escape(label_norm)}(?![a-z])", norm) and (best is None or len(label_norm) > best[0]):
            best = (len(label_norm), slug)
    for slug, words in vocab.CHAT_KEYWORDS.items():
        if slug not in slugs:
            continue
        for w in words:
            if re.search(rf"(?<![a-z]){re.escape(w)}(?![a-z])", norm) and (best is None or len(w) > best[0]):
                best = (len(w), slug)
    return best[1] if best else None


def _category_from_label(norm: str) -> str | None:
    """Tên danh mục gõ thẳng trong câu ("chi cho giáo dục 500k", "tiêu cho ăn uống"): nhãn dài nhất thắng."""
    best = None
    for label_norm, slug in _SLUG_BY_LABEL.items():
        if re.search(rf"(?<![a-z]){re.escape(label_norm)}(?![a-z])", norm) and (best is None or len(label_norm) > best[0]):
            best = (len(label_norm), slug)
    return best[1] if best else None


def categories_named(norm: str) -> tuple[str, ...]:
    """Mọi danh mục được gọi TÊN trong câu ("giải trí và mua sắm"), theo thứ tự xuất hiện, bỏ trùng slug backend. Chỉ tên danh mục, không tính từ vựng chat:
    từ vựng chat ("tiêu", "tiền") khớp ở khắp nơi nên không dùng để quyết định hỏi mấy danh mục."""
    hits = []
    for label_norm, key in _SLUG_BY_LABEL.items():
        m = re.search(rf"(?<![a-z]){re.escape(label_norm)}(?![a-z])", norm)
        if m:
            hits.append((m.start(), SLUG_BY_LOCAL_CATEGORY.get(key, key)))
    ordered: list[str] = []
    for _, slug in sorted(hits):
        if slug not in ordered:
            ordered.append(slug)
    return tuple(ordered)


def expense_category(text: str) -> str | None:
    """Slug backend của khoản chi: tên danh mục gõ thẳng trong câu trước (người dùng nói rõ), rồi bộ phân loại chung với bill. None = chưa rõ.

    Không khớp gần đúng: đó là cho lỗi OCR, còn câu chat bình thường dễ bị nó khớp nhầm.
    """
    label = _category_from_label(normalize(text))
    if label:
        return SLUG_BY_LOCAL_CATEGORY.get(label, label)
    return resolve(text, chat=True).slug


def _direction(text: str) -> tuple[str, str | None]:
    """(chiều giao dịch, phạm vi bằng chứng) theo dấu hiệu trong câu.

    Phạm vi bằng chứng cho biết dấu hiệu mạnh đến đâu so với lời của model:
      "any"  = dấu hiệu CỤ THỂ cho đúng chiều này (trả nợ cho mình, phí, người thân gửi mình, bán, rút / nạp vào ví của mình): thắng model dù model chắc đến đâu,
               vì độ chắc của Rasa là softmax chưa hiệu chỉnh (đo được sai ở mức 1,00);
      "flow" = chỉ nói tiền ĐI RA ("cho mẹ"): đủ để bác bỏ "tiền vào", không đủ để chọn giữa chi tiêu và chuyển tiền;
      None   = chỉ là chủ đề (lương, thưởng...) hoặc mặc định chi tiêu: model được quyền quyết khi nó chắc.
    """
    low = unicodedata.normalize("NFC", text).lower()
    norm = normalize(text)
    if _DEBT_IN.search(norm):
        return "income", "any"  # bạn trả nợ / cho tôi mượn: tiền vào, kể cả khi câu có chữ "trả nợ" vốn là dấu hiệu chuyển tiền ra
    if _FEE.search(norm):
        return "expense", "any"
    giving = bool(_GIVING_AWAY.search(norm))
    not_income = bool(_NOT_INCOME.search(norm))
    income = bool(_INCOME_ACCENTED.search(low) or _INCOME_PLAIN.search(norm)) and not not_income
    transfer = bool(_TRANSFER_CUE.search(norm))
    if income and (not transfer or _RECEIVED.search(norm) or _INCOME_ACCENTED.search(low)):
        specific = bool(_INCOME_SPECIFIC.search(low) or _INCOME_SPECIFIC_PLAIN.search(norm)) and not giving
        return "income", "any" if specific else None
    if transfer:
        return "transfer", "any" if (_WITHDRAW.search(norm) or _OWN_WALLET.search(norm)) else None
    return "expense", "flow" if giving else None


def detect_kind(text: str) -> str:
    """"expense" | "income" | "transfer" theo dấu hiệu trong câu (luật; Rasa có thể ghi đè khi rất chắc)."""
    return _direction(text)[0]


def _merchant_in(description: str) -> str | None:
    m = _MERCHANT_IN.search(description)
    return m.group(1).strip() if m and len(m.group(1).strip()) >= 3 else None


def is_query(norm: str) -> bool:
    return bool(_QUERY.search(norm)) or _is_detail(norm)


def _mentions_period(norm: str) -> bool:
    return bool(any(p.search(norm) for p in (_PERIOD_MONTH, _PERIOD_YEAR, _PERIOD_DAY, _PERIOD_DAY_ONLY, _PERIOD_RANGE, _PERIOD_LAST, _PERIOD_QUARTER, _PERIOD_DAY_BEFORE, _PERIOD_DAY_PART))
                or any(key in norm for key, _ in _PERIODS))


def _is_detail(norm: str) -> bool:
    """Xin xem CHI TIẾT các khoản ("chi tiết hôm nay", "liệt kê các khoản chi tháng này", "hôm nay tôi mua gì"): có cách nói xin danh sách, KHÔNG phải lời khuyên/ý định
    ("nên mua gì"), và có ngữ cảnh tiền (nêu một kỳ, có từ chi tiêu, hoặc chữ "khoản") để "chi tiết" đứng một mình không bị coi là câu hỏi chi tiêu."""
    return bool(_DETAIL_Q.search(norm) and not _DETAIL_NOT.search(norm) and (_mentions_period(norm) or _SPEND_Q.search(norm) or "khoan" in norm))


def is_question(text: str, norm: str) -> bool:
    """Câu hỏi, không phải khoản ghi: kết thúc bằng dấu hỏi hoặc tiểu từ hỏi, hoặc mở đầu bằng "có nên", "nên", "liệu", "có phải"."""
    return text.rstrip().endswith("?") or norm.endswith(_QUESTION_PARTICLES) or bool(_QUESTION_LEAD.match(norm))






def _rule_intent(text: str, norm: str) -> str:
    if parse_amount(text):
        return {"expense": "add_expense", "income": "add_income", "transfer": "transfer_money"}[detect_kind(text)]
    if is_query(norm):
        return "ask_spending"
    if norm in _GREET:
        return "greet"
    if norm in _THANKS or (len(norm.split()) <= 6 and _THANKS_LEAD.search(norm)):  # câu ngắn mở đầu bằng lời cảm ơn; câu có số tiền đã rẽ nhánh ở trên
        return "thanks"
    # Cụm chi tiêu không kèm số tiền ("ăn cơm", "mua nước", "đi ăn"): hỏi số tiền thay vì bảo ngoài chủ đề. Câu hỏi đã bị chặn trước đó.
    return "needs_amount" if _EXPENSE_VERB.match(norm) or expense_category(norm) else "out_of_scope"


def _is_social_question(text: str, norm: str, nlu: dict | None) -> bool:
    """Nguyên tắc 1 — HÌNH THỨC câu quyết định câu không phải khoản ghi cũng không phải hỏi tổng chi tiêu của mình:
    hỏi giá thị trường, lệnh quản lý dữ liệu/tài khoản, và câu hỏi có/không kiểu nói chuyện."""
    has_amount = bool(parse_amount(text))
    if not has_amount and _PRICE_Q.search(norm) and not _MONEY_WORD.search(norm):
        return True
    if not has_amount and _DATA_OPS.search(norm):
        return True
    if not is_question(text, norm) or is_query(norm):
        return False
    if _SPEND_Q.search(norm) and _mentions_period(norm):
        return False  # hỏi về chi tiêu của một kỳ ("tháng này tiêu nhiều nhất cho khoản nào"): từ để hỏi cuối câu không làm nó thành câu hỏi lạc đề
    if not (nlu and nlu["intent"] in _REPLY_INTENTS):
        return True
    # Model cho là lời đáp: lời đáp thật thì ngắn hoặc mở đầu bằng từ đáp; câu dài kết thúc bằng tiểu từ hỏi là một câu hỏi.
    return norm.endswith(_QUESTION_PARTICLES) and len(norm.split()) >= 2 and not norm.startswith(_ANSWER_STARTS)


def _purchase_evidence(norm: str) -> bool:
    """Câu có tên một thứ được mua, hoặc cho tặng, hoặc trả/thanh toán: cùng số tiền thì đủ là bằng chứng của một khoản ghi."""
    return bool(expense_category(norm) or _GIVING_AWAY.search(norm) or _PAYING.search(norm))


def _income_evidence(text: str, rule: str) -> bool:
    """Số tiền kèm dấu hiệu tiền vào CỤ THỂ ("được thưởng", "bán được", "hoàn lại", "cho thuê") là một khoản thu, cùng sức nặng với bằng chứng mua hàng của chi tiêu:
    Rasa nói ngoài chủ đề (có khi ở 1,00) cũng không đủ để từ chối."""
    return rule == "add_income" and _direction(text)[1] == "any"


def _has_diacritics(text: str) -> bool:
    return fold(text) != text.lower()


def _flow(intent: str) -> str:
    return "in" if intent == "add_income" else "out"  # chi tiêu và chuyển tiền đều là tiền đi ra


def _arbitrate(text: str, norm: str, rule: str, nlu: dict, now: datetime) -> str:
    """Rasa và luật bất đồng thì ai thắng. Mỗi nguyên tắc viết một lần, không theo từng câu:
      2. Số tiền kèm bằng chứng giao dịch là một khoản ghi, kể cả khi Rasa nói ngoài chủ đề.
      2b. Câu hỏi tổng NÊU MỘT KỲ cụ thể ("tháng 9", "năm 2025", "tuần trước") và nói về CHÍNH NGƯỜI DÙNG (đại từ "tôi/mình/tui" hoặc từ hỏi chi tiêu "tiêu", "đã chi")
          là câu hỏi chi tiêu, kể cả khi Rasa nói ngoài chủ đề: mô hình chưa thấy đủ cách nói kỳ (câu có số năm từng bị đoán out_of_scope với độ chắc 0,98).
          Câu về chủ thể khác ("thưởng tết năm nay các công ty bao nhiêu") là kiến thức chung nên vẫn theo Rasa.
      3. Chỉ từ hỏi tổng (bao nhiêu, tổng chi...) mới làm câu hỏi chi tiêu; câu có số tiền mà không có từ hỏi là khoản ghi.
      4. Chiều giao dịch: dấu hiệu CỤ THỂ trong câu thắng model (xem _direction); độ chắc của Rasa chưa hiệu chỉnh nên không dùng làm lý do ngược lại.
      5. Bằng chứng YẾU (tên một thứ được mua, hay mặc định chi tiêu) chỉ thắng lời "thu nhập/chuyển tiền" của model khi model không chắc,
         hoặc khi lời đó không có dấu hiệu nào trong câu nâng đỡ.
    """
    name, confidence = nlu["intent"], nlu["confidence"]
    if name in _REPLY_CUES and not _REPLY_CUES[name].search(norm):
        return rule  # Rasa gán xác nhận/từ chối/hủy mà câu không có từ nào của loại đó: chuỗi vô nghĩa hay emoji (độ chắc 0,88-1,00 vẫn sai), để luật quyết
    has_entry = rule in _ENTRY_INTENTS  # luật đã tìm thấy số tiền
    if name == "out_of_scope" and has_entry and (confidence < OUT_OF_SCOPE_OVERRIDE or _purchase_evidence(norm) or _income_evidence(text, rule)):
        return rule
    if name == "out_of_scope" and rule == "ask_spending" and (
            confidence < OUT_OF_SCOPE_OVERRIDE or (_names_a_period(norm, now) and (_SPEND_Q.search(norm) or _SELF_REFERENCE.search(norm) or _is_detail(norm)))
            or (_BALANCE_Q.search(norm) and _SELF_REFERENCE.search(norm))):
        return rule
    if name in _ENTRY_INTENTS and rule == "ask_spending":
        return rule
    if name == "ask_spending" and not is_query(norm) and (has_entry or not _MONEY_WORD.search(norm)):
        return rule
    if name in _ENTRY_INTENTS and has_entry and name != rule:
        _, scope = _direction(text)
        if scope == "any" or (scope == "flow" and _flow(name) != _flow(rule)):
            return rule
        if rule == "add_expense" and expense_category(norm) and _has_diacritics(text):
            return rule  # câu không dấu: "ban" có thể là bạn/bán nên từ vựng kém tin cậy, nhường cho model
        if confidence < KIND_TRUST:
            return rule
    return name


def _resolve_intent(text: str, norm: str, nlu: dict | None, now: datetime) -> str:
    if _is_social_question(text, norm, nlu):
        return "out_of_scope"
    rule = _rule_intent(text, norm)
    if not nlu or rule in ("greet", "thanks"):
        return rule  # chào/cảm ơn khớp nguyên cụm: Rasa không có ý định "thanks" nên không có gì để cân nhắc
    return _arbitrate(text, norm, rule, nlu, now)


def _unclear_reason(text: str, norm: str, intent: str, nlu: dict | None) -> str | None:
    """Vì sao câu này đáng để người dùng (đồng ý chia sẻ) gửi về xem lại; None nếu hiểu rõ."""
    kinds = _ENTRY_INTENTS + ("ask_spending",)
    rule = _rule_intent(text, norm)
    if nlu and rule in kinds and nlu["intent"] in kinds and rule != nlu["intent"]:
        return "disagree"
    if nlu and nlu["confidence"] < UNCERTAIN_BELOW and intent not in ("greet", "goodbye"):
        return "low_confidence"
    if intent == "out_of_scope" and not is_question(text, norm) and len(text) <= 120:
        return "not_understood"
    return None


def _auto_ok(kind: str, value: int, category: str | None, nlu: dict | None, auto_below: int) -> bool:
    """Ghi ngay (không hỏi) chỉ khi: chi tiêu, đã biết danh mục, số tiền dưới ngưỡng, và Rasa (nếu có) chắc rằng đây là chi tiêu."""
    if auto_below <= 0 or kind != "expense" or category is None or value >= auto_below:
        return False
    return nlu is None or (nlu["intent"] == "add_expense" and nlu["confidence"] >= AUTO_CONFIDENCE)


def _entry(
    text: str,
    kind: str,
    nlu: dict | None,
    auto_below: int,
    fallback_category: Callable[[str | None, str], str | None] | None,
    now: datetime,
    default_when: datetime | None = None,
) -> Entry | None:
    when = parse_when(text, now)
    entry = _entry_core(remove_when(text, when) if when else text, kind, nlu, auto_below, fallback_category)
    if not entry:
        return None
    if entry.auto_save_ok and _STRAY_AMOUNT.search(normalize(entry.description)):  # còn số tiền thứ hai trong mô tả ("ăn 20k,30k"): không ghi ngay một con số có thể sai
        entry = replace(entry, auto_save_ok=False)
    return replace(entry, occurred_at=when.value if when else default_when)


def _entry_core(text: str, kind: str, nlu: dict | None, auto_below: int, fallback_category: Callable[[str | None, str], str | None] | None) -> Entry | None:
    entities = (nlu or {}).get("entities", {})
    match = parse_amount(text)
    if match:
        value, description = match.value, remove_amount(text, match)
        assumed = match.assumed
    else:  # luật không thấy số tiền nhưng Rasa cắt được một cụm số tiền: đọc cụm đó
        phrase = entities.get("amount")
        found = parse_amount(phrase) if phrase and not _PERCENT.search(normalize(text)) else None  # "giảm năm mươi phần trăm": Rasa cắt "năm mươi", mất chữ phần trăm
        if found and _SLANG_FOLDED.intersection(fold(w) for w in re.findall(r"[^\W\d_]+", phrase)):
            # Luật đã THẤY cụm này trong cả câu và loại nó ("hai củ" trong "mua hai củ cải", "hai lít" trong "đổ hai lít xăng"); đọc riêng cụm Rasa cắt ra
            # thì mất ngữ cảnh đó và biến củ cải, lít xăng thành tiền. Từ lóng chỉ được đọc khi có cả câu.
            found = None
        if not found:
            return None
        value, description = found.value, re.sub(r"\s+", " ", text.replace(phrase, " ")).strip()
        assumed = found.assumed
    description = _DESCRIPTION_LEADING.sub("", description).strip()
    description = _DESCRIPTION_TRAILING.sub("", description).strip()  # "ăn bún chả hết 55 cành" -> "ăn bún chả"
    norm_text = normalize(text)
    debt_in = bool(_DEBT_IN.search(norm_text))
    if kind != "income" and not debt_in and _LEND_OUT.search(norm_text):
        return Entry("expense", value, description, None, LOAN_SLUG, None, False, True, category_source="rule")  # cho mượn/cho vay: danh mục riêng, không tự ghi
    if kind == "transfer":
        if not _OWN_ACCOUNT.search(norm_text) and _TO_OTHERS.search(norm_text):
            # Trả nợ cũng là nợ nần (cùng danh mục "Cho vay / Mượn", báo cáo bỏ); chuyển tiền cho người khác thì là chi tiêu "Khác".
            category = LOAN_SLUG if _PAY_DEBT.search(norm_text) else "other"
            return Entry("expense", value, description, None, category, None, False, True)  # tiền rời khỏi mình
        return Entry("transfer", value, description, None, None, None, False, False)
    if kind == "income":
        if debt_in:
            return Entry("income", value, description, None, LOAN_SLUG, None, False, False)  # bạn trả nợ / mượn tiền: tiền vào nhưng không phải thu nhập
        income_type = category_from_text(normalize(description), "income") or category_from_text(norm_text, "income")
        return Entry("income", value, description, None, None, income_type, False, False)
    merchant = _merchant_in(description) or entities.get("merchant")
    category = expense_category(" ".join(part for part in (merchant, description) if part))
    if category is None and fallback_category is not None:
        category = fallback_category(merchant, description)
    if assumed:
        value *= 1000  # số nhỏ không đơn vị ("ăn phở bốn mươi lăm"): nghìn. Đoán nên không bao giờ tự lưu
    return Entry("expense", value, description, merchant, category, None, _auto_ok("expense", value, category, nlu, auto_below) and not assumed, False,
                 category_source="rule" if category else None)


def _query(norm: str, now: datetime) -> Query:
    key, label = _resolve_period(norm, now) or (_DEFAULT_PERIOD, dict(_PERIODS)[_DEFAULT_PERIOD])
    start, end = period_range(key, now)
    if _BALANCE_Q.search(norm):
        return Query("balance", key, label, start, end, None, None)
    detail = _is_detail(norm)
    if _INCOME_Q.search(norm) and not _SPEND_Q.search(norm):
        return Query("income", key, label, start, end, None, category_from_text(norm, "income"), detail=detail)
    named = categories_named(norm)
    if len(named) > 1:
        return Query("expense", key, label, start, end, named[0], None, named, detail)  # "giải trí và mua sắm": cả hai, không chỉ danh mục dài nhất
    return Query("expense", key, label, start, end, category_from_text(norm), None, detail=detail)


def _money(amount: int) -> str:
    return f"{amount:,}".replace(",", ".") + " ₫"


def _total_check(stated: int | None, entries: tuple[Entry, ...]) -> dict:
    if stated is None:
        return {}
    return {"total_amount": stated, "total_mismatch": stated != sum(e.amount for e in entries)}


def parse_chat(
    text: str,
    nlu: dict | None = None,
    *,
    now: datetime | None = None,
    auto_save_below: int = 2_000_000,
    fallback_category: Callable[[str | None, str], str | None] | None = None,
) -> ChatParse:
    """nlu: kết quả Rasa {"intent", "confidence", "entities"} hoặc None. fallback_category(merchant, mô tả) -> nhóm 9 nhóm hoặc None."""
    text = clean_text(text or "").strip()
    now = now or datetime.now(VN)
    norm = re.sub(r"\s+", " ", normalize(text)).strip(" .!?,")
    if nlu is not None and nlu["confidence"] < MIN_NLU_CONFIDENCE:
        nlu = None
    source, nlu_intent = ("rasa", nlu["intent"]) if nlu else ("rules", None)
    confidence = round(nlu["confidence"], 3) if nlu else None

    def result(intent: str, **extra) -> ChatParse:
        return ChatParse(intent, extra.get("entry"), extra.get("query"), extra.get("reply_text"), source, nlu_intent, confidence,
                         extra.get("unclear_reason"), extra.get("entries", ()),
                         extra.get("total_amount"), extra.get("total_mismatch", False), extra.get("skipped", ()), extra.get("goal"))

    if not norm:
        return result("empty", reply_text=_REPLIES["empty"])
    if (goal := parse_goal(text, now)) is not None and not is_query(norm):
        return result("create_goal", goal=goal.draft, reply_text=goal.reply or _REPLIES["goal"])
    intent = _resolve_intent(text, norm, nlu, now)
    reason = _unclear_reason(text, norm, intent, nlu)
    if intent in _ENTRY_INTENTS:
        text, cancelled = apply_cancellations(text)  # "rồi hủy", "nhầm, là 35k": áp trước khi tách khoản
        if cancelled and not parse_amount(text):
            return result("out_of_scope", reply_text=_REPLIES["cancelled"], unclear_reason="cancelled")
        suspicious = has_unresolved_cancel(text)  # còn chữ hủy/nhầm không áp được: không tự ghi
        text, stated_total = extract_total(text)  # "... tổng cộng 60k": số đối chiếu, không phải một khoản
        text = ", ".join(split_on_amounts(text))  # "ăn cơm 25k uống nước 10k" (giọng nói không có dấu câu): tách theo số tiền
        text, skipped = split_amountless(text)
        clauses = split_clauses(text)
        if len(clauses) > 1:  # nhiều khoản: loại từng khoản đọc theo luật (Rasa chỉ hiểu cả câu), và không bao giờ ghi ngay
            whole = parse_when(text, now)  # ngày nói ở một vế ("... cà phê 30k hôm qua") áp cho các vế không có ngày riêng
            many = tuple(
                replace(e, auto_save_ok=False)
                for c in clauses
                if (e := _entry(c, detect_kind(c), None, auto_save_below, fallback_category, now, whole.value if whole else None))
            )
            if len(many) > 1:  # mỗi khoản một dòng, kể cả cùng danh mục: gộp làm mất ý "ăn vặt" khác "đi chợ" khác "uống nước"
                return result(intent, entry=many[0], entries=many, unclear_reason=reason, skipped=skipped,
                              **_total_check(stated_total, many))
        entry = _entry(text, _KIND_BY_INTENT[intent], nlu, auto_save_below, fallback_category, now)
        if entry is None:
            return result("needs_amount", reply_text=_REPLIES["needs_amount"], unclear_reason=reason)
        if skipped or suspicious or reason == "disagree" or _AMBIGUOUS_SEND.search(norm) or (stated_total is not None and stated_total != entry.amount):  # có điều chưa khớp: không ghi ngay
            entry = replace(entry, auto_save_ok=False)  # "disagree": luật và Rasa bất đồng loại giao dịch, kể cả khi Rasa chắc 1,0 và thắng
        return result(intent, entry=entry, entries=(entry,), unclear_reason=reason, skipped=skipped, **_total_check(stated_total, (entry,)))
    if intent == "ask_spending":
        if _unresolved_period(norm, now):  # nói về thời gian mà không đọc được: hỏi lại, không trả số của "tháng này" như thể đó là điều được hỏi
            return result("needs_period", reply_text=_REPLIES["needs_period"], unclear_reason=reason)
        return result(intent, query=_query(norm, now), unclear_reason=reason)
    return result(intent, reply_text=_REPLIES.get(intent, _REPLIES["out_of_scope"]), unclear_reason=reason)
