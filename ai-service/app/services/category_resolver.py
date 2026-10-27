"""Bộ phân loại chung cho chat và món trên hóa đơn: một cụm từ ra MỘT danh mục dù đi bằng đường nào.

Gộp ba nguồn từ khóa vốn tách rời thành một chỉ mục ``từ khóa -> slug backend``:
  * từ khóa sản phẩm của bill (``app.bill.classifier._PRODUCT_KEYWORDS``, nhóm 9-nhóm đổi sang slug; được nạp lại từ DB),
  * từ vựng chat (``app.services.vocab.CHAT_KEYWORDS``, phần chi tiêu),
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
from app.services import intent_cues, vocab
from app.services.vi_text import fold
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.core.enums import CATEGORY_SLUGS
from app.pipeline.stages.categorizer import load_lexicon, subscribe_lexicon

# Khi hai từ khóa dài bằng nhau thì slug đứng trước thắng.
_SPECIFIC_FIRST = ("family", "education", "health", "housing", "bills", "entertainment", "transport", "shopping", "food", "other")
_PRIORITY = {slug: i for i, slug in enumerate(_SPECIFIC_FIRST)}
_VALID = frozenset(CATEGORY_SLUGS) - {"income"}

# Hai bảng dưới đây nằm trong app/data/intent_cues.json (bản admin kích hoạt thay được; ``invalidate`` chạy khi nó đổi):
#   * ``lists.not_item_words``: "khác" trong từ vựng chat là TÊN danh mục "Khác", không phải tên một mặt hàng: "hàng khác" phải là chưa rõ.
#   * ``forms.needs_accent``: từ khóa chỉ được nhận khi câu gốc viết ĐÚNG dấu: bỏ dấu thì "chợ" (đi chợ) trùng "cho" (đưa) và mọi câu có chữ "cho" thành
#     Ăn uống ("mẹ tôi cho ba triệu", "cho mẹ 500k"); "noi" cũng là nói/nơi/nối nên chỉ "nồi" có dấu mới là đồ gia dụng.

_TOKEN = re.compile(r"[a-z]+|\d+")  # "bo200g" -> bo, 200, g: chữ dính số vẫn khớp
_DIEN_THOAI = re.compile(r"\bdien thoa\w?")  # kể cả OCR sai chữ cuối

_FUZZY_MIN_KEYWORD = 7  # từ khóa ngắn hơn thì một chữ sai đã là một từ khác ("sua"/"sau")
_FUZZY_CUTOFF = 0.85  # một chữ sai trong từ khóa 8 ký tự = 0,875
# Cặp chữ OCR hay nhầm. Chỉ nhận lệch đúng một chữ nằm trong bảng này, hoặc thiếu/thừa một chữ: ngưỡng điểm một mình không phân biệt được
# "kham uot" (lỗi OCR) với "nuoc muoi" ≠ "nuoc suoi" (hai món khác nhau thật).
_OCR_PAIRS = {frozenset(p) for p in ("mn", "uv", "il", "l1", "i1", "o0", "ce", "ao", "nh", "rn", "s5", "b6", "g9", "z2", "o6")}


@dataclass(frozen=True)
class Resolution:
    slug: str | None  # slug backend, hoặc None khi chưa rõ
    source: str  # keyword | fuzzy | none


_NONE = Resolution(None, "none")
# (từ khóa -> slug, số từ tối đa của một từ khóa, từ khóa chỉ dành cho chat); gán MỘT lần khi xây xong
_State = tuple[dict[str, str], int, frozenset[str]]
_state: _State | None = None


def _tokens(text: str) -> list[str]:
    return _TOKEN.findall(text)


def _slug_of(group: str) -> str | None:
    slug = SLUG_BY_LOCAL_CATEGORY.get(group, group)  # bảng bill dùng 9 nhóm, từ vựng chat có cả slug backend (education, family)
    return slug if slug in _VALID else None


def _pairs():
    """(từ khóa, slug, có phải từ khóa sản phẩm của bill không)."""
    for group, words in classifier._PRODUCT_KEYWORDS.items():
        for word in words:
            yield word, _slug_of(group), True
    for group, words in vocab.CHAT_KEYWORDS.items():
        for word in words:
            yield word, _slug_of(group), False
    for word, slug in load_lexicon():
        yield fold(word), _slug_of(slug), False


# Từ ngắn không nằm trong bảng sản phẩm của bill (từ vựng chat, từ điển cửa hàng: "an" = ăn, "lau" = lẩu, "cho" = chợ) bỏ dấu thì trùng từ khác
# ("khăn ăn", "chổi lau nhà", "an toàn"): chỉ tin khi người dùng GÕ câu chat, không dùng để đoán tên món trên hóa đơn.
_CHAT_ONLY_MAX_LEN = 3


def _build() -> _State:
    index: dict[str, str] = {}
    from_product: set[str] = set()
    all_keys: set[str] = set()
    not_item_words = {fold(word) for word in intent_cues.current().lists["not_item_words"]}
    for word, slug, is_product in _pairs():
        if slug is None:
            continue
        key = " ".join(_tokens(_DIEN_THOAI.sub("dienthoai", fold(word))))  # cùng phép gộp với _prepare, nếu không "mua dien thoai" không bao giờ khớp
        if len(key) < 2 or key in not_item_words:
            continue
        all_keys.add(key)
        if is_product:
            from_product.add(key)
        if key not in index or _PRIORITY[slug] < _PRIORITY[index[key]]:
            index[key] = slug
    chat_only = frozenset(k for k in all_keys - from_product if " " not in k and len(k) <= _CHAT_ONLY_MAX_LEN)
    return index, max((len(k.split()) for k in index), default=1), chat_only


def _get_state() -> _State:
    global _state
    state = _state
    if state is None:
        state = _state = _build()
    return state


def _index() -> dict[str, str]:
    return _get_state()[0]


def invalidate() -> None:
    """Đánh dấu chỉ mục đã cũ (bảng từ khóa vừa được nạp lại từ DB); lần ``resolve`` kế tiếp xây lại."""
    global _state
    _state = None


vocab.subscribe(invalidate)  # từ điển chat đổi (admin kích hoạt) thì chỉ mục dựng từ nó cũng phải xây lại
subscribe_lexicon(invalidate)  # từ điển tên cửa hàng cũng vậy
intent_cues.subscribe(lambda cues: invalidate())  # bảng "không phải tên món" nằm trong intent_cues và được dùng lúc dựng chỉ mục


def _prepare(text: str) -> str:
    norm = " ".join(fold(unicodedata.normalize("NFC", text)).replace("'", "").split())  # OCR hay chèn dấu ' vào chữ
    for phrase in classifier._NOT_MEDICINE:  # từ vựng chung với ocr-service: shared_words.json
        norm = norm.replace(phrase, "")
    norm = _DIEN_THOAI.sub("dienthoai", norm)  # "điện thoại" là một từ riêng, không phải "tiền điện" + "thoại"
    if classifier._PASSWORD_LINE.search(norm):
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


def resolve(text: str, *, chat: bool = False) -> Resolution:
    """``chat=True`` cho câu người gõ: nhận cả từ ngắn của từ vựng chat ("ăn", "lẩu", "chợ") và KHÔNG khớp gần đúng, vì khớp gần đúng chỉ dành cho
    lỗi OCR còn câu chat thường bị nó khớp nhầm ("đẹp quá" ~ "dép quai"). Mặc định là tên món trên hóa đơn."""
    norm = _prepare(text or "")
    if not norm:
        return _NONE
    original = unicodedata.normalize("NFC", text).lower()
    index, max_words, chat_only = _get_state()  # MỘT bản chụp: invalidate() chạy xen giữa không làm max_words về mặc định
    needs_accent = intent_cues.current().needs_accent
    words = _tokens(norm)
    best: tuple[int, int, str] | None = None  # (độ dài từ khóa, -ưu tiên, slug)
    weak_best: tuple[int, int, str] | None = None  # từ khóa YẾU (``weak_keywords`` của từ điển: "cho con", "cho me"...): chỉ nói tiền đến tay ai, không nói mua gì
    weak_keys = vocab.WEAK_KEYWORDS  # một bản chụp cho cả lượt; đọc qua module để thấy bản admin vừa kích hoạt
    for n in range(1, max_words + 1):
        for start in range(len(words) - n + 1):
            key = " ".join(words[start : start + n])
            slug = index.get(key)
            accent = needs_accent.get(key)
            if slug and (chat or key not in chat_only) and (accent is None or accent in original):
                candidate = (len(key), -_PRIORITY[slug], slug)
                if key in weak_keys:
                    if weak_best is None or candidate > weak_best:
                        weak_best = candidate
                elif best is None or candidate > best:
                    best = candidate
    best = best or weak_best  # có từ khóa thường ("mua keo cho con") thì nó quyết; chỉ có từ khóa yếu ("cho con 500k") thì từ yếu quyết
    # Khớp gần đúng thắng khi từ khóa của nó DÀI hơn từ khóa khớp chính xác: "kham uot" (OCR đọc nhầm n/m của "khan uot") phải ra khăn ướt,
    # không bị từ khóa ngắn "kham" (khám) kéo đi. Chính xác dài bằng hoặc dài hơn thì vẫn thắng.
    near = None if chat else _fuzzy(norm, index)
    if near and (best is None or near[1] > best[0]):
        return Resolution(near[0], "fuzzy")
    return Resolution(best[2], "keyword") if best else _NONE
