"""Tách thông tin hóa đơn từ các dòng chữ OCR bằng luật đơn giản."""
import re
import unicodedata
from typing import Callable

from brands import BRAND_CATEGORIES, WHOLE_WORD_BRANDS

CATEGORIES = ("dining", "transport", "shopping", "groceries",
              "housing", "utilities", "healthcare", "entertainment", "other")

_AMOUNT_RE = re.compile(r"\d+(?:[.,]\d{3})*(?:[.,]\d{1,2})?(?!\d)")
_PAYABLE_KEYWORDS = ("phai tra", "can thanh toan", "tong thanh toan", "thanh toan")
_TOTAL_KEYWORDS = ("tong cong", "thanh tien", "tong", "total", "cong tien hang")
_MIN_AMOUNT = 1000  # bỏ qua số nhỏ như "Tổng số lượng 3"
_MAX_AMOUNT = 999_999_999  # dưới 1 tỷ đồng
_MIN_NAME_CONFIDENCE = 0.8  # dưới mức này thường là chữ viết tay/logo đọc hỏng
_MIN_LABEL_CONFIDENCE = 0.7  # dòng có nhãn rõ ("Tên NPP:") được nới ngưỡng vì nhãn là bằng chứng thêm
_GENERIC_STORE = ("bach hoa", "tap hoa")  # loại hình ghi trong tên cửa hàng, thắng tên thương hiệu trùng của ngành khác
_BUSINESS_WORDS = ("nha thuoc", "cong ty", "chi nhanh", "sieu thi", "nha hang", "tap hoa", "pharmacy", "quan an", "benh vien", "phong kham", "nha khoa")
_TITLE_MARKERS = ("phieu tinh tien", "phieu thanh toan", "hoa don", "phieu thu", "phieu giao", "phieu xuat kho",
                  "phieu nhap kho", "phieu ban hang", "phieu ban le", "phieu mua hang", "don hang", "giao nhan")
_HEADER_DATE = re.compile(r"^([A-Z][A-Z0-9]{2,14})\s*[-–]\s*\d{1,2}[/.]\d{1,2}[/.]\d{2,4}")  # "WAMIHS-21/09/2026 15:41"
_SITE_RE = re.compile(r"www\.([a-z0-9-]{4,})\.")
_MIN_OCR_CONFIDENCE = 0.7
_MIN_LINE_CONFIDENCE = 0.85  # chỉnh theo 2 hóa đơn mẫu: Kisnac mờ ~0.57, hóa đơn sạch 1.0
_MIN_COVERAGE = 0.7
_MIN_CATEGORY_CONFIDENCE = 0.7
_MIN_MODEL_PROB = 0.5         # xác suất tối thiểu của mô hình học để đưa ra gợi ý
_MAX_MODEL_CONFIDENCE = 0.69  # trần độ tin cậy của gợi ý mô hình (< 0.7 => luôn cần xác nhận)

# Thứ tự quan trọng: nhóm đứng trước được ưu tiên khi trùng từ khóa.
_CATEGORY_KEYWORDS = {
    "transport": ("xang", "petro", "grab", "taxi", "gojek", "nha xe", "bai giu xe", "bai do xe", "gui xe", "rua xe", "garage", "tram xe"),
    "healthcare": ("pharmacy", "pharmacity", "nha thuoc", "nhathuoc", "benh vien", "phong kham", "nha khoa", "rang ham mat", "tham my vien", "da lieu", "mat kinh", "eyewear", "benhvien", "phongkham", "nhakhoa"),
    "entertainment": ("cinema", "cgv", "karaoke", "rap chieu", "rap phim", "nha hat", "khu vui choi", "khu giai tri", "giai tri", "cong vien nuoc", "bowling"),
    "groceries": ("sieu thi", "mart", "coop", "bach hoa xanh", "winmart", "tap hoa", "thuc pham", "cho dau moi", "dai ly sua", "nuoc tinh khiet", "dai ly nuoc"),  # đại lý nước uống: bán nước đóng chai/bình, không phải cước nước sinh hoạt
    "housing": ("dien luc", "evn", "tien nuoc", "cap nuoc", "nuoc sach", "nha tro", "phong tro", "chung cu", "dai ly gas", "binh gas"),
    "utilities": ("internet", "viettel", "vnpt", "fpt telecom", "truyen hinh", "vien thong", "wifi"),
    "dining": ("cafe", "coffee", "pho", "banh mi", "com tam", "quan com", "tra sua", "restaurant",
               "nha hang", "quan an", "pizza", "kfc", "highland", "quan bun", "bun bo", "bun cha", "quan nhau", "tiem banh"),
    "shopping": ("shop", "store", "thoi trang", "quan ao", "vang bac da quy", "trang suc", "kim hoan", "tiem vang", "cua hang giay", "giay dep",
                 "my pham", "phu kien", "dien may", "noi that"),
}


# Bỏ dấu làm "phở" trùng "phố" (Thành phố...): từ khóa này chỉ tin khi nằm trong tên cửa hàng.
_NAME_ONLY_KEYWORDS = {"pho", "nha tro", "phong tro", "chung cu"}  # chỉ tin khi nằm trong tên cửa hàng (địa chỉ giao hàng cũng có thể in "nhà trọ")


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


_CODE_LABEL_BEFORE = re.compile(r"(?:^|[^a-z])(?:nv|ct|ma|id|hd|sdt|mst|stt|so hd|so ct)\s*[:.\-]?\s*$")
_DATE_OR_TIME = re.compile(r"\d\s*[/:]\s*\d")


_ACCOUNT_LINE = re.compile(r"(?:^|[^a-z])(?:tai khoan|stk|so tk|ngan hang)(?:[^a-z]|$)")


_DATE_IN_TEXT = re.compile(r"\b\d{1,2}\s*[/\-.]\s*\d{1,2}\s*[/\-.]\s*\d{2,4}\b|\b\d{1,2}\s*/\s*\d{4}\b")


