"""Bỏ/che dữ liệu cá nhân trong chữ OCR trước khi lưu dataset hoặc trả về app.

Nguyên tắc: chỉ giữ thứ cần để học danh mục (tên cửa hàng, tên sản phẩm, số tiền, ngày).
Bỏ cả dòng nhãn cá nhân (khách hàng, người nhận, nhân viên, thu ngân, địa chỉ, số tài khoản, điện thoại) kèm dòng giá trị
bên dưới nếu nhãn đứng riêng, che email, số điện thoại, số tài khoản/số dài.
Tên người đứng riêng một dòng (cột giá trị của nhãn bị OCR tách rời) chỉ bị bỏ khi nằm ngay sau nhãn liên hệ hoặc ngay trước nhãn địa chỉ
(nhãn cho phép OCR sai chữ: "Sg dién thoal"); tên người không có nhãn nào bên cạnh thì không nhận ra, để khỏi bỏ nhầm tên cửa hàng và món.
Địa chỉ: bỏ dòng có dạng "số nhà, phường/quận/TP" (dấu phẩy + tên đơn vị hành chính hoặc thành phố), dòng chỉ còn tên thành phố, và dòng số nhà
nằm sát một dòng địa chỉ. Tên cửa hàng/món có chứa tên địa danh ("Bệnh viện quận 7", "Vé máy bay Hà Nội - TP.HCM", "Điện lực Hồ Chí Minh") được giữ vì không có dấu phẩy.
ponytail: dựa trên nhãn và mẫu số; thêm nhãn/mẫu ở đây khi gặp.
"""
import re

from parsing.extract import _alnum, _looks_like_person, _norm, looks_like_address

_LABELS = ("khachhang", "tenkhachhang", "tenkh", "makhachhang", "makh", "nguoimua", "nguoinhan", "hotennguoi", "hoten",
           "nhanvien", "nvbh", "nvgh", "nvcv", "thungan", "diachi", "djachi", "dienthoai", "hotline",
           "sotaikhoan", "taikhoan", "khach")
_SHORT_LABELS = ("nv", "dt", "dc", "tel", "stk", "sdt", "email")  # nhãn ngắn: chỉ tính khi theo sau không phải chữ cái ("Telecom" không phải nhãn)
_CONTACT_LABELS = ("dienthoai", "sodienthoai", "khachhang", "tenkhachhang", "tenkh", "nguoimua", "nguoinhan", "hoten")
_ADDRESS_LABELS = ("diachi",)
_EMAIL = re.compile(r"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")
_PHONE = re.compile(r"(?<!\d)(?:\+?84|0)[\s.\-]?\d{2,3}[\s.\-]?\d{3}[\s.\-]?\d{3,4}(?!\d)")
_LONG_RUN = re.compile(r"(?<!\d)\d[\d.\-]{8,}\d(?!\d)")  # số dài có dấu chấm/gạch (STK); không gồm khoảng trắng để không nuốt cột tiền
_LONG_DIGITS = re.compile(r"\d{9,}")


_CITY = re.compile(r"\b(?:ho chi minh|tp\.? ?hcm|tphcm|ha noi|da nang|can tho|hai phong|viet nam)\b")
_CITY_ONLY = re.compile(r"^(?:tp\.? ?hcm|tphcm|ho chi minh|ha noi|da nang|can tho|hai phong|viet nam)[\s.,]*$")
_CITY_AT_END = re.compile(r"(?:tp\.? ?hcm|tphcm)[\s.,]*$")  # dính liền sau số nhà, mất dấu phẩy: "8TP.HCM"
_ADMIN_WORD = re.compile(r"\b(?:phuong|quan|huyen|thanh pho|tinh|tp)\b")
_ADMIN_NUMBER = re.compile(r"(?<![a-z0-9])(?:phuong|quan|huyen|p|q)\.?\s?\d{1,2}(?!\d)")
_ROAD = re.compile(r"^\s*(?:so\s+)?\d+[a-z]?(?:/\d+[a-z]?)*\s+(?:duong|d\.|pho|ngo|hem|khu pho|ap|kp|to)\b")
_CODE_HOUSE = re.compile(r"^[a-z]\d+\s*-\s*\d+")  # "H472 - 85 Hoang Huu Nam"


def _is_address_line(line: str) -> bool:
    norm = _norm(line).strip()
    if _CITY_ONLY.match(norm) or len(_ADMIN_NUMBER.findall(norm)) >= 2 or _ROAD.match(norm):
        return True
    if _CITY_AT_END.search(norm) and re.match(r"^[^\d]{0,4}\d", norm):  # số nhà ngay đầu dòng + thành phố ở cuối; "Vé máy bay Hà Nội - TP.HCM" (kể cả OCR "Ha N8i") được giữ
        return True
    return "," in norm and bool(_CITY.search(norm) or _ADMIN_WORD.search(norm) or _ADMIN_NUMBER.search(norm))


