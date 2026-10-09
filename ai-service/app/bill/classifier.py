"""Phân loại một hóa đơn thành nhóm danh mục từ TÊN CỬA HÀNG và các DÒNG CHỮ ĐÃ LỌC (không nhận ảnh hay chữ gốc).

Chép đúng logic của ocr-service/extract.py (classify_category_detailed và các hàm nó dùng) để kết quả không đổi khi bước phân loại
chuyển sang AI Service. Thứ tự ưu tiên: thương hiệu > từ khóa trong tên cửa hàng > mặt hàng > từ khóa trong toàn bộ chữ > mô hình học > other.
Chưa có bộ nhớ cửa hàng theo từng người dùng (còn ở ocr-service, sẽ chuyển cùng kiến thức sang DB của AI).
"""
import re
import unicodedata
from collections.abc import Callable

from app.bill.brands import BRAND_CATEGORIES, WHOLE_WORD_BRANDS

CATEGORIES = ("dining", "transport", "shopping", "groceries", "housing", "utilities", "healthcare", "entertainment", "other")

_MIN_MODEL_PROB = 0.5

_MAX_MODEL_CONFIDENCE = 0.69

# Thứ tự quan trọng: nhóm đứng trước được ưu tiên khi trùng từ khóa.
_CATEGORY_KEYWORDS = {
    "transport": ("xang", "petro", "grab", "taxi", "gojek", "nha xe", "bai giu xe", "bai do xe", "gui xe", "rua xe", "garage", "tram xe"),
    "healthcare": ("pharmacy", "pharmacity", "nha thuoc", "nhathuoc", "benh vien", "phong kham", "nha khoa", "rang ham mat", "tham my vien", "da lieu", "mat kinh", "eyewear", "benhvien", "phongkham", "nhakhoa"),
    "entertainment": ("cinema", "cgv", "karaoke", "rap chieu", "rap phim", "nha hat", "khu vui choi", "cong vien nuoc", "bowling"),
    "groceries": ("sieu thi", "mart", "coop", "bach hoa xanh", "winmart", "tap hoa", "thuc pham", "cho dau moi", "dai ly sua"),
    "housing": ("dien luc", "evn", "tien nuoc", "cap nuoc", "nuoc sach", "nha tro", "phong tro", "chung cu", "dai ly gas", "binh gas"),
    "utilities": ("internet", "viettel", "vnpt", "fpt telecom", "truyen hinh", "vien thong", "wifi"),
    "dining": ("cafe", "coffee", "pho", "banh mi", "com tam", "quan com", "tra sua", "restaurant",
               "nha hang", "quan an", "pizza", "kfc", "highland", "quan bun", "bun bo", "bun cha", "quan nhau", "tiem banh"),
    "shopping": ("shop", "store", "thoi trang", "quan ao", "vang bac da quy", "trang suc", "kim hoan", "tiem vang", "cua hang giay", "giay dep",
                 "my pham", "phu kien", "dien may", "noi that"),
}

# Bỏ dấu làm "phở" trùng "phố" (Thành phố...): từ khóa này chỉ tin khi nằm trong tên cửa hàng.
_NAME_ONLY_KEYWORDS = {"pho", "nha tro", "phong tro", "chung cu"}