def _amounts(text: str, skip_negative: bool = False) -> list[int]:
    if _ACCOUNT_LINE.search(_norm(text)):
        return []  # dòng số tài khoản/ngân hàng ("Số tài khoản nhận thanh toán: 909915990...") không chứa số tiền của hóa đơn
    text = _DATE_IN_TEXT.sub(lambda m: " " * len(m.group(0)), text)  # "thanh toán trước ngày 05/10/2026": số trong ngày tháng không phải tiền
    result = []
    for m in _AMOUNT_RE.finditer(text):
        if _CODE_LABEL_BEFORE.search(_norm(text[:m.start()])):
            continue  # số đứng sau nhãn mã (NV:, Số HD:, MST...) là mã, không phải tiền
        if skip_negative and m.start() and text[m.start() - 1] == "-" and (m.start() == 1 or not text[m.start() - 2].isalnum()):
            continue  # "-4.800", "-30.000": khoản giảm giá, không phải số tiền phải trả
        s = m.group(0)
        tail = re.search(r"[.,]\d{1,2}$", s)
        # "1,305,00" / "1.305.00": cùng một dấu phân cách hai lần mà nhóm cuối chỉ 2 chữ số = OCR cắt mất chữ số cuối khi xuống dòng
        # (không phải số lẻ, số lẻ thì khác dấu như "1,305.00"). Tiền VND hầu như luôn tròn chục nên bù "0". ponytail: đoán 0, nhắc kiểm tra nếu sai thì sửa tay.
        cut = re.fullmatch(r"\d{1,3}(?:([.,])\d{3})+\1\d{2}", s) if tail else None
        if cut:
            s += "0"
        elif tail and not re.search(r"[.,]\d{3}$", s):
            s = s[: -len(tail.group(0))]
        digits = re.sub(r"\D", "", s)
        if len(digits) == 4 and 1900 <= int(digits) <= 2100 and not re.search(r"[.,]", s):
            continue  # 4 chữ số trần trong khoảng 1900-2100 là năm ("10/2026", "Năm 2026"), không phải tiền
        if len(digits) >= 9 and not re.search(r"[.,]", s):
            continue  # 9 chữ số liền không dấu phân cách là mã/số tài khoản, không phải tiền (tiền lớn luôn có dấu . hoặc ,)
        # số quá lớn hoặc từ 10 chữ số (kể cả số 0 đầu: mã số thuế, điện thoại, mã vạch) không phải tiền
        if digits and len(digits) <= 9 and int(digits) <= _MAX_AMOUNT:
            result.append(int(digits))
    return result


_CASH_LABEL = re.compile(r"tien mat|tien khach|khach dua|tien thua|tien thoi|tra lai|\bcash\b|\bchange\b")  # tiền khách đưa / tiền thối lại


def _keyword_total(lines: list[str], keywords: tuple[str, ...], skip_subtotal: bool = False) -> int | None:
    """Số tiền lớn nhất trên (hoặc ngay dưới) các dòng chứa từ khóa."""
    candidates = []
    markers = [_alnum(m) for m in _TITLE_MARKERS]

    def letters(text: str) -> int:  # số chữ cái, bỏ đơn vị tiền
        return sum(ch.isalpha() for ch in re.sub(r"\b(?:dong|vnd|d)\b", " ", _norm(text)))

    def money(text: str) -> list[int]:
        return [a for a in _amounts(text, True) if a >= _MIN_AMOUNT]

    for i, line in enumerate(lines):
        # "tong" trơn phải gần đầu một từ: "TraYuzumatongboncha" / "MatOng" (mật ong) là tên hàng. Cho phép MỘT ký tự rác
        # dính phía trước ("ITong tien:"). Từ khóa dài vẫn khớp giữa chữ vì OCR hay dính nhãn ("Khachphai tra").
        norm = _norm(line)
        if not any(re.search(r"(?<![a-z]{2})tong", norm) if k == "tong" else k in norm for k in keywords):
            continue
        squeezed = _alnum(_norm(line))
        if any(m in squeezed for m in markers):
            continue  # "PHIẾU THANH TOÁN ..." là tiêu đề, không phải nhãn tổng tiền
        if skip_subtotal and "tamtinh" in squeezed:
            continue  # "Tổng (tạm tính)" là trước khi áp mã giảm giá, số cuối cùng nằm ở "Tổng cộng"
        if "soluong" in squeezed:
            continue  # "Tổng số lượng" là số lượng hàng, không phải tiền
        found = [a for a in _amounts(line, True) if a >= _MIN_AMOUNT]  # số thứ tự mục ("2. Số tiền phải thanh toán:") không tính là có tiền trên dòng
        if not found:
            # OCR tách cột: số tiền có thể nằm vài dòng dưới nhãn (xen một dòng "Số lượng"...). Chỉ nhận dòng
            # gần như chỉ là số tiền (không phải ngày giờ), lấy số lớn nhất trong 3 dòng kế để không dính số
            # bị rớt chữ số ngay dưới nhãn.
            # OCR đọc số bên phải TRƯỚC nhãn bên trái cả khối ("47,000", "Tổng cộng", "50,000", "Tiền mặt"): nếu ngay trước nhãn
            # đã có số tiền thì số nằm ngay trên nhãn tiền khách đưa / tiền thừa là của nhãn đó, không phải của nhãn này.
            # Chỉ xét nhãn tiền mặt/tiền thừa: dòng chữ thường (cảm ơn, ngày, VAT, phí) sau tổng không làm mất tổng.
            # ponytail: khối nhãn-trước-số xen kẽ có dòng tiền mặt/tiền thừa ("Tong cong", "54,000", "Tien khach dua", "100,000",
            # "Tien thua"...) vẫn có thể ra giá món cuối hoặc tiền khách đưa (code cũ cũng sai); sửa thật = ghép nhãn với số theo tọa độ y của hộp chữ.
            amount_before = i > 0 and letters(lines[i - 1]) <= 3 and bool(money(lines[i - 1]))
            for j in range(i + 1, min(i + 4, len(lines))):
                nxt = lines[j]
                if letters(nxt) <= 3 and not _DATE_OR_TIME.search(nxt):
                    if amount_before and j + 1 < len(lines) and _CASH_LABEL.search(_norm(lines[j + 1])):
                        continue
                    found.extend(money(nxt))
                elif found and _CASH_LABEL.search(_norm(nxt)):
                    break  # đã có số của nhãn này; số sau nhãn tiền khách đưa / tiền thừa ("Tiền khách đưa: 200.000") là của nhãn đó, không phải tổng
            if not found and i and letters(lines[i - 1]) <= 3:
                # OCR hay đọc số tiền ở bên phải TRƯỚC nhãn bên trái cùng hàng ("37.200đ" rồi "Tổng cộng"): nhãn không có số phía sau thì lấy dòng ngay trước
                found.extend(money(lines[i - 1]))
        candidates.extend(a for a in found if a >= _MIN_AMOUNT)
    return max(candidates) if candidates else None


