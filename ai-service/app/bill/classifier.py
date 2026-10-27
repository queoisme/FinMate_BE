"""Phân loại một hóa đơn thành nhóm danh mục từ TÊN CỬA HÀNG và các DÒNG CHỮ ĐÃ LỌC (không nhận ảnh hay chữ gốc).

Chép đúng logic của ocr-service/extract.py (classify_category_detailed và các hàm nó dùng) để kết quả không đổi khi bước phân loại
chuyển sang AI Service. Thứ tự ưu tiên: thương hiệu > từ khóa trong tên cửa hàng > mặt hàng > từ khóa trong toàn bộ chữ > mô hình học > other.
Chưa có bộ nhớ cửa hàng theo từng người dùng (còn ở ocr-service, sẽ chuyển cùng kiến thức sang DB của AI).
"""
import re
import unicodedata
from collections.abc import Callable

from app.bill import lexicon
from app.core import confidence
from app.bill.brands import BRAND_BACKEND_SLUG, BRAND_CATEGORIES, WHOLE_WORD_BRANDS

CATEGORIES = tuple(lexicon.load("category_groups.json")["groups"])  # cùng tệp với ocr-service và ràng buộc CHECK của DB

# từ vựng phân loại dùng chung với ocr-service/parsing/extract.py: shared_words.json (generic_store: loại hình ghi trong tên cửa hàng, thắng tên thương hiệu trùng của ngành khác)
_GENERIC_STORE = lexicon.shared_words("generic_store")
_GROCERIES_NOT_FOR = lexicon.shared_words("groceries_not_for")  # "Siêu thị điện máy", "Siêu thị nội thất" bán đồ gia dụng/điện tử, không phải thực phẩm
_NOT_MEDICINE = lexicon.shared_words("not_medicine")  # thuốc lá không phải thuốc chữa bệnh
_PASSWORD_LINE = re.compile(rf"\b(?:{lexicon.shared_alternation('password_line')})\b")  # "Pass Wifi: 1234" cuối phiếu quán cà phê không phải cước internet
_MIN_MODEL_PROB = confidence.get("bill.min_model_prob")

_MAX_MODEL_CONFIDENCE = confidence.get("bill.max_model")
# độ tin cậy theo nguồn của luật (app/data/confidence.json): thương hiệu > từ khóa tên cửa hàng > thương hiệu/từ khóa chỉ thấy trong thân bill
_BRAND_IN_MERCHANT = confidence.get("bill.brand_in_merchant")
_KEYWORD_IN_MERCHANT = confidence.get("bill.keyword_in_merchant")
_BRAND_IN_BODY = confidence.get("bill.brand_in_body")
_KEYWORD_IN_BODY = confidence.get("bill.keyword_in_body")
_PRODUCT_MAJORITY = confidence.get("bill.product_majority")
_PRODUCT_PLURALITY = confidence.get("bill.product_plurality")

_CATEGORY_KEYWORDS = lexicon.keyword_table("merchant_keywords.json")

_NAME_ONLY_KEYWORDS = set(lexicon.load("name_only_keywords.json"))

_PRODUCT_KEYWORDS = lexicon.keyword_table("product_keywords.json")

def _has_keyword(norm: str, keyword: str) -> bool:
    """Từ khóa <= 3 chữ phải là cả từ (tránh 'evn', 'kfc' khớp nhầm giữa từ khác)."""
    if len(keyword) <= 3:
        return re.search(rf"\b{re.escape(keyword)}\b", norm) is not None
    return keyword in norm


def _alnum(text: str) -> str:
    return re.sub(r"[^a-z0-9]", "", text)


def _brand_in(brand: str, norm: str) -> bool:
    """Khớp thương hiệu: nguyên văn, viết liền/có dấu chấm ("co.opmart"), hoặc sai một chữ do OCR ("co.cpmart").

    Khớp liền và khớp gần chỉ áp dụng cho tên đủ dài để khỏi khớp nhầm.
    """
    if brand in WHOLE_WORD_BRANDS:  # tên ngắn/thông dụng (nike, juno, puma...): chỉ khớp khi là cả từ, không khớp giữa chữ khác
        return re.search(rf"(?<![a-z0-9]){re.escape(brand)}(?![a-z0-9])", norm) is not None
    if brand in norm:
        return True
    key, text = _alnum(brand), _alnum(norm)
    if len(key) >= 6 and key in text:
        return True
    # khớp gần chỉ cho dòng ngắn cỡ tên thương hiệu (không áp lên cả đoạn văn dài)
    # khác tối đa 1 ký tự (rớt/chèn/nhầm một chữ do OCR): "cocpmart" ~ "coopmart", nhưng "cocomart" (khác 2) thì không
    return len(key) >= 7 and _within_one_edit(key, text)


def best_brand(matches: list[str], norm: str) -> str:
    """Nhiều tên cùng khớp: tên dài hơn (cụ thể hơn) thắng; hòa thì tên xuất hiện sớm hơn trong dòng thắng ("Muji Aeon" -> Muji)."""
    def pos(b: str) -> int:
        i = norm.find(b)
        return i if i >= 0 else 10**6
    return max(matches, key=lambda b: (len(_alnum(b)), -pos(b)))


def _within_one_edit(a: str, b: str) -> bool:
    if abs(len(a) - len(b)) > 1:
        return False
    i = 0
    while i < min(len(a), len(b)) and a[i] == b[i]:
        i += 1
    if i == len(a) or i == len(b):
        return True  # một chuỗi là tiền tố của chuỗi kia (hơn kém tối đa 1 ký tự)
    # bỏ qua một ký tự tại vị trí lệch rồi so phần còn lại: nhầm / chèn / rớt
    return a[i + 1:] == b[i + 1:] or a[i + 1:] == b[i:] or a[i:] == b[i + 1:]


