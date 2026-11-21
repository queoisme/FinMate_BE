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
    global _P, _SELF, _STOP_ACC, _STOP_FOLD, _CUT_ACC, _CUT_FOLD, _FILL_ACC, _FILL_FOLD
    _P = {name: pattern for name, pattern in cues.patterns.items() if name.startswith("item_")}
    _SELF = frozenset(fold(word) for word in cues.lists["item_self_words"])
    _STOP_ACC, _STOP_FOLD = _word_sets(cues.lists["item_name_stopwords"])
    _CUT_ACC, _CUT_FOLD = _word_sets(cues.lists["item_name_cutoff"])
    _FILL_ACC, _FILL_FOLD = _word_sets(cues.lists["item_repay_fillers"])


def _word_sets(words: tuple[str, ...]) -> tuple[frozenset[str], frozenset[str]]:
    return frozenset(unicodedata.normalize("NFC", word.lower()) for word in words), frozenset(fold(word) for word in words)


_bind(intent_cues.current())
intent_cues.subscribe(_bind)


@dataclass(frozen=True)
class ItemDraft:
    kind: str  # budget | recurring | loan
    amount: int  # đồng VND (hạn mức / số tiền mỗi lần / số tiền vay)
    name: str | None = None  # recurring: tên khoản; loan: tiêu đề ("Cho Bạn An vay"); budget: tên nhóm người dùng NÓI (có thể không phải nhóm mặc định nào)
    category: str | None = None  # budget: slug nhóm mặc định hệ thống nhận ra (None nếu không nhận ra)
    cadence: str | None = None  # recurring: weekly | monthly | yearly
    next_due: date | None = None  # recurring: ngày thanh toán tiếp theo
    direction: str | None = None  # loan: borrow | lend
    counterparty: str | None = None  # loan: tên người
    operation: str = "create"  # create | repay (gợi ý ghi lần trả vào khoản vay có sẵn, do backend xác nhận) | update (đổi số tiền việc vừa làm) | add (cộng thêm vào số tiền việc vừa làm) | delete (hủy việc vừa làm); update/add/delete mang theo định danh của việc đó lấy từ ngữ cảnh


@dataclass(frozen=True)
class ItemContext:
    """Việc ngân sách / khoản định kỳ / vay nợ VỪA làm trong phiên này, do app gửi kèm mỗi tin (server không giữ trạng thái). Chỉ có số liệu có cấu trúc, không có chữ người dùng gõ."""

    kind: str
    amount: int
    name: str | None = None
    category: str | None = None
    cadence: str | None = None
    direction: str | None = None
    counterparty: str | None = None


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


def parse_repay(text: str) -> ItemDraft | None:
    """Câu TRẢ NỢ ("bạn An đã trả cho tôi 500k", "tôi trả An 300k"): gợi ý ghi một lần trả vào khoản vay đang có với người đó. KHÔNG thay việc ghi giao dịch thu/chi: server vẫn đọc câu như
    mọi câu khác và backend mới quyết định (tìm khoản vay khớp tên của người dùng; không có thì giữ giao dịch như cũ), nên tên người không rời khỏi backend. None nếu thiếu tên người hoặc số tiền."""
    nfc = unicodedata.normalize("NFC", text)
    norm = normalize(text)
    to_me, by_me = _P["item_repay_to_me"].search(norm), _P["item_repay_by_me"].search(norm)
    if to_me:
        # "bạn An đã trả cho tôi": bỏ chữ đệm ngay trước "trả" ("đã", "vừa"...) rồi mới lấy tên người
        end = to_me.start()
        while (last := re.search(r"(\S+)\s*$", nfc[:end])) and _in(last.group(1), _FILL_ACC, _FILL_FOLD):
            end = last.start(1)
        direction, words, span = "lend", _words(nfc, 0, end, reverse=True), (to_me.start(), to_me.end())
    elif by_me:
        direction, words, span = "borrow", _words(nfc, by_me.end(), len(nfc), reverse=False), (by_me.start(), by_me.end())
    else:
        return None
    words = [w for w in words if not _in(w, _FILL_ACC, _FILL_FOLD)]
    who = _display(words)
    if who is None:
        return None
    spans = [span]
    if found := _find(nfc, who):
        spans.append(found)
    match = parse_amount(_mask(norm, spans))
    if match is None or match.assumed:
        return None
    return ItemDraft(LOAN, match.value, direction=direction, counterparty=who, operation="repay")