def _total_with_source(lines: list[str]) -> tuple[int | None, str]:
    """(tổng tiền, nguồn): 'label' nếu tìm thấy nhờ nhãn Tổng/Phải trả, 'repeat' nếu chỉ đoán theo số lặp, 'none'."""
    # Số khách thực trả ("phải trả", "cần thanh toán") ưu tiên hơn tổng tiền hàng (chưa trừ giảm giá/cộng phí).
    found = _keyword_total(lines, _PAYABLE_KEYWORDS)
    if found is not None:
        return found, "label"
    weak = _keyword_total(lines, _TOTAL_KEYWORDS, skip_subtotal=True) or _keyword_total(lines, _TOTAL_KEYWORDS)
    # Tổng thường lặp lại (tổng tiền, cần thanh toán, phương thức thanh toán), nhất là khi nhãn bị OCR đọc hỏng
    # hoặc đứng cạnh các dòng 0. Nhãn "Tổng" chung chung chỉ thắng nếu không có số lặp nào lớn hơn nó.
    # ponytail: lấy số lớn nhất xuất hiện >= 2 lần; sai nếu hóa đơn chỉ in tổng một lần.
    counts: dict[int, int] = {}
    for line in lines:
        for a in _amounts(line):
            if a >= _MIN_AMOUNT:
                counts[a] = counts.get(a, 0) + 1
    repeated = [a for a, n in counts.items() if n >= 2]
    top_repeated = max(repeated) if repeated else None
    if weak is not None and (top_repeated is None or weak >= top_repeated):
        return weak, "label"
    return (top_repeated, "repeat") if top_repeated is not None else (None, "none")


def extract_total(lines: list[str]) -> int | None:
    return _total_with_source(lines)[0]


MIN_RECEIPT_SCORE = 5        # dưới mức này: ảnh không giống hóa đơn
CONFIDENT_RECEIPT_SCORE = 6  # đúng bằng MIN: giống nhưng chưa chắc, vẫn nhận và nhắc kiểm tra
_RECEIPT_VOCAB = (
    "hoa don", "phieu", "tong", "thanh toan", "thanh tien", "don gia", "so luong", "vat", "ma so thue",
    "tien mat", "chiet khau", "khuyen mai", "cam on", "total", "subtotal", "cash", "receipt", "invoice",
    "tien khach", "phai tra", "diem tich", "ban le",
)


def receipt_score(lines: list[str]) -> int:
    """Điểm "giống hóa đơn" từ chữ OCR: nhiều dòng có giá tiền, có nhãn Tổng/Phải trả, có từ vựng hóa đơn.

    ponytail: luật chấm điểm chỉnh theo 9 hóa đơn thật và vài mẫu không phải hóa đơn (thực đơn, áp phích,
    căn cước...); cần thêm ảnh thật không phải hóa đơn để chỉnh lại ngưỡng.
    """
    amount_lines = sum(1 for t in lines if any(a >= _MIN_AMOUNT for a in _amounts(t)))
    score = (2 if amount_lines >= 2 else 0) + (1 if amount_lines >= 8 else 0)
    _, source = _total_with_source(lines)
    score += {"label": 3, "repeat": 1, "none": 0}[source]  # thực đơn có giá lặp nhưng không có nhãn Tổng
    norm_text = " ".join(_norm(t) for t in lines)
    hits = sum(1 for v in _RECEIPT_VOCAB if v in norm_text)
    score += (2 if hits >= 2 else 0) + (1 if hits >= 4 else 0)
    return score + (1 if len(lines) >= 6 else 0)


_TITLE_WORDS = ("phieu", "hoa don", "thanh toan")


def _brand_span(line: str, brand: str) -> str:
    """Dòng tiêu đề kiểu "PHIẾU THANH TOÁN BÁCH HÓA XANH": chỉ lấy đoạn ngắn nhất chứa tên thương hiệu."""
    if not any(w in _norm(line) for w in _TITLE_WORDS):
        return line
    words = line.split()
    for size in range(1, len(words) + 1):
        for start in range(len(words) - size + 1):
            span = " ".join(words[start:start + size])
            if _brand_in(brand, _norm(span)):
                return span
    return line


# Nhãn bên bán ("Tên NPP: ...") cho ra tên cửa hàng; nhãn bên mua ("Tên khách hàng: ...") thì không.
_SELLER_LABELS = (("npp",), ("ncc",), ("nha", "cung", "cap"), ("don", "vi", "ban"), ("cua", "hang"), ("nguoi", "ban"))
_BUYER_PREFIXES = ("ten khach hang", "ten kh", "khach hang", "nguoi mua", "nguoi nhan", "dia chi", "dja chi", "dien giai")
_BUYER_LABELS = ("khach", "khachhang", "nguoimua", "tenkh", "nguoinhan")  # nhãn đứng riêng một dòng, giá trị ở dòng sau
_CODE_TOKEN = re.compile(r"^[A-Za-z]{1,4}\d{4,}$")  # PN007415, SP008236...
# Dòng địa chỉ (có tên thành phố / bắt đầu bằng "Số 32"...) không phải tên cửa hàng
_CITY_MARKERS = ("tphcm", "hchiminh", "hochiminh", "tphn", "hanoi", "thanhphoho", "cantho", "danang", "haiphong")
_GLUED_CODE = re.compile(r"[A-Za-z]{1,4}\d{5,}")  # IN042620, s6IN042620: mã chứng từ dính liền
# Từ vựng chung của hóa đơn: dòng chủ yếu gồm các từ này không phải tên cửa hàng ("Cảm ơn quý khách", "Tổng thành tiền")
_GENERIC_WORDS = {"cam", "on", "quy", "khach", "tong", "cong", "thanh", "toan", "tien", "don", "gia", "luong", "vat",
                  "chiet", "khau", "hotline", "website", "email", "thoai", "nhan", "vien", "ngan", "ghi", "khuyen",
                  "phieu", "stt", "mon", "can", "phai", "dua", "thua"}  # chỉ từ chắc chắn là từ vựng hóa đơn (không có "hoa", "hang"...)


def _mostly_generic(line: str) -> bool:
    words = re.findall(r"[a-z]+", _norm(line))
    return bool(words) and sum(w in _GENERIC_WORDS for w in words) / len(words) >= 0.6


_FIELD_LABEL_PREFIXES = ("nvgh", "nvbh", "nvcv", "dtnpp", "dtddnpp", "dtgsnpp", "hoadonso", "hoaddnso", "hoddnso",
                         "hoadons", "ngayhoadon", "ngayhoaddn", "ngayhdn", "ngayin", "ngayddn", "ngay", "sdt", "mst",
                         "stt", "soct", "loaitien")
_ANY_DATE = re.compile(r"\d{1,2}\s*[/.\-]\s*\d{1,2}\s*[/.\-]\s*\d{2,4}")
_FIELD_VALUE = re.compile(r"^[^:]{1,25}:\s*\S")  # "NVBH: Phan Thi..." / "Hoa don so: IN042620"
_STATUS_PHRASES = ("da nhap hang", "da thanh toan", "da giao", "da huy", "hoan thanh", "dang xu ly", "cho xu ly",
                    "in store", "instore", "take away", "takeaway", "mang ve", "tai cho", "dine in", "pager")  # hình thức phục vụ in trên phiếu quán


