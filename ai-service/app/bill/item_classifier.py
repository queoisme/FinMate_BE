"""Phân loại từng món trên hóa đơn (mục 5 của thiết kế tách bill theo món).

Thứ tự cho mỗi món: thừa kế món chính → luật/từ khóa (DB) → Gemini (chỉ tên, chỉ khi được phép) → nhóm mặc định của cửa hàng → unknown.
Gemini đứng TRƯỚC nhóm mặc định: cửa hàng tiện lợi bán cả đồ ăn lẫn khăn ướt, nên "mặc định là ăn uống" đoán sai một cách tự tin.
Chỉ điều phối, không chạm DB: route nạp sẵn ``instruction``. Không log tên món, cửa hàng hay kết quả của mô hình.
"""
from __future__ import annotations

import re
from collections.abc import Sequence
from difflib import SequenceMatcher

from app.bill.classifier import _PRODUCT_KEYWORDS, _category_from_products, _norm, classify_category_detailed
from app.bill.gemini import MAX_NAMES, GeminiClient, GeminiFailed
from app.bill.knowledge import BillKnowledge, merchant_key
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.core.enums import CATEGORY_SLUGS
from app.schemas.bill import ClassifyItemIn, ClassifyItemOut

# Số tiền kiểu 45,000 / 1.250.000 (kèm đ/d/vnd dính trước hoặc sau): ô tên đôi khi dính giá, không được gửi giá ra ngoài.
_MONEY_RE = re.compile(r"(?<!\w)(?:(?:vnd|đ|d)\s?)?\d{1,3}(?:[.,]\d{3})+(?:\s?(?:vnd|đ|d))?(?!\w)", re.IGNORECASE)


def _strip_money(name: str) -> str:
    return " ".join(_MONEY_RE.sub(" ", name).split())


_REMEMBERABLE = frozenset(CATEGORY_SLUGS) - {"income"}  # đúng tập mà CHECK của bill_item_cache chấp nhận
_MIN_KEY, _MAX_KEY = 3, 60


def item_key(name: str) -> str:
    """Khóa của bảng nhớ tên món: bỏ số tiền, bỏ dấu, chữ thường, gọn khoảng trắng."""
    return merchant_key(_strip_money(name))


def _rememberable(name: str, key: str) -> bool:
    """Chỉ nhớ tên giống tên hàng thật: không phải mã (SP002993), không quá ngắn/dài, không chứa ký tự điều khiển."""
    letters = sum(c.isalpha() for c in key)
    return _MIN_KEY <= len(key) <= _MAX_KEY and letters >= 3 and letters >= sum(c.isdigit() for c in key) and name.isprintable()


_FUZZY_MIN_KEYWORD = 7  # từ khóa ngắn hơn thì một chữ sai đã là một từ khác ("sua"/"sau")
_FUZZY_CUTOFF = 0.85  # một chữ sai trong từ khóa 8 ký tự = 0,875
# Cặp chữ OCR hay nhầm. Chỉ nhận lệch đúng một chữ nằm trong bảng này, hoặc thiếu/thừa một chữ: ngưỡng điểm một mình không phân biệt được
# "kham uot" (lỗi OCR) với "nuoc muoi" ≠ "nuoc suoi" (hai món khác nhau thật).
_OCR_PAIRS = {frozenset(p) for p in ("mn", "uv", "il", "l1", "i1", "o0", "ce", "ao", "nh", "rn", "s5", "b6", "g9", "z2")}


def _ocr_typo(cand: str, kw: str) -> bool:
    ops = [op for op in SequenceMatcher(None, cand, kw).get_opcodes() if op[0] != "equal"]
    if len(ops) != 1:
        return False
    tag, a0, a1, b0, b1 = ops[0]
    if tag == "replace":
        return a1 - a0 == 1 and b1 - b0 == 1 and frozenset((cand[a0], kw[b0])) in _OCR_PAIRS
    return (a1 - a0) + (b1 - b0) == 1  # thiếu hoặc thừa đúng một chữ


def _fuzzy_rule(name: str) -> str | None:
    """Slug backend khi tên món gần như trùng một từ khóa sản phẩm (OCR sai/thiếu một chữ), None nếu không có hoặc không chắc.

    Chỉ gọi khi luật khớp chính xác không ra. So từng cụm từ liên tiếp trong tên với từ khóa có cùng số từ; chữ cái đầu phải trùng.
    Hai nhóm cùng gần như nhau thì không đoán.
    """
    words = " ".join(_norm(name).replace("'", "").split()).split()
    best: tuple[float, int, str] | None = None
    tie = False
    for category, keywords in _PRODUCT_KEYWORDS.items():
        for kw in keywords:
            if len(kw) < _FUZZY_MIN_KEYWORD:
                continue
            n = len(kw.split())
            for start in range(len(words) - n + 1):
                cand = " ".join(words[start : start + n])
                if cand[0] != kw[0] or cand == kw:
                    continue
                ratio = SequenceMatcher(None, cand, kw).ratio()
                if ratio < _FUZZY_CUTOFF or not _ocr_typo(cand, kw):
                    continue
                if best is None or (ratio, len(kw)) > best[:2]:
                    best, tie = (ratio, len(kw), category), False
                elif (ratio, len(kw)) == best[:2] and category != best[2]:
                    tie = True
    return None if best is None or tie else SLUG_BY_LOCAL_CATEGORY.get(best[2])