# Từ vựng sản phẩm (đã bỏ dấu), khớp nguyên từ. ponytail: bảng nhỏ viết tay; mô hình học (giai đoạn B) sẽ thay dần khi đủ dữ liệu.
_PRODUCT_KEYWORDS = {
    # Bỏ các từ mơ hồ khi mất dấu: "duong" (đường ăn/đường phố), "lau" (lẩu/lầu), "chao" (cháo/chào), "muoi" (muối/mười).
    "dining": ("bun", "bun bo", "banh mi", "com", "mi quang", "hu tieu", "xoi", "lau thai", "nuong",
               "ca phe", "tra sua", "sinh to", "nuoc ep", "tra dao", "banh canh", "goi cuon",
               "pizza", "spaghetti", "carbonara", "cha ca", "nem ca", "tra da",
               "hong tra", "tra dai loan", "tra xanh", "tra den", "tra vai", "tra tac", "tra chanh", "olong", "matcha", "tran chau",
               "thach dua", "thach suong sao", "thach tran chau", "soda chanh", "nuoc cam ep", "sua chua uong",
               "khoai mi", "banh khoai",
               # món quen thuộc ở quán ăn nhanh, quán cơm, quán phở
               "burger", "ga ran", "khoai chien", "khoai tay chien", "pho bo", "pho ga", "com suon", "bun cha", "bun rieu", "ca phe sua", "bac xiu"),
    "groceries": ("gao", "nuoc mam", "dau an", "duong cat", "sua tuoi", "trung ga", "thit heo", "thit bo",
                  "thit ga", "bap bo", "bo vien", "suon heo", "rau", "mi goi", "banh keo", "snack",
                  "nuoc ngot", "bia", "xuong ga", "sua bot", "ta quan", "ta dan", "dau hao", "tuong ot", "nuoc tuong", "hat nem", "gia vi",
                  "bot ngot", "bot canh", "bot chien", "mayonnaise", "nuoc cham",
                  # hàng tiêu dùng, thực phẩm tươi/đóng gói thường gặp ở tạp hóa, đại lý
                  "bot giat", "nuoc rua chen", "giay ve sinh", "nuoc xa vai", "nuoc suoi", "ca chua", "xuc xich", "ca basa",
                  "banh mi sandwich", "banh mi gon"),
    "healthcare": ("paracetamol", "vitamin", "augmentin", "voltaren", "khau trang", "gac y te", "kim luon",
                   "natri clorid", "thuoc", "amoxicillin", "oresol",
                   "nieng rang", "tram rang", "cao voi rang", "nho rang", "rang su",
                   "kinh can", "kinh mat", "trong kinh", "gong kinh"),
    "transport": ("xang", "dau diesel", "ve xe", "gui xe", "do xe", "sua xe", "va lop", "thay nhot", "rua xe", "cau duong", "ve tau", "ve may bay", "cuoc taxi", "cuoc xe"),
    # vé và dịch vụ vui chơi
    "entertainment": ("ve xem phim", "vexem phim", "xem phim", "ve phim", "bap nuoc", "ve vao cong", "ve tham quan", "ve cong vien", "cong vien nuoc", "khu vui choi",
                      "may choi game", "nap the game", "bowling", "karaoke", "ve ca nhac", "ve concert", "ve kich", "truot bang", "gio choi"),
    # dịch vụ làm đẹp/cá nhân (không có nhóm riêng nên là "other"): spa, salon, cắt tóc, nail, mi, triệt lông
    "other": ("cat toc", "goi dau thu gian", "nhuom toc", "uon toc", "lam mong", "noi mi", "triet long", "tam trang", "xong hoi", "cao rau"),
    # phiếu mua/bán vàng bạc thường không có logo: nhận qua tên hàng
    "shopping": ("vang 9999", "vang 999", "vang 24k", "vang 18k", "nhan tron", "nhan cuoi", "nhan vang", "kim cuong", "vang mieng",
                 # hóa đơn mỹ phẩm không logo (siêu thị/nhà thuốc nhiều dòng khác nên danh mục của họ vẫn thắng)
                 "sua rua mat", "tay trang", "kem chong nang", "son moi", "mat na", "nuoc hoa",
                 # quần áo, giày dép, túi xách, đồ điện tử nhỏ
                 "quan jean", "quan tay", "quan short", "ao thun", "ao khoac", "ao so mi", "ao polo", "dam cong so", "dam du tiec", "chan vay",
                 "tui xach", "balo", "that lung", "kinh ram", "mu non", "giay the thao", "giay da nam", "giay da nu", "giay cao got", "giay sandal",
                 "dep sandal", "dep quai", "dep tong", "tat nam", "tai nghe", "sac du phong", "cap sac", "op lung", "noi chien", "may say toc", "ban phim"),
    "housing": ("tien dien", "tien nuoc", "tien nha", "tien thue nha", "tien thue phong", "tien rac", "tien gas", "binh gas", "phi quan ly chung cu", "phi quan ly", "phi chung cu"),
    "utilities": ("cuoc internet", "cuoc dienthoai", "nap dienthoai", "tien dienthoai", "nap data", "truyen hinh cap", "wifi", "goi cuoc",
                  "goi data", "cuoc di dong", "cap quang"),  # "tien dienthoai" (xem _category_from_products) không bị "tien dien" (nhà ở) khớp nhầm
}

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
        norm = norm.replace("thuoc la", "")  # thuốc lá không phải thuốc chữa bệnh
        norm = re.sub(r"\bdien thoa\w?", "dienthoai", norm)  # "điện thoại" (kể cả OCR sai chữ cuối) là một từ riêng, không phải "tiền điện" + "thoại"
        if re.search(r"\b(?:pass|password|mat khau)\b", norm):  # "Pass Wifi: 1234" cuối phiếu quán cà phê không phải cước internet
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
    return top, "product", 0.7 if n >= 2 and n >= 2 * runner_up else 0.6


def classify_category_detailed(
    merchant: str | None,
    lines: list[str],
    remembered: str | None = None,
    predict: Callable[[str | None, list[str]], tuple[str, float]] | None = None,
) -> tuple[str, str, float]:
    """(danh mục, nguồn, độ tin cậy). Ưu tiên: bộ nhớ > thương hiệu > từ khóa > mô hình học > other."""
    if remembered:
        return remembered, "memory", 1.0
    merchant_norm = _norm(merchant or "")
    body_norm = _norm(" ".join(l for l in lines if not re.search(chr(92) + "b(?:pass|password)" + chr(92) + "b", _norm(l))))  # dòng "Pass Wifi: ..." không phải cước internet
    matches = [b for b in BRAND_CATEGORIES if _brand_in(b, merchant_norm)]
    if matches:  # nhiều tên khớp (vd. "Supersports Aeon"): tên cụ thể hơn (dài hơn) thắng tên trung tâm thương mại
        return BRAND_CATEGORIES[best_brand(matches, merchant_norm)], "brand", 0.8
    for category, keywords in _CATEGORY_KEYWORDS.items():
        if category == "groceries" and ("dien may" in merchant_norm or "noi that" in merchant_norm):
            continue  # "Siêu thị điện máy", "Siêu thị nội thất" bán đồ gia dụng/điện tử, không phải thực phẩm
        if any(_has_keyword(merchant_norm, k) for k in keywords):
            return category, "keyword", 0.75
    for brand, category in BRAND_CATEGORIES.items():  # tên thương hiệu viết liền trong website/địa chỉ
        if _brand_in(brand, body_norm):
            return category, "brand", 0.7
    product = _category_from_products(lines, merchant)
    if product:
        return product
    for category, keywords in _CATEGORY_KEYWORDS.items():
        if any(_has_keyword(body_norm, k) for k in keywords if k not in _NAME_ONLY_KEYWORDS):
            return category, "keyword", 0.6
    if predict:
        category, prob = predict(merchant, lines)
        if category != "other" and prob >= _MIN_MODEL_PROB:
            # gợi ý của mô hình luôn dưới ngưỡng 0.7 để app vẫn nhờ người dùng xác nhận
            return category, "model", round(min(prob, _MAX_MODEL_CONFIDENCE), 2)
    return "other", "none", 0.0