def _looks_like_code_or_status(line: str) -> bool:
    """Mã chứng từ (PN007415) hoặc nhãn trạng thái ("Đã nhập hàng") không phải tên cửa hàng."""
    if any(_CODE_TOKEN.match(t) for t in line.split()) or _GLUED_CODE.search(line):
        return True
    chars = [c for c in line if not c.isspace()]
    if chars and sum(c.isdigit() for c in chars) / len(chars) >= 0.5:
        return True
    if _ANY_DATE.search(line) or _FIELD_VALUE.match(line) or "_" in line:
        return True  # ngày tháng, "Nhãn: giá trị" (Hóa đơn số: ..., NVBH: ...), hoặc mã có gạch dưới (Q9_2-Q92)
    norm = _norm(line)
    squeezed = _alnum(norm)  # OCR hay viết liền không dấu cách: "TenKH99993554CoCoMart"
    if any(squeezed.startswith(p) for p in _FIELD_LABEL_PREFIXES):
        return True  # nhãn trường thông tin bị OCR viết liền, mất dấu hai chấm: "NVGHQ9...", "Hóa ddn s6IN042620"
    if any(c in squeezed for c in _CITY_MARKERS) or re.match(r"^so\d", squeezed):
        return True  # địa chỉ
    return (any(norm.startswith(p) for p in _STATUS_PHRASES)
            or any(squeezed.startswith(_alnum(p)) for p in _BUYER_PREFIXES))


def _looks_like_person(line: str) -> bool:
    """"HO THI HOANG NGA": họ + đệm "Thị/Văn" -> tên người (thu ngân, chủ hộ), không phải tên cửa hàng."""
    words = _norm(line).split()
    return 3 <= len(words) <= 5 and words[0] != "sieu" and (words[1].startswith("thi") or words[1] == "van")  # họ có thể dính chữ rác ("InNGUYEN")


_ADDRESS_START = re.compile(r"^(?:[a-z]\d+|\d+[a-z]?)(?:\s*[-/,]\s*\d+[a-z]?)*\s+[a-z]")  # "H472 - 85 Hoang Huu Nam", "185 Hoang Huu Nam"


def looks_like_address(line: str | None) -> bool:
    """Dòng bắt đầu bằng số nhà (kiểu "185 Hoàng Hữu Nam", "H472 - 85 ..."): là địa chỉ, không phải tên cửa hàng."""
    return bool(line and _ADDRESS_START.match(_norm(line).strip()))


def _seller_label_name(line: str) -> str | None:
    """"Tên NPP: Công ty ..." -> "Công ty ..." (chịu lỗi OCR một ký tự ở chữ "Tên": "T@n")."""
    tokens = line.split()
    if len(tokens) < 3:
        return None
    first = tokens[0]
    if not (_within_one_edit(_alnum(_norm(first)) or "?", "ten") or (len(first) <= 4 and first[0] in "Tt")):  # "T@a": OCR sai cả "ê" lẫn "n"
        return None
    for words in _SELLER_LABELS:
        k = len(words)
        if [_norm(t) for t in tokens[1:1 + k]] == list(words):
            rest = " ".join(tokens[1 + k:]).lstrip(": ").strip()
            if sum(c.isalpha() for c in rest) >= 3:
                return rest
    return None


_TABLE_HEADERS = {"ttien", "thanhtien", "dongia", "sanpham", "soluong", "dvt", "stt", "sl", "tensp", "tenhang", "dongia"}  # tiêu đề cột ("T.Tiền")
_POLICY_LINE = ("doitra", "camon", "hangdamua", "khongdoi", "baohanh")  # dạng viết liền: dòng chính sách/lời cảm ơn, không phải tên cửa hàng
_NOT_NAME_WORDS = ("dia chi", "tel", "mst", "ma so thue", "dien thoai", "email", "website", "duong", "phuong",
                   "quan", "thanh pho", "viet nam", "chi minh", "hotline", "fax")


def _join_company_name(lines: list[str], confs: list[float], i: int, line: str) -> str:
    """Tên công ty trên con dấu hay bị OCR chia nhiều dòng ("CÔNG TY TNHH TM DV XNK" / "THỰC PHẨM SẠCH"):
    nối các dòng IN HOA liền sau, dừng ở dòng địa chỉ/liên hệ/mã số."""
    if not any(w in _alnum(_norm(line)) for w in ("congty", "cty")):
        return line
    parts = [line]
    for j in (i + 1, i + 2):
        if j >= len(lines) or confs[j] < _MIN_NAME_CONFIDENCE:
            break
        nxt = lines[j].strip()
        norm = _norm(nxt)
        if (nxt != nxt.upper() or sum(c.isalpha() for c in nxt) < 3 or any(c.isdigit() for c in nxt)
                or any(w in norm for w in _NOT_NAME_WORDS) or _looks_like_code_or_status(nxt)
                or any(_alnum(m) in _alnum(norm) for m in _TITLE_MARKERS)):  # dòng tiêu đề không thuộc tên công ty
            break
        parts.append(nxt)
    return " ".join(parts)


def _brand_from_website(lines: list[str]) -> str | None:
    """Thương hiệu đã biết nằm trong tên miền ở dòng website/email (cho phép OCR sai một ký tự): "MMIETNAM" ~ "mmvietnam"."""
    for line in lines:
        low = line.lower()
        if not any(k in low for k in ("http", "www.", "@", ".com", ".vn")):
            continue
        for token in re.findall(r"[a-z0-9]{6,}", _norm(line)):
            for brand in BRAND_CATEGORIES:
                key = _alnum(brand)
                if len(key) >= 7 and (token == key or _within_one_edit(key, token)) and brand not in WHOLE_WORD_BRANDS:
                    return brand
    return None


