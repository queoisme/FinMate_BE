"""Hiểu câu ĐẶT NGÂN SÁCH, THÊM KHOẢN ĐỊNH KỲ và GHI KHOẢN VAY NỢ bằng chat:
"gia hạn gói Claude Pro 600k hằng tháng ngày 5", "đặt ngân sách ăn uống 2 triệu", "bạn An nợ tôi 700k".

Chỉ ĐỌC câu và trả bản nháp (``ItemDraft``) hoặc câu hỏi lại cho phần còn thiếu (``ItemResult.reply``); app tự gọi API tạo khi bản nháp đủ thông tin, không phải chỗ này.
Câu hỏi lại được app nhớ và ghép với tin kế tiếp rồi phân tích lại (server không giữ trạng thái), nên người dùng chỉ cần nói phần thiếu.

Mọi từ vựng (động từ, chu kỳ, từ chỉ người, câu hỏi lại) nằm ở app/data/intent_cues.json (hoặc bản admin kích hoạt trong DB); ở đây chỉ có cơ chế.
Hướng vay nợ: "A nợ tôi" = MÌNH cho vay; "tôi nợ A" = MÌNH đi vay. Không rõ tên người thì hỏi lại, không đoán.
Chỉ câu nói về KHOẢN NỢ mới vào đây. "cho A vay", "A cho tôi mượn", "A trả nợ tôi" là lúc tiền đi hoặc về: giao dịch loại cho vay/mượn do bộ ghi chi tiêu xử lý như trước.
"""

from __future__ import annotations

import calendar
import re
import unicodedata
from collections.abc import Callable
from dataclasses import dataclass
from datetime import date, datetime

from app.services import intent_cues
from app.services.amount_normalizer import normalize, parse_amount
from app.services.vi_text import fold

BUDGET, RECURRING, LOAN = "budget", "recurring", "loan"
_CADENCES = (("monthly", "item_cadence_month"), ("weekly", "item_cadence_week"), ("yearly", "item_cadence_year"))
_SLASH_GROUPS = (("monthly", "month"), ("weekly", "week"), ("yearly", "year"))  # tên nhóm trong mẫu item_cadence_slash (hợp đồng giữa mẫu và code)


def _bind(cues: intent_cues.Cues) -> None:
    """Gắn mẫu và danh sách từ của câu tạo ngân sách / khoản định kỳ / vay nợ. Chỉ gọi với bản đã qua intent_cues.build."""
    global _P, _SELF, _STOP_ACC, _STOP_FOLD, _CUT_ACC, _CUT_FOLD
    _P = {name: pattern for name, pattern in cues.patterns.items() if name.startswith("item_")}
    _SELF = frozenset(fold(word) for word in cues.lists["item_self_words"])
    _STOP_ACC, _STOP_FOLD = _word_sets(cues.lists["item_name_stopwords"])
    _CUT_ACC, _CUT_FOLD = _word_sets(cues.lists["item_name_cutoff"])


def _word_sets(words: tuple[str, ...]) -> tuple[frozenset[str], frozenset[str]]:
    return frozenset(unicodedata.normalize("NFC", word.lower()) for word in words), frozenset(fold(word) for word in words)


_bind(intent_cues.current())
intent_cues.subscribe(_bind)


@dataclass(frozen=True)
class ItemDraft:
    kind: str  # budget | recurring | loan
    amount: int  # đồng VND (hạn mức / số tiền mỗi lần / số tiền vay)
    name: str | None = None  # recurring: tên khoản; loan: tiêu đề ("Cho Bạn An vay")
    category: str | None = None  # budget: slug nhóm chi
    cadence: str | None = None  # recurring: weekly | monthly | yearly
    next_due: date | None = None  # recurring: ngày thanh toán tiếp theo
    direction: str | None = None  # loan: borrow | lend
    counterparty: str | None = None  # loan: tên người


@dataclass(frozen=True)
class ItemResult:
    """``draft`` có thì đủ thông tin để tạo; không thì ``reply`` là câu hỏi lại."""

    draft: ItemDraft | None
    reply: str | None


def _replies() -> dict[str, str]:
    return intent_cues.current().replies  # đọc lúc gọi: bản admin vừa kích hoạt có hiệu lực ngay


