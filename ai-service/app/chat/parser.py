"""Hiểu một câu chat: "đi ăn 200k", "nhận lương 14 củ 5", "chuyển 2 triệu sang tiết kiệm", "tháng này tôi tiêu bao nhiêu?".

Chỉ ĐỌC câu và trả quyết định, không lưu gì và không giữ trạng thái giữa các câu: backend lưu giao dịch (bản nháp, xác nhận,
hoàn tác, sửa danh mục đều là việc của backend). Số liệu tổng hợp cũng do backend tính từ giao dịch của nó; ở đây chỉ hiểu người dùng
hỏi gì (loại, kỳ, danh mục).

Ý định lấy từ Rasa khi Rasa có mặt và đủ chắc, không thì từ luật. Hai bên bất đồng giữa chi tiêu/thu nhập/chuyển tiền thì tin dấu hiệu trong câu,
trừ khi Rasa rất chắc. Nhóm danh mục ở đây là 9 nhóm của bộ luật bill/chat; quy đổi sang slug backend làm ở route (app/core/category_map.py).
"""
from __future__ import annotations

import re
import unicodedata
from collections.abc import Callable
from dataclasses import dataclass, replace
from datetime import datetime, timedelta

from app.chat import vocab
from app.chat.amount import normalize, parse_amount, remove_amount
from app.chat.clauses import apply_cancellations, extract_total, split_clauses
from app.chat.dates import VN, parse_when, remove_when
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.core.category_resolver import resolve

MIN_NLU_CONFIDENCE = 0.6  # Rasa chắc dưới mức này thì bỏ qua, dùng luật
OUT_OF_SCOPE_OVERRIDE = 0.75  # câu có số tiền mà Rasa bảo "ngoài chủ đề" chỉ bị từ chối khi Rasa chắc từ mức này
KIND_TRUST = 0.97  # Rasa và luật bất đồng loại giao dịch: Rasa chỉ thắng khi chắc từ mức này
UNCERTAIN_BELOW = 0.85  # Rasa hiểu nhưng chắc dưới mức này: tin nhắn đáng để xem lại
AUTO_CONFIDENCE = 0.9  # Rasa chắc từ mức này (và là add_expense) mới cho ghi ngay

CATEGORIES = tuple(vocab.CATEGORY_LABELS)
INCOME_TYPES = tuple(vocab.INCOME_LABELS)

_GREET = {"chao", "xin chao", "hello", "hi", "alo", "hey", "chao mam"}
_THANKS = {"cam on", "cam on nhe", "thanks", "thank you"}
_PERIODS = (("hom nay", "hôm nay"), ("hom qua", "hôm qua"), ("tuan nay", "tuần này"), ("tuan truoc", "tuần trước"),
            ("thang truoc", "tháng trước"), ("thang nay", "tháng này"))

# Dấu hiệu loại giao dịch. Các từ dễ nhầm khi bỏ dấu ("thưởng" / "thường") được khớp trên câu CÓ dấu.
_INCOME_ACCENTED = re.compile(r"nhận (?:lương|tiền|thưởng|được)|được (?:cho|tặng|thưởng|hoàn|lì xì)|(?:mẹ|bố|ba|cô|chú|dì|bác|anh|em|sếp|khách) (?:cho|gửi|tặng|trả|biếu)|lương|thưởng|lì xì|hoàn tiền|tiền hoàn|thu nhập|tiền công|làm thêm|khách trả|bán (?:được|đồ|xe|điện thoại|hàng)")
_INCOME_PLAIN = re.compile(r"(?<![a-z])(?:nhan luong|nhan thuong|nhan tien|duoc cho|duoc tang|duoc hoan|li xi|hoan tien|tien hoan|thu nhap|tien cong|lam them|khach tra|ban duoc|ban do|ban xe|freelance)(?![a-z])")
_RECEIVED = re.compile(r"nhan (?:luong|tien|thuong)|duoc (?:cho|tang)")  # chắc chắn là tiền vào dù câu có chữ "chuyển"
_NOT_INCOME = re.compile(r"(?<![a-z])(?:tra|phat|ung) (?:luong|cong|thuong)(?![a-z])")  # "trả lương nhân viên" là tiền ra
_TRANSFER_CUE = re.compile(r"(?<![a-z])(?:chuyen khoan|chuyen tien|chuyen|rut|nap(?: tien)?(?: \S+){0,3} vao|gui tiet kiem|tra no|dao no)(?![a-z])")  # "nạp 300k vào momo": số tiền nằm giữa
_OWN_ACCOUNT = re.compile(r"(?<![a-z])(?:tiet kiem|vi|momo|zalopay|vnpay|ngan hang|tai khoan|the|atm|tien mat)(?![a-z])")
_TO_OTHERS = re.compile(r"(?<![a-z])(?:cho|tra no|dao no|bieu|tang)(?![a-z])")
_BALANCE_Q = re.compile(r"con lai|tiet kiem duoc|thu chi|chenh lech|du bao nhieu|so du")
_INCOME_Q = re.compile(r"thu nhap|nhan duoc|kiem duoc|thu duoc|luong")
_SPEND_Q = re.compile(r"tieu|chi tieu|da chi|tong chi")
_QUERY = re.compile(r"bao nhieu|tong chi|tong tieu|thong ke|da chi|da tieu|tieu het|chi het|con lai|thu chi|thu nhap")