def _join_logo_lines(lines: list[str], confs: list[float], i: int, line: str, markers: list[str]) -> str:
    """Logo/tên chia hai dòng IN HOA liền nhau ("NHÀ TRỌ" / "SẠCH SẼ") thì ghép lại; không ghép dòng tiêu đề, địa chỉ, có số."""
    def plain_caps(text: str, max_words: int = 4) -> bool:
        words = text.split()
        return text == text.upper() and 1 <= len(words) <= max_words and not any(c.isdigit() for c in text) and sum(c.isalpha() for c in text) >= 3

    # "ĐẠI LÝ NƯỚC TINH KHIẾT" (loại hình, dài hơn 4 từ) đứng trên tên riêng "HẢI NAM"
    first_words = 6 if _norm(line).startswith("dai ly") else 4
    if i + 1 >= len(lines) or not plain_caps(line.strip(), first_words) or confs[i + 1] < _MIN_NAME_CONFIDENCE:
        return line
    nxt = lines[i + 1].strip()
    norm = _norm(nxt)
    # "duong" chỉ là địa chỉ khi đứng đầu dòng ("Đường Lê Lợi"); "HƯỞNG DƯƠNG" là tên riêng
    not_name = any(w in norm for w in _NOT_NAME_WORDS if w != "duong") or norm.startswith("duong ")
    if (not plain_caps(nxt) or _mostly_generic(nxt) or _looks_like_code_or_status(nxt) or not_name
            or any(m in _alnum(norm) for m in markers)):
        return line
    return f"{line.strip()} {nxt}"


_VENUE_ONLY = {"coffee", "cafe", "tea", "shop", "store", "restaurant", "quan", "nha", "hang", "bar", "pub", "market", "mart"}


def extract_merchant(lines: list[str], confidences: list[float] | None = None) -> str | None:
    """Như _extract_merchant nhưng không nhận tên chỉ gồm một từ loại hình ("COFFEE" còn sót lại khi logo bị đọc mất): trả None để app hỏi."""
    name = _fix_q_misread(_strip_greeting(_extract_merchant(lines, confidences)))
    return None if name and _alnum(_norm(name)) in _VENUE_ONLY else name


_OU_WORD = re.compile(r"^[Oo][Uu][EAYeayÊêÂâ]\w*$")


def _fix_q_misread(name: str | None) -> str | None:
    """Chữ "Q" in đậm ở logo hay bị đọc thành "O": "OUE", "OUAN", "OUY" không phải âm tiết tiếng Việt (chỉ có "QUE", "QUAN", "QUY"), nên "OU"+nguyên âm đầu từ
    luôn là "QU". Chỉ sửa chữ cái đầu của từ như vậy; từ khác ("OUTLET") giữ nguyên."""
    if not name:
        return name
    return " ".join(("Q" if w[0] == "O" else "q") + w[1:] if _OU_WORD.match(w) else w for w in name.split())


def looks_garbled_name(name: str | None) -> bool:
    """Tên cửa hàng đọc nát: chữ thường lẫn giữa dòng IN HOA ("GIA iNH XANH" khi OCR mất chữ "Đ") là lỗi đọc, không phải cách viết thật.

    Bắt hai dạng: từ gồm một chữ thường rồi từ hai chữ hoa trở lên ("iNH"), và một chữ thường đứng riêng giữa các từ IN HOA ("GIA i NH")."""
    words = (name or "").split()
    if any(re.fullmatch(r"[a-z][A-Z]{2,}", w) for w in words):
        return True
    caps = sum(1 for w in words if len(w) >= 2 and w.isalpha() and w.isupper())
    return caps >= 2 and any(len(w) == 1 and w.isalpha() and w.islower() for w in words)


def _strip_greeting(name: str | None) -> str | None:
    """"NGÔ GIA XIN CHÀO" (lời chào in kèm tên, trái tim hai bên bị đọc thành "V"): bỏ từ "xin chào" trở đi; không còn gì thì None để app hỏi."""
    if not name:
        return name
    norm = _norm(name)
    squeezed = _alnum(norm)
    if "xinchao" not in squeezed:
        return name
    pos = norm.find("xin chao")
    if pos < 0:  # dính liền "NGOGIAXINCHAOV": _norm giữ nguyên độ dài ký tự nên vị trí khớp với tên gốc
        pos = norm.find("xinchao")
    cut = name[:pos].strip(" -.,:;*♥❤")
    return cut or None


