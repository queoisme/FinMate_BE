"""Phân loại từng món trên hóa đơn (mục 5 của thiết kế tách bill theo món).

Thứ tự cho mỗi món (món phụ thừa kế món chính). Đã cho phép AI: bảng nhớ → Gemini (chỉ tên) → luật/từ khóa dùng chung với chat (``app.services.category_resolver``) làm dự phòng → nhóm mặc định của cửa hàng → unknown.
Chưa cho phép AI: luật → bảng nhớ → nhóm mặc định → unknown. Không có ngoại lệ theo nhóm hàng: hóa đơn có món sức khỏe cũng đi qua AI như mọi hóa đơn (chủ sản phẩm bỏ quy tắc chặn ngày 2026-10-23).
Gemini đứng TRƯỚC luật và nhóm mặc định: một hóa đơn chứa nhiều nhóm hàng, "mặc định là ăn uống" hay một từ khóa đoán sai một cách tự tin.
Chỉ điều phối, không chạm DB: route nạp sẵn ``instruction``. Không log tên món, cửa hàng hay kết quả của mô hình.
"""
from __future__ import annotations

import re
from collections.abc import Sequence

from app.bill.classifier import brand_backend_slug, classify_category_detailed
from app.bill.gemini import MAX_NAMES, GeminiClient, GeminiFailed
from app.bill.knowledge import BillKnowledge, merchant_key
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.services.category_resolver import resolve
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


def learnable_items(cases: Sequence[dict]) -> dict[str, str]:
    """Từ các nhãn đã được người kiểm ({"name", "expect"}) ra ``khóa -> slug`` để ghi vào bảng nhớ tên món.

    Cùng bộ lọc với câu trả lời của Gemini: không nhớ income, slug lạ, tên giống mã hàng, quá ngắn/dài. Nhãn đứng sau thắng nhãn đứng trước."""
    learned: dict[str, str] = {}
    for case in cases:
        name, slug = str(case.get("name", "")), case.get("expect")
        key = item_key(name)
        if slug in _REMEMBERABLE and key and _rememberable(name, key):
            learned[key] = slug
    return learned


def _merchant_default(merchant: str | None) -> str | None:
    """Nhóm mặc định của THƯƠNG HIỆU (bảng bill_brands), đã đổi sang slug BE.

    Không dùng từ khóa chung trong tên cửa hàng ("mart", "nhà thuốc", "shop"): khớp nhầm ("Smartphone Center" → food) sẽ gắn nhóm
    nguồn ``merchant`` cho mọi món lạ và chặn luôn Gemini. Với lines=[] nguồn "brand" chỉ có thể đến từ tên cửa hàng khớp bảng thương hiệu.
    """
    if not merchant:
        return None
    if slug := brand_backend_slug(merchant):
        return slug
    category, source, _ = classify_category_detailed(merchant, [])
    return SLUG_BY_LOCAL_CATEGORY.get(category) if source == "brand" else None


async def classify_items(
    items: Sequence[ClassifyItemIn],
    merchant: str | None,
    knowledge: BillKnowledge,
    gemini: GeminiClient,
    instruction: str,
    allow_ai: bool,
    stats: dict[str, int] | None = None,
) -> tuple[list[ClassifyItemOut], bool]:
    """``stats`` (tuỳ chọn): được điền số đếm để đo mức dùng Gemini (``ai_candidates``, ``ai_sent``, ``ai_failed``)."""
    await knowledge.refresh_if_stale()  # nạp bảng thương hiệu/từ khóa từ DB vào classifier (TTL 60 giây)

    # Luật/từ khóa theo đúng tên món, dùng chung với chat (kể cả khớp gần đúng cho lỗi OCR một chữ).
    rules: list[str | None] = [None] * len(items)
    for i, item in enumerate(items):
        if item.parent is None:
            rules[i] = resolve(item.name).slug

    default = _merchant_default(merchant)
    clean = {i: _strip_money(item.name) for i, item in enumerate(items)}
    keys = {i: item_key(item.name) for i, item in enumerate(items)}
    top = [i for i, item in enumerate(items) if item.parent is None]

    # Một hóa đơn thường có nhiều nhóm hàng (cửa hàng tiện lợi bán cả đồ ăn, pin, khăn ướt) nên luật từ khóa không đủ: khi người dùng cho phép AI,
    # AI quyết trước, luật chỉ là dự phòng lúc AI lỗi hoặc trả "không rõ". Chưa cho phép thì luật quyết như cũ.
    ai_first = allow_ai
    results: list[tuple[str | None, str] | None] = [None] * len(items)  # (slug, nguồn); None = chưa quyết
    if not ai_first:
        for i in top:
            if rules[i]:
                results[i] = (rules[i], "rule")

    # Tên món đã từng được Gemini trả lời (bảng nhớ dùng chung, không gắn người dùng). Đọc bảng không gửi gì ra ngoài nên chạy cả khi chưa cho phép AI.
    open_ = [i for i in top if results[i] is None and keys[i]]
    if open_:
        cached = await knowledge.cached_items(sorted({keys[i] for i in open_}))
        for i in open_:
            if cached.get(keys[i]) in _REMEMBERABLE:
                results[i] = (cached[keys[i]], "cache")

    # Gemini cho tối đa 20 món chưa quyết đầu tiên, chỉ tên (đã bỏ số tiền; tên rỗng sau khi bỏ thì không gửi).
    ai_used = False
    ai_failed = False  # đã cho phép AI mà AI không trả lời được: món lạ để "chưa rõ", không rơi xuống nhóm mặc định của cửa hàng
    candidates = [i for i in top if results[i] is None and clean[i]] if ai_first else []
    pending = candidates[:MAX_NAMES]
    if pending and instruction.strip():
        answers = None
        if gemini.available:
            try:  # GeminiClient tự giới hạn tổng thời gian và khóa mạch khi mọi model đều lỗi
                answers = await gemini.classify([clean[i] for i in pending], instruction)
            except GeminiFailed:
                answers = None  # không bao giờ ném lỗi ra cho client
        ai_failed = answers is None
        if stats is not None:
            stats.update({"ai_candidates": len(candidates), "ai_sent": len(pending), **({"ai_failed": 1} if ai_failed else {})})
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

    # Luật làm dự phòng cho món AI không quyết (lỗi, "không rõ", hoặc nằm ngoài 20 món đầu).
    for i in top:
        if results[i] is None and rules[i]:
            results[i] = (rules[i], "rule")

    # Nhóm mặc định của cửa hàng, chỉ cho món chưa khớp gì. KHÔNG áp khi AI được phép mà thất bại: cửa hàng tiện lợi bán pin, khăn ướt,
    # bút lẫn đồ ăn, gán "Ăn uống" cho cả bill là xếp sai một cách im lặng; để "chưa rõ" thì người dùng chọn.
    if default and not ai_failed:
        for i in top:
            if results[i] is None:
                results[i] = (default, "merchant")

    # Món phụ thừa kế nhóm của món chính (làm sau cùng vì cần kết quả CUỐI của món chính); còn lại là unknown.
    out: list[ClassifyItemOut] = []
    for i, item in enumerate(items):
        if item.parent is not None:
            parent = results[item.parent]  # parent < i (schema đã kiểm) nên đã quyết xong
            results[i] = (parent[0], "inherit") if parent else None
        slug, source = results[i] or (None, "unknown")
        out.append(ClassifyItemOut(index=i, category_slug=slug, source=source))
    return out, ai_used