def _address_lines(lines: list[str]) -> set[int]:
    strong = [_is_address_line(l) for l in lines]
    found = {i for i, hit in enumerate(strong) if hit}
    for i, line in enumerate(lines):  # dòng số nhà ("185 Hoang Huu Nam") nằm sát một dòng địa chỉ; "1 Chai" (một từ sau số) là món
        near = (i > 0 and strong[i - 1]) or (i + 1 < len(lines) and strong[i + 1])
        words_after_number = len(_norm(line).split()) - 1
        if near and (_CODE_HOUSE.match(_norm(line)) or (looks_like_address(line) and words_after_number >= 2)):
            found.add(i)
    return found


def _is_label_line(line: str) -> bool:
    norm = _norm(line).lstrip()
    if _edit_distance(_alnum(norm)[:6], "diachi") <= 1:  # "Dia chl20 Thanh Mau..." (OCR sai chữ i): nhãn địa chỉ kèm giá trị cùng dòng
        return True
    if any(norm.startswith(s) and not norm[len(s):len(s) + 1].isalpha() for s in _SHORT_LABELS):
        return True
    return any(_alnum(norm).startswith(label) for label in _LABELS)


def _edit_distance(a: str, b: str) -> int:
    row = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        prev, row[0] = row[0], i
        for j, cb in enumerate(b, 1):
            prev, row[j] = row[j], min(row[j] + 1, row[j - 1] + 1, prev + (ca != cb))
    return row[-1]


def _is_fuzzy_label(line: str, labels: tuple[str, ...]) -> bool:
    """Dòng chỉ gồm nhãn (OCR sai tối đa 1-2 chữ, có thể thừa 1-2 chữ rác đầu dòng): "Sg dién thoal" ~ "dienthoai"."""
    if any(c.isdigit() or c == "[" for c in line):
        return False  # nhãn kèm giá trị ("Điện thoại 090...", "[so-dai]") không phải dòng chỉ có nhãn
    text = _alnum(_norm(line))
    for label in labels:
        n, slack = len(label), 2 if len(label) >= 8 else 1
        if len(text) > n + 4:
            continue  # dòng dài hơn nhãn nhiều là câu/món, không phải nhãn
        if any(_edit_distance(label, text[k:k + size]) <= slack for k in range(3) for size in (n - 1, n, n + 1)):
            return True
    return False


def _looks_like_plain_name(text: str) -> bool:
    """"Vu Thanh Tung": 2-4 từ toàn chữ cái, mỗi từ viết hoa đầu (hoặc IN HOA hết), không số."""
    words = text.split()
    return 2 <= len(words) <= 4 and all(w.isalpha() and w[0].isupper() and (w[1:].islower() or w.isupper()) for w in words)


def _personal_name_lines(lines: list[str]) -> set[int]:
    names = set()
    for i, line in enumerate(lines):
        if not _looks_like_plain_name(line):
            continue
        after_contact = i > 0 and _is_fuzzy_label(lines[i - 1], _CONTACT_LABELS)
        before_address = i + 1 < len(lines) and _is_fuzzy_label(lines[i + 1], _ADDRESS_LABELS)
        if after_contact or before_address:
            names.add(i)
    return names


def _mask(line: str) -> str:
    line = _EMAIL.sub("[email]", line)
    line = _PHONE.sub("[sdt]", line)
    line = _LONG_RUN.sub(lambda m: "[so-dai]" if sum(c.isdigit() for c in m.group(0)) >= 10 else m.group(0), line)
    return _LONG_DIGITS.sub("[so-dai]", line)


def sanitize_lines(lines: list[str]) -> list[str]:
    out, skip_value = [], False
    stripped = [l.strip() for l in lines]
    dropped = _personal_name_lines(stripped) | _address_lines(stripped)
    for i, line in enumerate(lines):
        text = line.strip()
        if i in dropped:
            continue
        if skip_value:  # dòng giá trị đứng dưới nhãn riêng ("Người nhận:" / "Nguyễn Văn A")
            skip_value = False
            if not text.endswith(":") and not _is_label_line(text):
                continue
        if _is_label_line(text):
            skip_value = text.endswith(":")
            continue
        if _looks_like_person(text):
            continue
        masked = _mask(line)
        if re.sub(r"\[[a-z-]+\]", "", masked).strip(" :-.,"):  # dòng chỉ còn dấu "[email]/[sdt]" thì bỏ hẳn
            out.append(masked)
    return out