def _extract_merchant(lines: list[str], confidences: list[float] | None = None) -> str | None:
    """Tên cửa hàng: thương hiệu đã biết > từ chỉ loại hình > tên miền website > dòng chữ rõ đầu tiên.

    Bỏ dòng đọc kém (chữ viết tay) và dòng nhãn kết thúc bằng ':' (vd. "Cửa hàng:").
    """
    confs = confidences if confidences is not None else [1.0] * len(lines)
    usable = [
        (i, line.strip()) for i, (line, c) in enumerate(zip(lines, confs))
        if c >= _MIN_NAME_CONFIDENCE and sum(ch.isalpha() for ch in line) >= 3
        and not line.strip().endswith(":") and not _looks_like_code_or_status(line)
        and not _looks_like_person(line)
        and not any(w in _alnum(_norm(line)) for w in _POLICY_LINE)
        and _alnum(_norm(line)) not in _TABLE_HEADERS
        and not (i and lines[i - 1].strip().endswith(":") and _alnum(_norm(lines[i - 1])) in _BUYER_LABELS)
    ]
    for i, line in enumerate(lines[:15]):  # nhãn bên bán là tín hiệu rõ nhất ("Tên NPP: ...")
        if confs[i] >= _MIN_LABEL_CONFIDENCE and (name := _seller_label_name(line)):
            return name
    # Tên cửa hàng nằm trên dòng tiêu đề ("PHIẾU TÍNH TIỀN"...); các dòng dưới đó như "Đơn hàng siêu thị" chỉ là mô tả.
    # So cả dạng viết liền ("PHIEUGIAONHANTHANHTOAN") vì OCR hay mất dấu cách.
    markers = [_alnum(m) for m in _TITLE_MARKERS]
    cut = next((i for i, l in enumerate(lines) if any(m in _alnum(_norm(l)) for m in markers)), len(lines))
    # Từ dòng tiêu đề bảng món ("STT", "Tên hàng") trở xuống là tên hàng, không phải tên cửa hàng: "Bút bi Thiên Long" không làm cửa hàng là Thiên Long.
    table = next((i for i, l in enumerate(lines) if _alnum(_norm(l)) in _TABLE_HEADERS or _alnum(_norm(l)).startswith("stt")), len(lines))
    cut = min(cut, table)
    pool = [(i, l) for i, l in usable if i < cut] or usable
    # Website in cuối bill ("www.giadinhxanh.vn") xác nhận dòng nào ở đầu bill là TÊN: dòng công ty ("... GIA iNH XANH") thường đọc nát hơn dòng logo ("GIA ĐÌNH XANH").
    stems = {m.group(1).replace("-", "") for l in lines if (m := _SITE_RE.search(_norm(l)))}
    for _, line in pool[:12]:
        squeezed = _alnum(_norm(line))
        if not any(m in squeezed for m in markers) and any(squeezed == st or (len(st) >= 6 and (squeezed.startswith(st) or squeezed.endswith(st))) for st in stems):
            return line  # dòng tiêu đề ("PHIẾU THANH TOÁN BÁCH HÓA XANH") không phải tên: để bước thương hiệu cắt lấy đoạn tên
    for idx, line in pool[:12]:  # thương hiệu đã biết: loại được chữ nền (bàn phím, bàn...) ở đầu ảnh
        brand = next((b for b in BRAND_CATEGORIES if _brand_in(b, _norm(line))), None)
        if brand and "www." not in line.lower() and "@" not in line:  # dòng email/website để nhánh tên miền trả về tên thương hiệu, không trả cả địa chỉ email
            span = _brand_span(line, brand)
            above = lines[idx - 1].strip() if idx and confs[idx - 1] >= _MIN_NAME_CONFIDENCE else ""
            if span == line and any(g in _norm(above) for g in _GENERIC_STORE):  # logo hai dòng "BÁCH HÓA MINI" / "AN KHANG": giữ cả loại hình
                return _join_logo_lines(lines, confs, idx - 1, above, markers)
            return span
    for i, line in pool[:12]:
        norm, squeezed = _norm(line), _alnum(_norm(line))  # so cả dạng viết liền ("ChiNhanhTongCongTy...")
        # dòng chỉ có đúng từ chỉ loại hình ("Chi nhánh") là nhãn trường, không phải tên
        if "www." not in line.lower() and any((w in norm or _alnum(w) in squeezed) and len(squeezed) - len(_alnum(w)) >= 3
                                              for w in _BUSINESS_WORDS):
            return _join_company_name(lines, confs, i, line.strip())
    brand_site = _brand_from_website(lines)  # chữ đầu phiếu bị đọc nát nhưng dòng website vẫn còn ("einvoice.mmvietnam.com")
    if brand_site:
        return brand_site
    for line in lines:
        site = _SITE_RE.search(_norm(line))
        if site:
            return site.group(1)
    for line in lines[:3]:  # dòng đầu "TÊN-ngày giờ": tên cửa hàng/mã điểm bán in kèm thời điểm in phiếu
        m = _HEADER_DATE.match(line.strip())
        if m and not _mostly_generic(m.group(1)):
            return m.group(1)
    # Nếu tiêu đề nằm ngay đầu (cut == 0) thì tên nằm sau nó; bỏ chính dòng tiêu đề khỏi ứng viên.
    # Dòng bắt đầu bằng chữ thường là mảnh bị cắt giữa chữ ("uong Mai Sai Gon"), không phải tên.
    after_title = [(i, l) for i, l in pool
                   if not any(m in _alnum(_norm(l)) for m in markers)
                   and next((c for c in l if c.isalpha()), "A").isupper()
                   and not _mostly_generic(l)
                   and not (i + 1 < len(lines) and not any(c.isalpha() for c in lines[i + 1]) and _amounts(lines[i + 1]))]  # dòng ngay trên một số tiền là dòng hàng, không phải tên
    for limit in (5, 8):  # ảnh chụp màn hình app thường có mã phiếu, ngày, trạng thái trước tên đối tác
        for i, line in after_title:
            if i < limit:
                return _join_logo_lines(lines, confs, i, line, markers)
    return None


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
               "burger", "ga ran", "khoai chien", "khoai tay chien", "pho bo", "pho ga", "com suon", "bun cha", "bun rieu", "ca phe sua", "bac xiu",
               # học từ 12 bill ảnh 2026-10-14: "răng" (nha khoa) khớp nhầm "bắp rang", chặn luôn AI cho cả bill
               "bap rang", "bap rang bo", "canh chua", "rau muong", "xao toi"),
    "groceries": ("gao", "nuoc mam", "dau an", "duong cat", "sua tuoi", "trung ga", "thit heo", "thit bo",
                  "thit ga", "bap bo", "bo vien", "suon heo", "rau", "mi goi", "banh keo", "snack",
                  "nuoc ngot", "bia", "xuong ga", "sua bot", "ta quan", "ta dan", "dau hao", "tuong ot", "nuoc tuong", "hat nem", "gia vi",
                  "bot ngot", "bot canh", "bot chien", "mayonnaise", "nuoc cham",
                  # hàng tiêu dùng, thực phẩm tươi/đóng gói thường gặp ở tạp hóa, đại lý
                  "nuoc suoi", "ca chua", "xuc xich", "ca basa",
                  "banh mi sandwich", "banh mi gon", "mi ly", "hao hao"),
    "healthcare": ("paracetamol", "vitamin", "augmentin", "voltaren", "khau trang", "gac y te", "kim luon",
                   "natri clorid", "thuoc", "amoxicillin", "oresol",
                   "nieng rang", "tram rang", "cao voi rang", "nho rang", "rang su",
                   "kinh can", "kinh mat", "trong kinh", "gong kinh",
                   "ho so y te", "bang ca nhan", "nuoc muoi sinh ly", "muoi sinh ly", "xet nghiem", "phi kham", "kham noi"),
    "transport": ("xang", "dau diesel", "ve xe", "gui xe", "do xe", "sua xe", "va lop", "thay nhot", "rua xe", "cau duong", "ve tau", "ve may bay", "cuoc taxi", "cuoc xe"),
    # vé và dịch vụ vui chơi
    "entertainment": ("ve xem phim", "vexem phim", "xem phim", "ve phim", "bap nuoc", "ve vao cong", "ve tham quan", "ve cong vien", "cong vien nuoc", "khu vui choi",
                      "may choi game", "nap the game", "bowling", "karaoke", "ve ca nhac", "ve concert", "ve kich", "truot bang", "gio choi", "ve nguoi lon", "ve tre em", "the game"),
    # dịch vụ làm đẹp/cá nhân (không có nhóm riêng nên là "other"): spa, salon, cắt tóc, nail, mi, triệt lông
    "other": ("cat toc", "goi dau thu gian", "nhuom toc", "uon toc", "lam mong", "noi mi", "triet long", "tam trang", "xong hoi", "cao rau"),
    # phiếu mua/bán vàng bạc thường không có logo: nhận qua tên hàng
    "shopping": ("vang 9999", "vang 999", "vang 24k", "vang 18k", "nhan tron", "nhan cuoi", "nhan vang", "kim cuong", "vang mieng",
                 # hóa đơn mỹ phẩm không logo (siêu thị/nhà thuốc nhiều dòng khác nên danh mục của họ vẫn thắng)
                 "bot giat", "nuoc rua chen", "giay ve sinh", "nuoc xa vai",  # đồ dùng gia đình tiêu hao là shopping, không phải thực phẩm
                 "khan uot", "khan giay", "kem danh rang", "ban chai danh rang", "dau goi", "sua tam", "sua tam trang",  # đồ vệ sinh cá nhân dùng một lần
                 "sua rua mat", "tay trang", "kem chong nang", "son moi", "mat na", "nuoc hoa",
                 # quần áo, giày dép, túi xách, đồ điện tử nhỏ
                 "quan jean", "quan tay", "quan short", "ao thun", "ao khoac", "ao so mi", "ao polo", "dam cong so", "dam du tiec", "chan vay",
                 "tui xach", "balo", "that lung", "kinh ram", "mu non", "giay the thao", "giay da nam", "giay da nu", "giay cao got", "giay sandal",
                 "dep sandal", "dep quai", "dep tong", "tat nam", "tai nghe", "sac du phong", "cap sac", "op lung", "noi chien", "may say toc", "ban phim",
                 # văn phòng phẩm ("bìa nút" không phải bia, "thước kẻ" không phải thuốc) và đồ vệ sinh nam
                 "bia nut", "bia ho so", "thuoc ke", "thuoc 20cm", "thuoc 30cm", "but bi", "so tay", "vo ke", "romano"),
    "housing": ("tien dien", "tien nuoc", "tien nha", "tien thue nha", "tien thue phong", "tien rac", "tien gas", "binh gas", "phi quan ly chung cu", "phi quan ly", "phi chung cu"),
    "utilities": ("cuoc internet", "cuoc dienthoai", "nap dienthoai", "tien dienthoai", "nap data", "truyen hinh cap", "wifi", "goi cuoc",
                  "goi data", "cuoc di dong", "cap quang"),  # "tien dienthoai" (xem _category_from_products) không bị "tien dien" (nhà ở) khớp nhầm
}


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
    # "BÁCH HÓA MINI AN KHANG": tên cửa hàng ghi rõ loại hình bách hóa/tạp hóa thì đó là tạp hóa, dù đoạn sau trùng tên một chuỗi khác (nhà thuốc An Khang).
    if matches and any(g in merchant_norm for g in _GENERIC_STORE) and BRAND_CATEGORIES[best_brand(matches, merchant_norm)] != "groceries":
        return "groceries", "keyword", 0.75
    if matches:  # nhiều tên khớp (vd. "Supersports Aeon"): tên cụ thể hơn (dài hơn) thắng tên trung tâm thương mại
        return BRAND_CATEGORIES[best_brand(matches, merchant_norm)], "brand", 0.8
    # Logo ghi loại hình ở đầu phiếu ("KHU GIẢI TRÍ" / "HAPPY ZONE") trong khi tên cửa hàng chọn được lại là dòng công ty: với giải trí xét thêm 4 dòng đầu.
    head_norm = _norm(" ".join(lines[:4]))
    for category, keywords in _CATEGORY_KEYWORDS.items():
        if category == "groceries" and ("dien may" in merchant_norm or "noi that" in merchant_norm):
            continue  # "Siêu thị điện máy", "Siêu thị nội thất" bán đồ gia dụng/điện tử, không phải thực phẩm
        name_text = f"{merchant_norm} {head_norm}" if category == "entertainment" else merchant_norm
        if any(_has_keyword(name_text, k) for k in keywords):
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