# Số tiền còn sót trong mô tả (kể cả dính dấu phẩy: "20k,30k"). Không tính "lít" vì "mua 2 lít sữa 50k" là số lượng.
_STRAY_AMOUNT = re.compile(r"\d+(?:[.,]\d+)?\s*(?:k|nghin|ngan|canh|trieu|tr|cu)(?![a-z])")
_EXPENSE_VERB = re.compile(r"(?:an|uong|mua|di an|di uong|do xang|dong|tra|nap|thue)(?![a-z])")

_REPLIES = {
    "greet": "Chào bạn! Mình là Mầm, bạn cứ nhắn khoản chi tiêu, ví dụ \"ăn phở 45k\", hoặc hỏi \"tháng này tôi tiêu bao nhiêu?\".",
    "goodbye": "Tạm biệt bạn! Cần ghi chi tiêu thì nhắn mình nhé.",
    "thanks": "Không có gì! Cần ghi thêm khoản nào cứ nhắn mình nhé.",
    "empty": "Bạn nhắn khoản chi tiêu nhé, ví dụ: ăn phở 45k.",
    "needs_amount": "Mình chưa thấy số tiền. Bạn nhắn kèm số tiền nhé, ví dụ \"ăn phở 45k\".",
    "cancelled": "Mình đã bỏ khoản đó, không ghi gì. Cần ghi khoản khác cứ nhắn mình nhé.",
    "out_of_scope": "Mình chỉ giúp ghi chi tiêu, thu nhập và xem tổng thôi. Bạn thử \"ăn phở 45k\" hoặc \"tháng này tôi tiêu bao nhiêu?\" nhé.",
}
_REPLIES["confirm"] = _REPLIES["deny"] = _REPLIES["cancel"] = "Hiện chưa có khoản nào đang chờ xác nhận. Bạn nhắn khoản mới nhé, ví dụ \"ăn phở 45k\"."

_KIND_BY_INTENT = {"add_expense": "expense", "add_income": "income", "transfer_money": "transfer"}
_ENTRY_INTENTS = tuple(_KIND_BY_INTENT)


@dataclass(frozen=True)
class Entry:
    kind: str  # expense | income | transfer
    amount: int  # đồng VND
    description: str
    merchant: str | None
    category: str | None  # slug backend (cùng bộ phân loại với món trên bill); None khi chưa biết; thu nhập/chuyển tiền không có
    income_type: str | None  # salary | bonus | gift | side_income | refund | other_income (khi kind = income và đoán được)
    auto_save_ok: bool  # đủ chắc để ghi ngay không cần hỏi (chỉ với chi tiêu)
    to_others: bool  # "chuyển cho người khác / trả nợ": tiền rời khỏi người dùng nên tính là chi tiêu
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
    month = today.replace(day=1)
    if key == "thang truoc":
        return (month - timedelta(days=1)).replace(day=1), month
    return month, (month + timedelta(days=32)).replace(day=1)  # "thang nay" (mặc định)


def _label_index(labels: dict[str, str]) -> dict[str, str]:
    return {re.sub(r"\s+", " ", normalize(v)): k for k, v in labels.items()}


_SLUG_BY_LABEL = _label_index(vocab.CATEGORY_LABELS)
_INCOME_BY_LABEL = _label_index(vocab.INCOME_LABELS)


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