def _norm(text: str) -> str:
    """Chữ thường, bỏ dấu tiếng Việt (đ -> d)."""
    text = unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D"))
    return "".join(c for c in text if unicodedata.category(c) != "Mn").lower()


def _category_from_products(lines: list[str], merchant: str | None = None) -> tuple[str, str, float] | None:
    """Đếm số dòng có sản phẩm của từng nhóm. Nhóm thắng rõ ràng: 2 dòng trở lên được tin hơn 1 dòng; hòa thì không đoán.

    Bỏ qua chính dòng tên cửa hàng: tên cửa hàng không phải sản phẩm (vd. "Lâm Dương" không phải "đường").
    """
    counts: dict[str, int] = {}
    for line in lines:
        if merchant and line.strip() == merchant.strip():
            continue
        norm = " ".join(_norm(line).replace("'", "").split())  # OCR hay chèn dấu ' vào chữ ("nuo'ng")
        for phrase in _NOT_MEDICINE:
            norm = norm.replace(phrase, "")
        norm = re.sub(r"\bdien thoa\w?", "dienthoai", norm)  # "điện thoại" (kể cả OCR sai chữ cuối) là một từ riêng, không phải "tiền điện" + "thoại"
        if _PASSWORD_LINE.search(norm):
            continue
        if re.search(r"https?|www\.|\.(com|vn|net|org)\b", norm):  # địa chỉ web không phải tên sản phẩm
            continue
        best: tuple[int, str] | None = None  # mỗi dòng chỉ tính cho từ khóa DÀI nhất khớp được: "banh mi sandwich" (tạp hóa) cụ thể hơn "banh mi" (ăn uống)
        for category, words in _PRODUCT_KEYWORDS.items():
            for w in words:
                # không đòi ranh giới sau từ khóa là khoảng trắng: OCR hay dính chữ với số ("bap bo200g")
                if re.search(rf"\b{re.escape(w)}(?![a-z])", norm) and (best is None or len(w) > best[0]):
                    best = (len(w), category)
        if best:
            counts[best[1]] = counts.get(best[1], 0) + 1
    if not counts:
        return None
    ranked = sorted(counts.items(), key=lambda kv: kv[1], reverse=True)
    top, n = ranked[0]
    if len(ranked) > 1 and ranked[1][1] == n:
        return None
    runner_up = ranked[1][1] if len(ranked) > 1 else 0
    return top, "product", _PRODUCT_MAJORITY if n >= 2 and n >= 2 * runner_up else _PRODUCT_PLURALITY


def brand_backend_slug(merchant: str | None) -> str | None:
    """Slug backend của thương hiệu có nhóm nằm ngoài 9 nhóm của bộ luật bill (vd. Con Cưng → family); None khi không phải."""
    norm = _norm(merchant or "")
    hits = [b for b in BRAND_BACKEND_SLUG if _brand_in(b, norm)]
    return BRAND_BACKEND_SLUG[best_brand(hits, norm)] if hits else None


def classify_category_detailed(
    merchant: str | None,
    lines: list[str],
    predict: Callable[[str | None, list[str]], tuple[str, float]] | None = None,
) -> tuple[str, str, float]:
    """(danh mục, nguồn, độ tin cậy). Ưu tiên: thương hiệu > từ khóa > mô hình học > other. (Cửa hàng người dùng đã chốt do route ``/bill/classify`` tra trước, không ở đây.)"""
    merchant_norm = _norm(merchant or "")
    body_norm = _norm(" ".join(l for l in lines if not _PASSWORD_LINE.search(_norm(l))))
    matches = [b for b in BRAND_CATEGORIES if _brand_in(b, merchant_norm)]
    # "BÁCH HÓA MINI AN KHANG": tên cửa hàng ghi rõ loại hình bách hóa/tạp hóa thì đó là tạp hóa, dù đoạn sau trùng tên một chuỗi khác (nhà thuốc An Khang).
    if matches and any(g in merchant_norm for g in _GENERIC_STORE) and BRAND_CATEGORIES[best_brand(matches, merchant_norm)] != "groceries":
        return "groceries", "keyword", _KEYWORD_IN_MERCHANT
    if matches:  # nhiều tên khớp (vd. "Supersports Aeon"): tên cụ thể hơn (dài hơn) thắng tên trung tâm thương mại
        return BRAND_CATEGORIES[best_brand(matches, merchant_norm)], "brand", _BRAND_IN_MERCHANT
    for category, keywords in _CATEGORY_KEYWORDS.items():
        if category == "groceries" and any(w in merchant_norm for w in _GROCERIES_NOT_FOR):
            continue
        if any(_has_keyword(merchant_norm, k) for k in keywords):
            return category, "keyword", _KEYWORD_IN_MERCHANT
    for brand, category in BRAND_CATEGORIES.items():  # tên thương hiệu viết liền trong website/địa chỉ
        if _brand_in(brand, body_norm):
            return category, "brand", _BRAND_IN_BODY
    product = _category_from_products(lines, merchant)
    if product:
        return product
    for category, keywords in _CATEGORY_KEYWORDS.items():
        if any(_has_keyword(body_norm, k) for k in keywords if k not in _NAME_ONLY_KEYWORDS):
            return category, "keyword", _KEYWORD_IN_BODY
    if predict:
        category, prob = predict(merchant, lines)
        if category != "other" and prob >= _MIN_MODEL_PROB:
            # gợi ý của mô hình luôn dưới ngưỡng 0.7 để app vẫn nhờ người dùng xác nhận
            return category, "model", round(min(prob, _MAX_MODEL_CONFIDENCE), 2)
    return "other", "none", 0.0