def compute_coverage(lines: list[tuple[str, float]]) -> float:
    """Tỷ lệ dòng chữ (có >= 2 chữ cái) mà OCR đọc rõ (độ tin cậy >= 0.85).

    ponytail: xấp xỉ theo độ rõ chữ, chưa theo số tiền; cần tách dòng hàng (Hướng 2) mới chính xác.
    """
    text_lines = [c for t, c in lines if sum(ch.isalpha() for ch in t) >= 2]
    if not text_lines:
        return 0.0
    return round(sum(c >= _MIN_LINE_CONFIDENCE for c in text_lines) / len(text_lines), 2)


_WORD_ALIAS = {"uoi": "muoi", "tra": "tram"}  # OCR bỏ dấu và rớt chữ: "mươi" -> "uoi", "trăm" -> "tra" (chỉ có hiệu lực trong đoạn đọc số)
_DIGITS = {"mot": 1, "hai": 2, "ba": 3, "bon": 4, "tu": 4, "nam": 5, "lam": 5, "sau": 6, "bay": 7, "tam": 8, "chin": 9}
_ZERO_WORDS = {"khong", "linh", "le"}
_NUMBER_WORDS = set(_DIGITS) | _ZERO_WORDS | {"muoi", "moi", "tram", "nghin", "ngan", "trieu"}


def _words(text: str) -> list[str]:
    """Tách chữ thường không dấu; từ OCR dính nhau ("linhnamnghin") được tách lại nếu ghép trọn từ các từ chỉ số."""
    known = _NUMBER_WORDS | set(_WORD_ALIAS) | {"dong"}
    out = []
    for w in re.findall(r"[a-z]+", _norm(text)):
        best = [[]] + [None] * len(w)  # best[i] = cách tách ít mảnh nhất cho w[:i]
        for i in range(1, len(w) + 1):
            for j in range(i):
                if best[j] is not None and w[j:i] in known and (best[i] is None or len(best[j]) + 1 < len(best[i])):
                    best[i] = best[j] + [w[j:i]]
        out.extend(best[-1] if w not in known and best[-1] and len(best[-1]) >= 2 else [w])
    return out


def words_to_amount(text: str) -> int | None:
    """"Tám trăm bảy mươi ba nghìn đồng" -> 873000. Gặp chữ lạ (OCR hỏng) trong đoạn số thì trả None, không đoán."""
    words = _words(text)
    start = next((i for i, w in enumerate(words) if w in _NUMBER_WORDS), None)
    if start is None:
        return None
    millions = thousands = cur = pending = n = 0
    for w in words[start:]:
        w = _WORD_ALIAS.get(w, w)
        if w in ("dong", "d"):
            break
        if w in _DIGITS:
            pending = _DIGITS[w]
        elif w in _ZERO_WORDS:
            continue
        elif w in ("muoi", "moi"):  # OCR hay mất dấu "mươi" -> "moi"
            cur, pending = cur + (pending or 1) * 10, 0
        elif w == "tram":
            cur, pending = cur + pending * 100, 0
        elif w in ("nghin", "ngan"):
            thousands, cur, pending = cur + pending, 0, 0
        elif w == "trieu":
            millions, cur, pending = cur + pending, 0, 0
        else:
            return None
        n += 1
    return (millions * 1_000_000 + thousands * 1000 + cur + pending) if n >= 2 else None