def expense_category(text: str) -> str | None:
    """Slug backend của khoản chi: tên danh mục gõ thẳng trong câu trước (người dùng nói rõ), rồi bộ phân loại chung với bill. None = chưa rõ.

    Không khớp gần đúng: đó là cho lỗi OCR, còn câu chat bình thường dễ bị nó khớp nhầm.
    """
    label = _category_from_label(normalize(text))
    if label:
        return SLUG_BY_LOCAL_CATEGORY.get(label, label)
    return resolve(text, chat=True).slug


def detect_kind(text: str) -> str:
    """"expense" | "income" | "transfer" theo dấu hiệu trong câu (luật; Rasa có thể ghi đè khi rất chắc)."""
    low = unicodedata.normalize("NFC", text).lower()
    norm = normalize(text)
    income = bool(_INCOME_ACCENTED.search(low) or _INCOME_PLAIN.search(norm)) and not _NOT_INCOME.search(norm)
    transfer = bool(_TRANSFER_CUE.search(norm))
    if income and (not transfer or _RECEIVED.search(norm) or _INCOME_ACCENTED.search(low)):
        return "income"
    return "transfer" if transfer else "expense"


def _merchant_in(description: str) -> str | None:
    m = re.search(r"\b(?:ở|tại|o|tai)\s+(?!nha\b)(.+)$", description, re.I)
    return m.group(1).strip() if m and len(m.group(1).strip()) >= 3 else None


def is_query(norm: str) -> bool:
    return bool(_QUERY.search(norm))


def is_question(text: str, norm: str) -> bool:
    """Câu hỏi, không phải khoản ghi: "iphone 20 triệu có đáng mua không", "nên mua nhà hay thuê nhà"."""
    return text.rstrip().endswith("?") or norm.endswith(" khong") or bool(re.match(r"(?:co nen|nen|lieu|co phai) ", norm))


def _rule_intent(text: str, norm: str) -> str:
    if parse_amount(text):
        return {"expense": "add_expense", "income": "add_income", "transfer": "transfer_money"}[detect_kind(text)]
    if is_query(norm):
        return "ask_spending"
    if norm in _GREET:
        return "greet"
    if norm in _THANKS:
        return "thanks"
    # Cụm chi tiêu không kèm số tiền ("ăn cơm", "mua nước", "đi ăn"): hỏi số tiền thay vì bảo ngoài chủ đề. Câu hỏi đã bị chặn trước đó.
    return "needs_amount" if _EXPENSE_VERB.match(norm) or expense_category(norm) else "out_of_scope"


def _resolve_intent(text: str, norm: str, nlu: dict | None) -> str:
    if is_question(text, norm) and not is_query(norm) and not (nlu and nlu["intent"] in ("greet", "goodbye", "confirm", "deny", "cancel")):
        return "out_of_scope"
    rule = _rule_intent(text, norm)
    if not nlu:
        return rule
    name = nlu["intent"]
    if name == "out_of_scope" and rule in _ENTRY_INTENTS and nlu["confidence"] < OUT_OF_SCOPE_OVERRIDE:
        return rule  # câu có số tiền mà Rasa chỉ hơi nghiêng về ngoài chủ đề: ưu tiên ghi, người dùng còn được xác nhận
    if name in ("add_income", "transfer_money") and rule == "add_expense" and expense_category(norm):
        return rule  # Rasa hay quá tự tin với từ lạ ("cắt kính cận 800k" -> thu nhập 1.0): không có dấu hiệu thu/chuyển mà có từ khóa chi tiêu rõ thì là chi tiêu
    if name in _ENTRY_INTENTS and rule in _ENTRY_INTENTS and name != rule and nlu["confidence"] < KIND_TRUST:
        return rule  # bất đồng loại giao dịch mà Rasa chưa thật chắc: tin dấu hiệu trong câu
    return name


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
    else:  # luật không thấy số tiền nhưng Rasa cắt được một cụm số tiền: đọc cụm đó
        phrase = entities.get("amount")
        found = parse_amount(phrase) if phrase else None
        if not found:
            return None
        value, description = found.value, re.sub(r"\s+", " ", text.replace(phrase, " ")).strip()
    description = re.sub(r"^(?:vừa|mới|nay|hôm nay|hôm qua|tôi|mình|em|đã|hết|tốn|mất|chi|tiêu|khoảng)\s+", "", description, flags=re.I).strip()
    description = re.sub(r"\s+(?:hết|tốn|mất|khoảng|hết khoảng)$", "", description, flags=re.I).strip()  # "ăn bún chả hết 55 cành" -> "ăn bún chả"
    norm_text = normalize(text)
    if kind == "transfer":
        if not _OWN_ACCOUNT.search(norm_text) and _TO_OTHERS.search(norm_text):
            return Entry("expense", value, description, None, "other", None, False, True)  # chuyển cho người khác / trả nợ: tiền rời khỏi mình
        return Entry("transfer", value, description, None, None, None, False, False)
    if kind == "income":
        income_type = category_from_text(normalize(description), "income") or category_from_text(norm_text, "income")
        return Entry("income", value, description, None, None, income_type, False, False)
    merchant = _merchant_in(description) or entities.get("merchant")
    category = expense_category(" ".join(part for part in (merchant, description) if part))
    if category is None and fallback_category is not None:
        category = fallback_category(merchant, description)
    return Entry("expense", value, description, merchant, category, None, _auto_ok("expense", value, category, nlu, auto_below), False)


