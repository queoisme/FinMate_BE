"""Bộ phân loại chung cho chat và món trên hóa đơn: một cụm từ ra MỘT danh mục dù đi bằng đường nào.

Gộp ba nguồn từ khóa vốn tách rời thành một chỉ mục ``từ khóa -> slug backend``:
  * từ khóa sản phẩm của bill (``app.bill.classifier._PRODUCT_KEYWORDS``, nhóm 9-nhóm đổi sang slug; được nạp lại từ DB),
  * từ vựng chat (``app.chat.vocab.CHAT_KEYWORDS``, phần chi tiêu),
  * từ điển tên cửa hàng của luồng thông báo (``app/data/merchant_categories.json``, bỏ ``income``).

Quy tắc: khớp theo từ nguyên vẹn (đã bỏ dấu), **từ khóa dài nhất thắng**; cùng độ dài thì slug cụ thể hơn thắng
(``_SPECIFIC_FIRST``: sữa bột là ``family`` chứ không phải ``food``). Không khớp thì thử khớp gần đúng cho lỗi OCR một chữ.
Không biết thì trả ``None``, không ép sang ``other``. Không log gì về nội dung.
"""
from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass
from difflib import SequenceMatcher

from app.bill import classifier
from app.chat import vocab
from app.chat.vi_text import fold
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.core.enums import CATEGORY_SLUGS
from app.pipeline.stages.categorizer import load_lexicon

# Khi hai từ khóa dài bằng nhau thì slug đứng trước thắng.
_SPECIFIC_FIRST = ("family", "education", "health", "housing", "bills", "entertainment", "transport", "shopping", "food", "other")
_PRIORITY = {slug: i for i, slug in enumerate(_SPECIFIC_FIRST)}
_VALID = frozenset(CATEGORY_SLUGS) - {"income"}

# "khác" trong từ vựng chat là TÊN danh mục "Khác", không phải tên một mặt hàng: "hàng khác" phải là chưa rõ.
_NOT_ITEM_WORDS = frozenset({"khac"})

_TOKEN = re.compile(r"[a-z]+|\d+")  # "bo200g" -> bo, 200, g: chữ dính số vẫn khớp

_FUZZY_MIN_KEYWORD = 7  # từ khóa ngắn hơn thì một chữ sai đã là một từ khác ("sua"/"sau")
_FUZZY_CUTOFF = 0.85  # một chữ sai trong từ khóa 8 ký tự = 0,875
# Cặp chữ OCR hay nhầm. Chỉ nhận lệch đúng một chữ nằm trong bảng này, hoặc thiếu/thừa một chữ: ngưỡng điểm một mình không phân biệt được
# "kham uot" (lỗi OCR) với "nuoc muoi" ≠ "nuoc suoi" (hai món khác nhau thật).
_OCR_PAIRS = {frozenset(p) for p in ("mn", "uv", "il", "l1", "i1", "o0", "ce", "ao", "nh", "rn", "s5", "b6", "g9", "z2")}


@dataclass(frozen=True)
class Resolution:
    slug: str | None  # slug backend, hoặc None khi chưa rõ
    source: str  # keyword | fuzzy | none


_NONE = Resolution(None, "none")
_state: tuple[dict[str, str], int] | None = None  # (từ khóa -> slug, số từ tối đa của một từ khóa); gán MỘT lần khi xây xong


def _tokens(text: str) -> list[str]:
    return _TOKEN.findall(text)


def _slug_of(group: str) -> str | None:
    slug = SLUG_BY_LOCAL_CATEGORY.get(group, group)  # bảng bill dùng 9 nhóm, từ vựng chat có cả slug backend (education, family)
    return slug if slug in _VALID else None


def _pairs():
    for group, words in classifier._PRODUCT_KEYWORDS.items():
        for word in words:
            yield word, _slug_of(group)
    for group, words in vocab.CHAT_KEYWORDS.items():
        for word in words:
            yield word, _slug_of(group)
    for word, slug in load_lexicon():
        yield fold(word), _slug_of(slug)


