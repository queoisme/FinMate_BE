"""Hiểu câu HỎI TRƯỚC KHI MUA: "Hôm nay mua cái quần 800.000đ được không?", "đợi tới ngày 16 mua laptop 20 triệu có ổn không".

Chỉ ĐỌC câu và trả (tên món, giá, ngày định mua, danh mục); backend tính kết luận ở ``POST /forecast/purchase-check`` bằng số liệu của nó. AI ở đây không bao giờ
tính số dư hay đưa kết luận, và không ghi giao dịch nào.

Kích hoạt hẹp có chủ ý: cần ĐỦ CẢ động từ mua LẪN cách hỏi khả năng chi ("được không", "ổn không", "đủ tiền"...). "mua cà phê 45k" vẫn là khoản chi;
"iphone 20 triệu có đáng mua không" (hỏi giá trị món đồ) và "nên mua nhà hay thuê nhà" (xin lời khuyên, không giá) vẫn ngoài phạm vi.

Ngày định mua (giờ Việt Nam) chỉ từ hôm nay trở đi, khác ``dates.parse_when`` vốn từ chối ngày tương lai:
  "hôm nay", "ngày mai", "ngày kia"; "ngày 16" = 16 tháng này, hoặc tháng sau nếu đã qua 16; "ngày 16/11", "ngày 16 tháng 11" (đã qua thì bị từ chối).
Không nói ngày thì ``purchase_date`` là None (backend hiểu là hôm nay).

``normalize()`` (bỏ dấu, chữ thường) giữ nguyên độ dài so với câu gốc đã NFC, nên vị trí khớp trên chữ bỏ dấu dùng lại được trên câu gốc.
Mọi từ khóa, mẫu và câu trả lời nằm ở app/data/intent_cues.json (purchase_*), không ở đây.
"""

from __future__ import annotations

import calendar
import re
import unicodedata
from dataclasses import dataclass
from datetime import date, datetime, timedelta

from app.services import intent_cues
from app.services.amount_normalizer import normalize, parse_amount
from app.services.category_resolver import resolve
from app.services.dates import VN
from app.services.vi_text import fold

_MAX_ITEM = 60  # backend giới hạn nhãn món 60 ký tự


def _bind(cues: intent_cues.Cues) -> None:
    """Gắn từ mua, cách hỏi, ngày và chữ không thuộc tên món (app/data/intent_cues.json, hoặc bản admin kích hoạt trong DB) vào các tên dùng ở module này.
    - ``_ASK_STRONG``: hỏi khả năng chi, đủ để hỏi lại giá nếu thiếu; ``_ASK_WEAK``: xin lời khuyên, chỉ tính khi có giá.
    - ``_STOPWORDS``: chữ không thuộc TÊN món (đã bỏ dấu). Chỉ gọi với bản đã qua intent_cues.build."""
    global _BUY, _ASK_STRONG, _ASK_WEAK, _CHOICE, _WHEN, _PAST, _STOPWORDS
    p = cues.patterns
    _BUY, _ASK_STRONG, _ASK_WEAK, _WHEN, _PAST = p["purchase_buy"], p["purchase_ask_strong"], p["purchase_ask_weak"], p["purchase_when"], p["purchase_past"]
    _CHOICE = p["purchase_choice"]
    _STOPWORDS = frozenset(fold(word) for word in cues.lists["purchase_stopwords"])


_bind(intent_cues.current())
intent_cues.subscribe(_bind)


@dataclass(frozen=True)
class PurchaseDraft:
    item: str | None  # tên món người dùng nói; None nếu câu không nói (backend dùng nhãn mặc định)
    amount_cents: int  # đồng VND (quy ước của repo)
    purchase_date: date | None  # ngày định mua, giờ Việt Nam; None = không nói (hôm nay)
    category_slug: str | None  # slug backend đoán từ tên món; None = chưa rõ


@dataclass(frozen=True)
class PurchaseResult:
    """``draft`` có thì app hiện thẻ xác nhận rồi gọi backend; không thì ``reply`` là câu hỏi lại, hoặc ``needs_amount`` (câu thiếu giá: dùng câu hỏi số tiền có sẵn)."""

    draft: PurchaseDraft | None
    reply: str | None = None
    needs_amount: bool = False