def _query(norm: str, now: datetime) -> Query:
    key, label = next(((k, v) for k, v in _PERIODS if k in norm), ("thang nay", "tháng này"))
    start, end = period_range(key, now)
    if _BALANCE_Q.search(norm):
        return Query("balance", key, label, start, end, None, None)
    if _INCOME_Q.search(norm) and not _SPEND_Q.search(norm):
        return Query("income", key, label, start, end, None, category_from_text(norm, "income"))
    return Query("expense", key, label, start, end, category_from_text(norm), None)


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
    text = (text or "").strip()
    now = now or datetime.now(VN)
    norm = re.sub(r"\s+", " ", normalize(text)).strip(" .!?,")
    if nlu is not None and nlu["confidence"] < MIN_NLU_CONFIDENCE:
        nlu = None
    source, nlu_intent = ("rasa", nlu["intent"]) if nlu else ("rules", None)
    confidence = round(nlu["confidence"], 3) if nlu else None

    def result(intent: str, **extra) -> ChatParse:
        return ChatParse(intent, extra.get("entry"), extra.get("query"), extra.get("reply_text"), source, nlu_intent, confidence,
                         extra.get("unclear_reason"), extra.get("entries", ()),
                         extra.get("total_amount"), extra.get("total_mismatch", False))

    if not norm:
        return result("empty", reply_text=_REPLIES["empty"])
    intent = _resolve_intent(text, norm, nlu)
    reason = _unclear_reason(text, norm, intent, nlu)
    if intent in _ENTRY_INTENTS:
        text, cancelled = apply_cancellations(text)  # "rồi hủy", "nhầm, là 35k": áp trước khi tách khoản
        if cancelled and not parse_amount(text):
            return result("out_of_scope", reply_text=_REPLIES["cancelled"], unclear_reason="cancelled")
        text, stated_total = extract_total(text)  # "... tổng cộng 60k": số đối chiếu, không phải một khoản
        clauses = split_clauses(text)
        if len(clauses) > 1:  # nhiều khoản: loại từng khoản đọc theo luật (Rasa chỉ hiểu cả câu), và không bao giờ ghi ngay
            whole = parse_when(text, now)  # ngày nói ở một vế ("... cà phê 30k hôm qua") áp cho các vế không có ngày riêng
            many = tuple(
                replace(e, auto_save_ok=False)
                for c in clauses
                if (e := _entry(c, detect_kind(c), None, auto_save_below, fallback_category, now, whole.value if whole else None))
            )
            if len(many) > 1:
                return result(intent, entry=many[0], entries=many, unclear_reason=reason, **_total_check(stated_total, many))
        entry = _entry(text, _KIND_BY_INTENT[intent], nlu, auto_save_below, fallback_category, now)
        if entry is None:
            return result("needs_amount", reply_text=_REPLIES["needs_amount"], unclear_reason=reason)
        return result(intent, entry=entry, entries=(entry,), unclear_reason=reason, **_total_check(stated_total, (entry,)))
    if intent == "ask_spending":
        return result(intent, query=_query(norm, now), unclear_reason=reason)
    return result(intent, reply_text=_REPLIES.get(intent, _REPLIES["out_of_scope"]), unclear_reason=reason)