def _in(token: str, accented: frozenset[str], folded: frozenset[str]) -> bool:
    """Chữ có dấu chỉ khớp đúng dạng có dấu trong danh sách ('nhà' không bị coi là 'nha'); chữ không dấu khớp theo dạng bỏ dấu."""
    lowered = unicodedata.normalize("NFC", token.lower())
    return lowered in accented or (fold(lowered) == lowered and lowered in folded)


def _words(nfc: str, start: int, end: int, reverse: bool) -> list[str]:
    """Tối đa hai chữ liền kề làm TÊN người: dừng ở số, từ chỉ tiền/thời gian (item_name_cutoff) hoặc từ chỉ chính mình."""
    tokens = [m.group(0).strip(".,;:!?\"'") for m in re.finditer(r"\S+", nfc[start:end])]
    tokens = [t for t in tokens if t]
    if reverse:
        tokens.reverse()
    kept: list[str] = []
    for token in tokens:
        if len(kept) == 2 or not re.fullmatch(r"[^\W\d_]+", token) or _in(token, _CUT_ACC, _CUT_FOLD) or fold(token) in _SELF:
            break
        kept.append(token)
    if reverse:
        kept.reverse()
    return kept


def _display(words: list[str]) -> str | None:
    return " ".join(w[:1].upper() + w[1:] for w in words)[:80] or None


def _mask(norm: str, spans: list[tuple[int, int]]) -> str:
    return "".join(" " if any(s <= i < e for s, e in spans) else c for i, c in enumerate(norm))


def _span(match: re.Match | None) -> list[tuple[int, int]]:
    return [(match.start(), match.end())] if match else []


def _find(nfc: str, phrase: str) -> tuple[int, int] | None:
    start = nfc.lower().find(phrase.lower())
    return (start, start + len(phrase)) if start >= 0 else None


def _loan(nfc: str, norm: str) -> ItemResult | None:
    replies = _replies()
    owes_me, i_owe = _P["item_loan_owes_me"].search(norm), _P["item_loan_i_owe"].search(norm)
    spans: list[tuple[int, int]]
    if owes_me:
        direction, who = "lend", _display(_words(nfc, 0, owes_me.start(), reverse=True))
        spans = [(owes_me.start(), owes_me.end())]
    elif i_owe:
        direction, who = "borrow", _display(_words(nfc, i_owe.end(), len(nfc), reverse=False))
        spans = [(i_owe.start(), i_owe.end())]
    else:
        return None

    if who and (found := _find(nfc, who)):
        spans.append(found)
    match = parse_amount(_mask(norm, spans))
    if match is None:
        return ItemResult(None, replies["item_loan_ask_amount"])
    if match.assumed:
        return ItemResult(None, replies["item_needs_unit"])
    if who is None:
        return ItemResult(None, replies["item_loan_ask_who"])
    title = replies["item_loan_title_lend" if direction == "lend" else "item_loan_title_borrow"].replace("{who}", who)
    return ItemResult(ItemDraft(LOAN, match.value, name=title, direction=direction, counterparty=who), None)


def _budget(norm: str, category_of: Callable[[str], str | None]) -> ItemResult | None:
    strong, weak = _P["item_budget_strong"].search(norm), _P["item_budget_weak"].search(norm)
    if not strong and not weak:
        return None
    replies = _replies()
    masked = _mask(norm, _span(strong) or _span(weak))
    match = parse_amount(masked)
    category = category_of(masked)
    if not strong and (match is None or category is None):
        return None  # "ngân sách còn bao nhiêu", "hạn mức thẻ": chưa phải câu đặt ngân sách
    if category is None:
        return ItemResult(None, replies["item_budget_ask_category"])
    if match is None:
        return ItemResult(None, replies["item_budget_ask_amount"])
    if match.assumed:
        return ItemResult(None, replies["item_needs_unit"])
    return ItemResult(ItemDraft(BUDGET, match.value, category=category), None)


