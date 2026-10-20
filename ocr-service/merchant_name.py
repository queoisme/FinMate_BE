"""Chuẩn hóa tên cửa hàng cho đẹp trước khi lưu/hiển thị (tên OCR thô thường IN HOA, dính chữ, dính ký tự rác)."""
import re

from classification.brands import BRAND_CATEGORIES, BRAND_DISPLAY
from extract import _GENERIC_STORE, _alnum, _brand_in, _norm, best_brand

_ACRONYMS = {"TNHH", "CP", "JSC", "TP", "VN", "HCM", "HN", "KFC", "BBQ", "CGV", "EVN", "FPT", "MB", "TM", "DV"}
# Tiêu đề hóa đơn dính vào đầu tên; dài trước để khớp đúng cụm dài nhất.
_TITLE_PHRASES = sorted(
    ["hoa don ban le", "hoa don ban hang", "hoa don", "phieu thanh toan", "phieu tinh tien",
     "phieu thu", "phieu giao hang"],
    key=len, reverse=True,
)


def _strip_title(text: str) -> str:
    words = text.split()
    norm = _norm(text)
    for phrase in _TITLE_PHRASES:
        if norm.startswith(phrase):
            rest = words[len(phrase.split()):]
            if rest:
                return " ".join(rest)
    return text


_LEGAL_PREFIXES = ("cong ty", "cty", "tnhh", "ho kinh doanh", "dntn", "doanh nghiep tu nhan", "co phan",
                   "ty tnhh", "ty cp", "ty mtv", "ty co phan")  # "Công" đã bị mất: "Ty TNHH ..." vẫn là loại hình
# Từ loại hình + ngành nghề đứng đầu tên công ty, không phải tên riêng ("CÔNG TY TNHH TM DV XNK <tên riêng>")
_LEGAL_TOKENS = {
    "cong", "ty", "cty", "tnhh", "cp", "co", "phan", "mtv", "jsc", "ho", "kinh", "doanh", "dntn",
    "doanh", "nghiep", "tu", "nhan", "tm", "dv", "xnk", "sx", "tmdv", "thuong", "mai", "dich", "vu",
    "xuat", "nhap", "khau", "san", "va", "mot", "thanh", "vien",
}


_FORM_TOKENS = {"ty", "cty", "tnhh", "cp", "dntn", "jsc", "mtv", "doanh", "nhan", "phan"}  # từ chỉ loại hình hoàn chỉnh


_LEADING_JUNK = re.compile(r"^[iIlL|!](?=cong|CONG|Cong)")  # chữ thừa OCR đọc nhầm trước chữ "CÔNG"


def _split_glued_legal(word: str) -> list[str]:
    """Tách từ loại hình bị OCR dính liền: "TYTNHH" thành TY TNHH, "CONGTY" thành CONG TY, "TMDV" thành TM DV.

    Chỉ tách khi cả từ phân rã hoàn toàn thành các từ loại hình đã biết (để khỏi xé tên riêng).
    """
    norm = _norm(word)
    n = len(norm)
    best: list[list[int] | None] = [None] * (n + 1)   # best[i] = các điểm cắt của cách tách ngắn nhất phủ norm[:i]
    best[0] = []
    for i in range(1, n + 1):
        for j in range(i):
            if best[j] is not None and norm[j:i] in _LEGAL_TOKENS:
                candidate = best[j] + [i]
                if best[i] is None or len(candidate) < len(best[i]):
                    best[i] = candidate
    if n == 0 or best[n] is None or len(best[n]) < 2:
        return [word]
    cuts, start, parts = best[n], 0, []
    for end in cuts:
        parts.append(word[start:end])
        start = end
    # phải có một từ loại hình rõ ràng, nếu không "COCO" sẽ bị xé thành "CO" + "CO"
    if not any(_norm(p) in _FORM_TOKENS - {"nhan", "phan", "doanh"} for p in parts):
        return [word]
    return parts


def _strip_company_form(text: str) -> str:
    """Bỏ phần loại hình/ngành nghề ở đầu tên công ty, giữ tên riêng; nếu bỏ hết thì giữ nguyên."""
    text = _LEADING_JUNK.sub("", text)
    words = [part for w in text.split() for part in _split_glued_legal(w)]
    text = " ".join(words)
    if not any(_norm(text).startswith(p) for p in _LEGAL_PREFIXES):
        return text
    k = 0
    while k < len(words) and _norm(words[k]) in _LEGAL_TOKENS:
        # "phan" chỉ là loại hình ("cổ phần") khi đứng sau "co"; "Phân Phối", "Phan Thiết" là tên riêng
        if _norm(words[k]) == "phan" and (k == 0 or _norm(words[k - 1]) != "co"):
            break
        k += 1
    # Chỉ cắt khi đã bỏ được một loại hình hoàn chỉnh (ty, tnhh, cp, doanh...). Nếu OCR dính chữ
    # ("Cong Tytnhh ...") thì để nguyên, tránh cắt dở dang làm mất nghĩa.
    if not any(_norm(w) in _FORM_TOKENS for w in words[:k]):
        return text
    return " ".join(words[k:]) or text


def _pretty_word(word: str) -> str:
    if word.upper() in _ACRONYMS:
        return word.upper()
    if any(c.isdigit() for c in word) and any(c.isalpha() for c in word):
        return word.upper()  # kiểu "gs25"
    return word.capitalize()


def _brand_of_another_trade(matches: list[str], norm: str) -> bool:
    """"BÁCH HÓA MINI AN KHANG" trùng tên chuỗi nhà thuốc An Khang: tên cửa hàng ghi loại hình bách hóa/tạp hóa thì giữ tên của nó, không đổi sang tên chuỗi khác ngành."""
    return any(g in norm for g in _GENERIC_STORE) and BRAND_CATEGORIES[best_brand(matches, norm)] != "groceries"


def format_merchant_name(raw: str | None) -> str:
    """Tên chuẩn: thương hiệu đã biết -> tên có dấu; còn lại -> làm sạch và viết hoa đầu từ. Gọi lại nhiều lần vẫn ra cùng kết quả."""
    text = re.sub(r"(?<=[A-Z])['`´’](?=[A-Z])", "", raw or "")      # DU'O'NG -> DUONG
    text = re.sub(r"[^\w\s&.\-']", " ", text).replace("_", " ")
    text = re.sub(r"\s+", " ", text).strip(" .-'")
    if not text:
        return ""
    text = _strip_title(text) or text
    text = _strip_company_form(text)
    norm = _norm(text)
    matches = [b for b in BRAND_CATEGORIES if _brand_in(b, norm)]
    if matches and not _brand_of_another_trade(matches, norm):
        return BRAND_DISPLAY[best_brand(matches, norm)]  # tên cụ thể hơn thắng; hòa thì tên xuất hiện sớm hơn trong dòng
    text = re.sub(r"(?<=[A-Za-z])\.(?=[A-Za-z])", " ", text)        # THUC.PHAM -> THUC PHAM
    text = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", text)                # PizzaHut -> Pizza Hut
    if text == text.upper() or text == text.lower():                 # chỉ IN HOA hoặc chỉ thường: viết hoa đầu từ
        text = " ".join(_pretty_word(w) for w in text.split())
    return text