def _build() -> tuple[dict[str, str], int]:
    index: dict[str, str] = {}
    for word, slug in _pairs():
        if slug is None:
            continue
        key = " ".join(_tokens(fold(word)))
        if len(key) < 2 or key in _NOT_ITEM_WORDS:
            continue
        if key not in index or _PRIORITY[slug] < _PRIORITY[index[key]]:
            index[key] = slug
    return index, max((len(k.split()) for k in index), default=1)


def _index() -> dict[str, str]:
    global _state
    if _state is None:
        _state = _build()
    return _state[0]


def invalidate() -> None:
    """Đánh dấu chỉ mục đã cũ (bảng từ khóa vừa được nạp lại từ DB); lần ``resolve`` kế tiếp xây lại."""
    global _state
    _state = None


def _prepare(text: str) -> str:
    norm = " ".join(fold(unicodedata.normalize("NFC", text)).replace("'", "").split())  # OCR hay chèn dấu ' vào chữ
    norm = norm.replace("thuoc la", "")  # thuốc lá không phải thuốc chữa bệnh
    norm = re.sub(r"\bdien thoa\w?", "dienthoai", norm)  # "điện thoại" là một từ riêng, không phải "tiền điện" + "thoại"
    if re.search(r"\b(?:pass|password|mat khau)\b", norm):  # "Pass Wifi: 1234" không phải cước internet
        return ""
    if re.search(r"https?|www\.|\.(com|vn|net|org)\b", norm):  # địa chỉ web không phải tên sản phẩm
        return ""
    return norm


def _ocr_typo(cand: str, kw: str) -> bool:
    ops = [op for op in SequenceMatcher(None, cand, kw).get_opcodes() if op[0] != "equal"]
    if len(ops) != 1:
        return False
    tag, a0, a1, b0, b1 = ops[0]
    if tag == "replace":
        return a1 - a0 == 1 and b1 - b0 == 1 and frozenset((cand[a0], kw[b0])) in _OCR_PAIRS
    return (a1 - a0) + (b1 - b0) == 1  # thiếu hoặc thừa đúng một chữ


def _fuzzy(norm: str, index: dict[str, str]) -> tuple[str, int] | None:
    """(slug, độ dài từ khóa) khi cụm trong câu gần như trùng một từ khóa (OCR sai/thiếu một chữ). Hai slug cùng gần như nhau thì không đoán."""
    words = norm.split()
    best: tuple[float, int, str] | None = None
    tie = False
    for kw, slug in index.items():
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
                best, tie = (ratio, len(kw), slug), False
            elif (ratio, len(kw)) == best[:2] and slug != best[2]:
                tie = True
    return None if best is None or tie else (best[2], best[1])


def resolve(text: str, *, fuzzy: bool = True) -> Resolution:
    """``fuzzy=False`` cho văn bản người gõ (chat): khớp gần đúng chỉ dành cho lỗi OCR, còn câu chat thường bị nó khớp nhầm ("đẹp quá" ~ "dép quai")."""
    norm = _prepare(text or "")
    if not norm:
        return _NONE
    index = _index()
    max_words = _state[1] if _state else 1
    words = _tokens(norm)
    best: tuple[int, int, str] | None = None  # (độ dài từ khóa, -ưu tiên, slug)
    for n in range(1, max_words + 1):
        for start in range(len(words) - n + 1):
            key = " ".join(words[start : start + n])
            slug = index.get(key)
            if slug:
                candidate = (len(key), -_PRIORITY[slug], slug)
                if best is None or candidate > best:
                    best = candidate
    # Khớp gần đúng thắng khi từ khóa của nó DÀI hơn từ khóa khớp chính xác: "kham uot" (OCR đọc nhầm n/m của "khan uot") phải ra khăn ướt,
    # không bị từ khóa ngắn "kham" (khám) kéo đi. Chính xác dài bằng hoặc dài hơn thì vẫn thắng.
    near = _fuzzy(norm, index) if fuzzy else None
    if near and (best is None or near[1] > best[0]):
        return Resolution(near[0], "fuzzy")
    return Resolution(best[2], "keyword") if best else _NONE