def _budget(nfc: str, norm: str, category_of: Callable[[str], str | None]) -> ItemResult | None:
    strong, weak = _P["item_budget_strong"].search(norm), _P["item_budget_weak"].search(norm)
    if not strong and not weak:
        return None
    replies = _replies()
    masked = _mask(norm, _span(strong) or _span(weak))
    match = parse_amount(masked)
    category = category_of(masked)
    if not strong and (match is None or category is None):
        return None  # "ngân sách còn bao nhiêu", "hạn mức thẻ": chưa phải câu đặt ngân sách
    # Tên nhóm người dùng nói ("thú cưng"): hệ thống chỉ biết các nhóm mặc định, còn nhóm của từng tài khoản khác nhau nên app đối chiếu với danh mục CỦA NGƯỜI DÙNG (và đề nghị tạo nếu chưa có).
    said = _leftover(nfc, _span(strong) + ([(match.start, match.end)] if match else [])) if strong else None
    if category is None and said is None:
        return ItemResult(None, replies["item_budget_ask_category"])
    if match is None:
        return ItemResult(None, replies["item_budget_ask_amount"])
    if match.assumed:
        return ItemResult(None, replies["item_needs_unit"])
    return ItemResult(ItemDraft(BUDGET, match.value, name=said, category=category), None)


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


def _leftover(nfc: str, removed: list[tuple[int, int]]) -> str | None:
    """Các chữ còn lại sau khi bỏ từ lệnh, số tiền, ngày và chữ đệm (item_name_stopwords): tên khoản / tên nhóm người dùng nói; không còn gì thì None."""
    kept = []
    for word in re.finditer(r"\S+", nfc):
        if any(start < word.end() and word.start() < end for start, end in removed):
            continue
        token = word.group(0).strip(".,;:!?\"'")
        if token and not _in(token, _STOP_ACC, _STOP_FOLD) and not re.fullmatch(r"[\d.,]+", token):
            kept.append(token)
    name = " ".join(kept)
    return name[:1].upper() + name[1:80] if len(name) >= 2 else None


def _recurring_name(nfc: str, removed: list[tuple[int, int]]) -> str:
    return _leftover(nfc, removed) or _replies()["item_recurring_default_name"]


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


def _target(last: ItemContext, operation: str, amount: int) -> ItemDraft:
    """Bản nháp thao tác lên việc vừa làm: giữ định danh (nhóm / tên / người) để app biết sửa hoặc xóa đúng cái nào."""
    return ItemDraft(
        last.kind, amount, name=last.name, category=last.category, cadence=last.cadence, direction=last.direction, counterparty=last.counterparty, operation=operation
    )


def _followup(nfc: str, norm: str, now: datetime, category_of: Callable[[str], str | None], last: ItemContext) -> ItemResult | None:
    """Câu nối tiếp việc VỪA làm: xóa ("xóa cái đó"), đổi số tiền ("đổi thành 3 triệu"), cộng thêm ("thêm 300k nữa"), hoặc làm tiếp việc cùng loại ("còn di chuyển 1 triệu").
    Câu còn từ lạ ngoài lệnh và số tiền ("tăng lương lên 20 triệu") không phải nối tiếp: để các luật khác xử lý."""
    modify, add, also = _P["item_modify"].search(norm), _P["item_add"].search(norm), _P["item_also"].search(norm)
    delete, referent = _P["item_delete_verb"].search(norm), _P["item_referent"].search(norm)
    command = [m for m in (modify, add, also, delete, referent) if m]
    if not command:
        return None
    removed = [(m.start(), m.end()) for m in command]
    match = parse_amount(_mask(norm, removed))
    if delete and referent and match is None:
        return ItemResult(_target(last, "delete", 0), None)
    if match is None or match.assumed:
        return None
    spans = removed + [(match.start, match.end)]
    rest = _leftover(nfc, spans)
    if rest is None and last.kind in (BUDGET, LOAN):
        if modify:
            return ItemResult(_target(last, "update", match.value), None)
        if add and not also:
            return ItemResult(_target(last, "add", match.value), None)
    if also and rest is not None and last.kind == BUDGET:
        return ItemResult(ItemDraft(BUDGET, match.value, name=rest, category=category_of(_mask(norm, spans))), None)
    if also and rest is not None and last.kind == RECURRING and last.cadence:
        due, _, _ = _next_due(norm, now)
        if due is None:
            return ItemResult(None, _replies()["item_recurring_ask_due"])
        return ItemResult(ItemDraft(RECURRING, match.value, name=rest, cadence=last.cadence, next_due=due), None)
    return None


def parse_item(text: str, now: datetime, category_of: Callable[[str], str | None], last: ItemContext | None = None) -> ItemResult | None:
    """None nếu câu không phải tạo ngân sách / khoản định kỳ / vay nợ (hoặc nối tiếp việc vừa làm khi có ``last``).
    ``category_of(norm)`` trả slug nhóm chi nêu trong câu (đã bỏ dấu) hoặc None."""
    nfc = unicodedata.normalize("NFC", text)
    norm = normalize(text)
    if last is not None and (followed := _followup(nfc, norm, now, category_of, last)) is not None:
        return followed
    return _loan(nfc, norm) or _budget(nfc, norm, category_of) or _recurring(nfc, norm, now)