def _merchant_default(merchant: str | None) -> str | None:
    """Nhóm mặc định của THƯƠNG HIỆU (bảng bill_brands), đã đổi sang slug BE.

    Không dùng từ khóa chung trong tên cửa hàng ("mart", "nhà thuốc", "shop"): khớp nhầm ("Smartphone Center" → food) sẽ gắn nhóm
    nguồn ``merchant`` cho mọi món lạ và chặn luôn Gemini. Với lines=[] nguồn "brand" chỉ có thể đến từ tên cửa hàng khớp bảng thương hiệu.
    """
    if not merchant:
        return None
    category, source, _ = classify_category_detailed(merchant, [])
    return SLUG_BY_LOCAL_CATEGORY.get(category) if source == "brand" else None


async def classify_items(
    items: Sequence[ClassifyItemIn],
    merchant: str | None,
    knowledge: BillKnowledge,
    gemini: GeminiClient,
    instruction: str,
    allow_ai: bool,
) -> tuple[list[ClassifyItemOut], bool]:
    await knowledge.refresh_if_stale()  # nạp bảng thương hiệu/từ khóa từ DB vào classifier (TTL 60 giây)
    results: list[tuple[str | None, str] | None] = [None] * len(items)  # (slug, nguồn); None = chưa quyết

    # Bước 2: luật/từ khóa theo đúng tên món.
    for i, item in enumerate(items):
        if item.parent is None:
            hit = _category_from_products([item.name], None)
            slug = SLUG_BY_LOCAL_CATEGORY.get(hit[0]) if hit else None
            if slug:
                results[i] = (slug, "rule")

    # Bước 2a: gần như trùng một từ khóa (OCR sai một chữ). Vẫn là luật nên cũng không tốn lượt gọi Gemini.
    for i, item in enumerate(items):
        if item.parent is None and results[i] is None:
            slug = _fuzzy_rule(item.name)
            if slug:
                results[i] = (slug, "rule")

    default = _merchant_default(merchant)

    # Bước 2b: tên món đã từng được Gemini trả lời (bảng nhớ dùng chung, không gắn người dùng). Đọc bảng không gửi gì ra ngoài nên
    # chạy kể cả khi người dùng chưa cho phép AI.
    clean = {i: _strip_money(item.name) for i, item in enumerate(items)}
    keys = {i: item_key(item.name) for i, item in enumerate(items)}
    open_ = [i for i, item in enumerate(items) if item.parent is None and results[i] is None and keys[i]]
    if open_:
        cached = await knowledge.cached_items(sorted({keys[i] for i in open_}))
        for i in open_:
            if cached.get(keys[i]) in _REMEMBERABLE:
                results[i] = (cached[keys[i]], "cache")

    # Bước 3: Gemini cho tối đa 20 món còn lại đầu tiên, chỉ tên (đã bỏ số tiền; tên rỗng sau khi bỏ thì không gửi).
    # Hóa đơn thuốc không bao giờ gửi: bỏ qua Gemini nếu cả hóa đơn (cửa hàng + mọi tên món) xếp vào healthcare,
    # nếu nhóm mặc định của thương hiệu là health, hoặc có bất kỳ món nào đã ra health bằng luật.
    ai_used = False
    bill_category, bill_source, _ = classify_category_detailed(merchant, [item.name for item in items])
    health_bill = (
        (SLUG_BY_LOCAL_CATEGORY.get(bill_category) == "health" and bill_source != "none")
        or default == "health"
        or any(r is not None and r[0] == "health" for r in results)
    )
    pending = [i for i, item in enumerate(items) if item.parent is None and results[i] is None and clean[i]][:MAX_NAMES]
    if pending and allow_ai and not health_bill and instruction.strip() and gemini.available:
        try:  # GeminiClient tự giới hạn tổng thời gian (8 giây) và khóa mạch khi quá hạn
            answers = await gemini.classify([clean[i] for i in pending], instruction)
        except GeminiFailed:
            answers = None  # món lạ rơi xuống nhóm mặc định / unknown; không bao giờ ném lỗi ra cho client
        if answers is not None:
            ai_used = True
            learned: dict[str, str] = {}
            for i, category in zip(pending, answers):
                if category != "unknown":
                    results[i] = (category, "ai")
                    if category in _REMEMBERABLE and _rememberable(items[i].name, keys[i]):
                        learned[keys[i]] = category
            if learned:
                await knowledge.remember_items(learned)

    # Bước 4: nhóm mặc định của cửa hàng, chỉ cho món chưa khớp luật và Gemini không quyết được.
    if default:
        for i, item in enumerate(items):
            if item.parent is None and results[i] is None:
                results[i] = (default, "merchant")

    # Bước 1 (làm sau cùng vì cần kết quả CUỐI của món chính) và bước 5.
    out: list[ClassifyItemOut] = []
    for i, item in enumerate(items):
        if item.parent is not None:
            parent = results[item.parent]  # parent < i (schema đã kiểm) nên đã quyết xong
            results[i] = (parent[0], "inherit") if parent else None
        slug, source = results[i] or (None, "unknown")
        out.append(ClassifyItemOut(index=i, category_slug=slug, source=source))
    return out, ai_used