def extract_words_total(lines: list[str]) -> int | None:
    """Số tiền bằng chữ in trên hóa đơn (có thể xuống dòng): thử từng dòng, rồi dòng đó nối dòng kế."""
    def n_words(s: str) -> int:
        return sum(w in _NUMBER_WORDS for w in _words(s))

    for i, line in enumerate(lines):
        if n_words(line) < 2:
            continue
        text = line
        for nxt in lines[i + 1:i + 4]:  # nối các dòng kế (bỏ dòng nhãn xen giữa) cho tới khi gặp chữ "đồng"
            if "dong" in _words(text):
                break
            if n_words(nxt) or "dong" in _words(nxt):  # dòng kế chỉ có chữ "đồng chẵn" cũng là phần của câu
                text += " " + nxt
        if "dong" in _words(text) and (v := words_to_amount(text)) is not None:
            return v
    return None


_NUM_TOKEN = re.compile(r"\d{1,3}(?:[.,]\d{3})+(?!\d)|\d+[.,]\d{1,2}(?!\d)|\d+")


def _num_value(tok: str) -> float:
    if re.fullmatch(r"\d{1,3}(?:[.,]\d{3})+", tok):
        return float(re.sub(r"\D", "", tok))   # "23,700" -> 23700
    return float(tok.replace(",", "."))           # "1,5" -> 1.5 (số lượng thập phân)


def items_cross_check(lines: list[str]) -> dict:
    """Tổng các dòng hàng, nhận ra dòng hàng bằng quy tắc số lượng x đơn giá = thành tiền (ba số liên tiếp).

    Cách này không phụ thuộc từ khóa nên là lưới an toàn độc lập với việc chọn dòng "Tổng". Bỏ qua dòng tài khoản/ngân hàng.
    Không có dòng hàng nào kiểm chứng được thì items_sum = None. ponytail: chỉ nhận dòng có đủ cả số lượng và đơn giá.
    """
    tokens = []
    for line in lines:
        if _ACCOUNT_LINE.search(_norm(line)) or _DATE_OR_TIME.search(line):
            continue
        tokens.extend(_NUM_TOKEN.findall(line))
    values = [_num_value(t) for t in tokens]
    amounts, i = [], 0
    while i + 2 < len(values):
        q, price, amount = values[i], values[i + 1], values[i + 2]
        if q > 0 and price >= _MIN_AMOUNT and amount >= _MIN_AMOUNT and abs(q * price - amount) <= max(1.0, amount * 0.005):
            amounts.append(int(amount))
            i += 3  # dòng hàng này đã dùng hết ba số, không tính lặp
        elif q > 0 and price >= _MIN_AMOUNT and amount >= _MIN_AMOUNT and abs(q * amount - price) <= max(1.0, price * 0.005):
            amounts.append(int(price))  # OCR đọc cột thành tiền trước cột đơn giá: SL, thành tiền, đơn giá
            i += 3  # dòng hàng này đã dùng hết ba số, không tính lặp
        else:
            i += 1
    return {"items_sum": sum(amounts) if amounts else None, "items_count": len(amounts)}


def items_check_level(total: int | None, items_sum: int | None) -> str | None:
    """match: bằng nhau. mismatch: lệch phi lý. plausible: còn lại.

    Tổng các dòng hàng thường chỉ là tổng MỘT PHẦN (OCR đọc đủ số lượng/đơn giá/thành tiền ở vài dòng thôi), nên tổng đọc ra lớn
    hơn nhiều lần vẫn bình thường: đo trên 29 ảnh thật các ca đó lệch 1,8-7 lần, còn lỗi lấy nhầm số tài khoản lệch 452 lần.
    Vì vậy chỉ coi là mismatch khi tổng > 50 lần tổng dòng hàng, hoặc tổng nhỏ hơn 0,4 lần (các dòng hàng cộng lại đã vượt hẳn tổng).
    ponytail: ngưỡng 50 chỉnh theo 29 ảnh; nếu gặp tổng đúng mà lệch >50 lần (phiếu chỉ đọc được một dòng hàng nhỏ) thì nới.
    """
    if total is None or not items_sum:
        return None
    if total == items_sum:
        return "match"
    return "plausible" if 0.4 <= total / items_sum <= 50 else "mismatch"


def analyze(
    lines: list[tuple[str, float]],
    lookup: Callable[[str | None], str | None] | None = None,
    predict: Callable[[str | None, list[str]], tuple[str, float]] | None = None,
    find_known: Callable[[list[str]], str | None] | None = None,
    forced_total: int | None = None,
) -> dict:
    """forced_total: tổng đã đọc theo HÀNG (items.payable_total), đáng tin hơn luật theo thứ tự dòng; có thì thay luôn tổng và các cờ tính từ tổng."""
    texts = [t for t, _ in lines]
    confidence = sum(c for _, c in lines) / len(lines) if lines else 0.0
    # Tên cửa hàng người dùng đã từng chốt, nằm ở bất kỳ dòng nào, được ưu tiên hơn việc đoán theo vị trí.
    merchant = (find_known(texts) if find_known else None) or extract_merchant(texts, [c for _, c in lines])
    total = forced_total if forced_total is not None else extract_total(texts)
    words_total = extract_words_total(texts)
    words_check = None if words_total is None else ("match" if words_total == total else "mismatch")
    total_from_words = total is None and words_total is not None
    if total_from_words:
        total = words_total  # chỉ có số tiền bằng chữ đọc được; vẫn nhắc kiểm tra
    cross = items_cross_check(texts)
    total_source = "text"
    if total is None and cross["items_count"] >= 2:
        total, total_source = cross["items_sum"], "items"  # không có dòng tổng đọc được: dùng tổng các dòng hàng, vẫn nhắc kiểm tra
    items_check = items_check_level(total, cross["items_sum"]) if total_source == "text" else None
    category, source, category_conf = classify_category_detailed(
        merchant, texts, remembered=lookup(merchant) if lookup else None, predict=predict
    )
    coverage = compute_coverage(lines)
    return {
        "merchant": merchant,
        "total_amount": total,
        "category": category,
        "category_source": source,
        "category_confidence": category_conf,
        "coverage": coverage,
        "words_check": words_check,
        "items_sum": cross["items_sum"],
        "items_count": cross["items_count"],
        "items_check": items_check,
        "total_source": total_source,
        "confidence": round(confidence, 2),
        "needs_review": (
            merchant is None
            or looks_garbled_name(merchant)  # tên đọc nát ("GIA iNH XANH"): không được im lặng
            or (looks_like_address(merchant) and source not in ("brand", "memory"))  # phiếu không in tên: tên đang là địa chỉ
            or total is None
            or words_check == "mismatch"
            or total_from_words
            or total_source == "items"
            or items_check == "mismatch"
            or confidence < _MIN_OCR_CONFIDENCE
            or category_conf < _MIN_CATEGORY_CONFIDENCE
            or coverage < _MIN_COVERAGE
        ),
        "raw_lines": texts,
    }