def _next_due(norm: str, now: datetime) -> tuple[date | None, tuple[int, int] | None, bool]:
    """(ngày, vị trí trong câu, hợp lệ?): ngày nói không tồn tại (31/2) thì (None, vị trí, False) để hỏi lại."""
    today = now.date()
    if m := _P["item_due_slash"].search(norm):
        year = int(m.group("y")) if m.group("y") else None
        year = year + 2000 if year is not None and year < 100 else year
        try:
            due = date(year or today.year, int(m.group("m")), int(m.group("d")))
        except ValueError:
            return None, (m.start(), m.end()), False
        if year is None and due < today:
            due = due.replace(year=today.year + 1)
        return due, (m.start(), m.end()), True
    if m := _P["item_due_day"].search(norm):
        day = int(m.group("d"))
        if not 1 <= day <= 31:
            return None, (m.start(), m.end()), False
        if m.group("m"):
            try:
                due = date(today.year, int(m.group("m")), day)
            except ValueError:
                return None, (m.start(), m.end()), False
            return (due if due >= today else due.replace(year=today.year + 1)), (m.start(), m.end()), True
        month, year = today.month, today.year
        due = date(year, month, min(day, calendar.monthrange(year, month)[1]))
        if due < today:
            month, year = (1, year + 1) if month == 12 else (month + 1, year)
            due = date(year, month, min(day, calendar.monthrange(year, month)[1]))
        return due, (m.start(), m.end()), True
    for name, offset in (("item_due_today", 0), ("item_due_tomorrow", 1)):
        if m := _P[name].search(norm):
            return date.fromordinal(today.toordinal() + offset), (m.start(), m.end()), True
    return None, None, True


def _cadence(norm: str) -> tuple[str | None, list[tuple[int, int]]]:
    for cadence, name in _CADENCES:
        if m := _P[name].search(norm):
            return cadence, [(m.start(), m.end())]
    if m := _P["item_cadence_slash"].search(norm):
        return next(cadence for cadence, group in _SLASH_GROUPS if m.group(group)), [(m.start(), m.end())]
    return None, []


def _recurring_name(nfc: str, removed: list[tuple[int, int]]) -> str:
    kept = []
    for word in re.finditer(r"\S+", nfc):
        if any(start < word.end() and word.start() < end for start, end in removed):
            continue
        token = word.group(0).strip(".,;:!?\"'")
        if token and not _in(token, _STOP_ACC, _STOP_FOLD) and not re.fullmatch(r"[\d.,]+", token):
            kept.append(token)
    name = " ".join(kept)
    if len(name) < 2:
        return _replies()["item_recurring_default_name"]
    return name[:1].upper() + name[1:80]


def _recurring(nfc: str, norm: str, now: datetime) -> ItemResult | None:
    strong, weak, pay = _P["item_recurring_strong"].search(norm), _P["item_recurring_weak"].search(norm), _P["item_pay_verb"].search(norm)
    cadence, cadence_spans = _cadence(norm)
    if not strong and not weak and not (cadence and pay):
        return None
    replies = _replies()
    due, due_span, due_ok = _next_due(norm, now)
    removed = _span(strong) + _span(weak) + _span(pay) + cadence_spans + ([due_span] if due_span else [])
    match = parse_amount(_mask(norm, removed))
    if match is None and not strong:
        return None  # "hằng tháng tôi đóng tiền net": chưa có số tiền thì không phải khoản định kỳ
    if match is None:
        return ItemResult(None, replies["item_recurring_ask_amount"])
    if match.assumed:
        return ItemResult(None, replies["item_needs_unit"])
    if cadence is None:
        return ItemResult(None, replies["item_recurring_ask_cadence"])
    if due is None:
        return ItemResult(None, replies["item_recurring_ask_due"])
    name = _recurring_name(nfc, removed + [(match.start, match.end)])
    return ItemResult(ItemDraft(RECURRING, match.value, name=name, cadence=cadence, next_due=due), None)


def parse_item(text: str, now: datetime, category_of: Callable[[str], str | None]) -> ItemResult | None:
    """None nếu câu không phải tạo ngân sách / khoản định kỳ / vay nợ. ``category_of(norm)`` trả slug nhóm chi nêu trong câu (đã bỏ dấu) hoặc None."""
    nfc = unicodedata.normalize("NFC", text)
    norm = normalize(text)
    return _loan(nfc, norm) or _budget(norm, category_of) or _recurring(nfc, norm, now)