def _safe_date(year: int, month: int, day: int) -> date | None:
    try:
        return date(year, month, day)
    except ValueError:  # 31/2, ngày 0...
        return None


def _next_month(day: date) -> date:
    return (day.replace(day=1) + timedelta(days=calendar.monthrange(day.year, day.month)[1])).replace(day=1)


def _purchase_day(found: re.Match, today: date) -> date | None:
    """Ngày định mua, hoặc None nếu đã qua hay không có thật (31/2)."""
    if found["today"]:
        return today
    if found["tomorrow"]:
        return today + timedelta(days=1)
    if found["after"]:
        return today + timedelta(days=2)
    day = int(found["day"])
    if found["month"]:
        target = _safe_date(today.year, int(found["month"]), day)
    else:
        target = _safe_date(today.year, today.month, day)
        if target and target < today:  # "ngày 5" nói vào ngày 17: là mùng 5 tháng sau
            following = _next_month(today)
            target = _safe_date(following.year, following.month, day)
    return target if target and target >= today else None


def _name_from(nfc: str, norm: str, removed: list[tuple[int, int]]) -> str | None:
    """Tên món = các chữ còn lại sau khi bỏ động từ mua, cách hỏi, giá, ngày và chữ đệm."""
    kept = []
    for word in re.finditer(r"\S+", nfc):
        if any(start < word.end() and word.start() < end for start, end in removed):
            continue
        plain = norm[word.start() : word.end()].strip(".,;:!?")
        if plain and plain not in _STOPWORDS:
            kept.append(word.group(0).strip(".,;:!?"))
    name = " ".join(kept)
    return name[:_MAX_ITEM].strip() if len(name) >= 2 else None


def parse_purchase(text: str, now: datetime) -> PurchaseResult | None:
    """None nếu câu không phải hỏi trước khi mua; còn lại là bản nháp hoặc câu hỏi lại (thiếu/mơ hồ giá, ngày không hiểu, khoản đã mua)."""
    nfc = unicodedata.normalize("NFC", text).strip()
    norm = normalize(nfc)
    buys = list(_BUY.finditer(norm))
    strong, weak = list(_ASK_STRONG.finditer(norm)), list(_ASK_WEAK.finditer(nfc))  # lời khuyên khớp trên câu CÓ dấu (xem purchase_ask_weak)
    if not buys or not (strong or weak):
        return None
    if not strong and _CHOICE.search(nfc):
        return None  # 'nên mua laptop nào dưới 20 triệu': hỏi chọn món, số tiền là hạn mức tìm kiếm chứ không phải giá một khoản định mua

    replies = intent_cues.current().replies  # đọc lúc gọi: bản admin vừa kích hoạt có hiệu lực ngay
    if _PAST.search(norm):
        return PurchaseResult(None, replies["purchase_past"])

    when = _WHEN.search(norm)
    removed = [(m.start(), m.end()) for m in (*buys, *strong, *weak)]
    if when:
        removed.append((when.start(), when.end()))
    # Giá đọc trên câu đã che cụm ngày và cách hỏi: "ngày 16" chứa số 16 nhưng không phải tiền.
    masked = "".join(" " if any(start <= i < end for start, end in removed) else c for i, c in enumerate(norm))
    match = parse_amount(masked)
    if match is None:
        return PurchaseResult(None, needs_amount=True) if strong else None  # 'nên mua nhà hay thuê nhà' không giá: xin lời khuyên, ngoài phạm vi
    if match.assumed:
        return PurchaseResult(None, replies["purchase_needs_unit"])

    purchase_date = None
    if when:
        purchase_date = _purchase_day(when, now.astimezone(VN).date())
        if purchase_date is None:
            return PurchaseResult(None, replies["purchase_bad_date"])

    removed.append((match.start, match.end))
    item = _name_from(nfc, norm, removed)
    category = resolve(item, chat=True).slug if item else None
    return PurchaseResult(PurchaseDraft(item, match.value, purchase_date, category))
