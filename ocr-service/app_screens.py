"""Nhận ra ảnh chụp màn hình app (không phải hóa đơn giấy) và đọc đúng trường theo từng loại màn hình.

Hiện hỗ trợ ba loại, viết theo ảnh thật của người dùng:
  - ride: chi tiết chuyến xe công nghệ ("Mã chuyến đi", "Total")
  - service_payment: chi tiết giao dịch thanh toán hóa đơn dịch vụ ("Nhà cung cấp", số tiền lớn đầu trang)
  - order_list: danh sách đơn đã mua trên sàn TMĐT ("Đơn đã mua", mỗi đơn có "Tổng số tiền (N sản phẩm): ...")
ponytail: luật theo giao diện hiện tại của từng app; app đổi giao diện hoặc app khác thì cần thêm luật.
"""
import re

from brands import BRAND_CATEGORIES, BRAND_DISPLAY, WHOLE_WORD_BRANDS
from extract import _MAX_AMOUNT, _alnum, _amounts, _norm, best_brand

_SHOP_BADGE = {"yeu", "thich", "thich+", "+"}
_ORDER_TOTAL = re.compile(r"san\s*pham\)?\s*:?\s*([\d.,]+)")
_ONLY_AMOUNT = re.compile(r"^\s*[-+]?\s*\d[\d.,]*\s*\S?\s*$")


def _amount_in(text: str) -> int | None:
    found = _amounts(text)
    return found[0] if found else None


def _strip_badge(line: str) -> str:
    """"Yêu thích+ FURYCUBE LOCAL" -> "FURYCUBE LOCAL"."""
    words = line.split()
    while words and _norm(words[0]) in _SHOP_BADGE:
        words.pop(0)
    return " ".join(words)


def _order_total(norm_line: str) -> int | None:
    m = _ORDER_TOTAL.search(norm_line)
    if not m:
        return None
    digits = re.sub(r"\D", "", m.group(1))
    return int(digits) if digits and int(digits) <= _MAX_AMOUNT else None


def _parse_order_list(lines: list[str]) -> dict:
    norms = [_norm(l).strip() for l in lines]
    done = [i for i, n in enumerate(norms) if n.startswith("hoan thanh")]
    orders, incomplete = [], 0
    totals = [i for i, n in enumerate(norms) if _order_total(n) is not None]
    if totals and (not done or totals[0] < done[0]):
        incomplete += 1  # đơn đầu bị cuộn mất phần tên shop
    for k, h in enumerate(done):
        end = done[k + 1] if k + 1 < len(done) else len(lines)
        t = next((i for i in range(h + 1, end) if _order_total(norms[i]) is not None), None)
        shop = _strip_badge(lines[h - 1]) if h > 0 else ""
        if t is None or not shop:
            incomplete += 1  # đơn bị cắt: không có dòng tổng tiền hoặc không có tên shop
            continue
        product = lines[h + 1] if h + 1 < t else ""
        orders.append({"merchant": shop, "total_amount": _order_total(norms[t]), "product": product,
                       "lines": lines[h - 1:t + 1]})
    return {"screen_type": "order_list", "orders": orders, "incomplete_orders": incomplete, "category": "shopping"}


def _parse_ride(lines: list[str]) -> dict:
    norms = [_norm(l).strip() for l in lines]
    total = None
    for i, n in enumerate(norms):
        if n == "total":
            total = next((a for a in (_amount_in(l) for l in lines[i:i + 3]) if a), None)
            break
    # ponytail: màn hình này không in tên hãng, nên dùng tên chung để người dùng tự sửa nếu muốn
    return {"screen_type": "ride", "merchant": "Chuyến xe công nghệ", "total_amount": total, "category": "transport"}


_HOUSING_WORDS = ("dien luc", "evn", "tien dien", "tien nuoc", "cap nuoc", "nuoc sach", "chung cu", "nha tro", "phong tro", "thue nha", "tien nha")


def _bill_category(text: str) -> str:
    """Hóa đơn dịch vụ: điện, nước, gas, chung cư, thuê nhà -> "housing"; còn lại (internet, điện thoại, data) -> "utilities"."""
    norm = _norm(text)
    if any(w in norm for w in _HOUSING_WORDS) or re.search(r"(?<![a-z])gas(?![a-z])", norm):
        return "housing"
    return "utilities"


def _parse_service_payment(lines: list[str]) -> dict:
    norms = [_norm(l).strip() for l in lines]
    merchant = None
    for i, n in enumerate(norms):
        if n.startswith("nha cung cap"):
            same_line = " ".join(lines[i].split()[3:])  # "Nhà cung cấp" là 3 từ; giá trị thường ở dòng kế
            merchant = same_line or (lines[i + 1].strip() if i + 1 < len(lines) else None)
            break
    title = next((i for i, n in enumerate(norms) if "chi tiet giao dich" in n), 0)
    banner = lines[title + 1].strip() if title + 1 < len(lines) else ""
    if merchant and banner and _norm(banner).replace(" ", "") == _norm(merchant).replace(" ", ""):
        merchant = banner  # dòng tiêu đề IN HOA phía trên đọc đúng dấu hơn dòng "Nhà cung cấp"
    total = next((_amount_in(l) for l in lines[title + 1:title + 6] if _ONLY_AMOUNT.match(l) and _amount_in(l)), None)
    if merchant is None and title + 1 < len(lines):
        merchant = lines[title + 1].strip().title()  # dòng tên in hoa ngay dưới tiêu đề
    return {"screen_type": "service_payment", "merchant": merchant, "total_amount": total,
            "category": _bill_category(" ".join(x for x in (merchant, banner) if x))}


_TICKET_WORD = re.compile(r"ticke|icket|tickz")  # OCR hay đọc hỏng chữ TICKET in lặp làm nền
_CINEMA_WORDS = ("phong chieu", "chieu phim", "ve xem phim", "gold class", "galaxy cinema", "lotte cinema", "bhd star")
_THOUSANDS = re.compile(r"\d{1,3}(?:[.,]\d{3})+")


def _is_cinema_ticket(norms: list[str]) -> bool:
    tickets = sum(bool(_TICKET_WORD.search(n)) for n in norms)
    text = " | ".join(norms)
    if any(w in text for w in _CINEMA_WORDS) or re.search(r"(?<![a-z])cgv(?![a-z])", text):
        return tickets >= 1 or "the vao" in text
    return "the vao" in text and tickets >= 3  # "THE VÀO" + nền TICKET lặp lại, dù chữ khác bị mất


def _parse_cinema_ticket(lines: list[str]) -> dict:
    text = " | ".join(_norm(l) for l in lines)
    merchant = next((name for key, name in (("galaxy", "Galaxy Cinema"), ("lotte cinema", "Lotte Cinema"), ("bhd", "BHD Star"))
                     if key in text), None)
    if merchant is None and (re.search(r"(?<![a-z])cgv(?![a-z])", text) or "gold class" in text):
        merchant = "CGV"  # "Gold Class" là hạng phòng riêng của CGV
    amounts = [int(re.sub(r"\D", "", m)) for l in lines for m in _THOUSANDS.findall(l)]
    amounts = [a for a in amounts if 10_000 <= a <= 5_000_000]
    # ponytail: lấy số lớn nhất; vé nhiều dòng giá (vé + dịch vụ) thì số lớn nhất là tổng. Không đọc được giá thì để trống, không đoán
    return {"screen_type": "cinema_ticket", "merchant": merchant, "total_amount": max(amounts) if amounts else None,
            "category": "entertainment"}


_COURIER_SHOP = (("spx", "Shopee"), ("shopee", "Shopee"), ("lazada", "Lazada"), ("tiktok", "TikTok Shop"))


def _is_shipping_label(norms: list[str]) -> bool:
    """Cần ít nhất hai dấu hiệu độc lập (nhãn "Mã vận đơn" hay bị OCR đọc nát nên không dựa vào một chữ nào)."""
    text = " | ".join(norms)
    signals = [
        "van don" in text,                                              # "Mã vận đơn"
        "noi dung hang" in text,                                        # "Nội dung hàng"
        "tien thu" in text and "nhan" in text,                          # "Tiền thu người nhận"
        bool(re.search(r"(?<![a-z])spx[a-z]{0,3}\d{8,}", _alnum(text.replace(" | ", ""))) or re.search(r"spx[a-z]{0,3}\d{8,}", text.replace(" ", ""))),  # mã vận đơn SPXVN...
    ]
    return sum(signals) >= 2


def _parse_shipping_label(lines: list[str]) -> dict:
    """Nhãn dán trên kiện hàng (SPX, GHN...): lấy số tiền "Tiền thu người nhận" (COD) nếu có. Không giữ dòng chữ nào để lưu vì nhãn có tên, địa chỉ người nhận."""
    norms = [_norm(l) for l in lines]
    text = " | ".join(norms)
    total = None
    label = next((i for i, n in enumerate(norms) if "tien thu" in n and "nhan" in n), None)
    if label is not None:
        for line in lines[label:label + 9]:  # OCR hay đưa số tiền xuống cách nhãn vài dòng (xen "Khối lượng", "Chữ ký")
            if "vnd" in _norm(line):
                amounts = [a for a in _amounts(line) if a >= 1000]
                if amounts:
                    total = amounts[0]
                    break
    merchant = next((shop for key, shop in _COURIER_SHOP if re.search(rf"(?<![a-z]){key}(?![a-z])", text)), None)
    return {"screen_type": "shipping_label", "merchant": merchant, "total_amount": total, "category": "shopping",
            "force_review": True, "keep_lines": []}


def _next_amount(lines: list[str], start: int, span: int = 3, signed: bool = True) -> int | None:
    """Số tiền đầu tiên trên dòng `start` hoặc vài dòng ngay dưới (OCR hay tách nhãn và số thành hai dòng)."""
    for line in lines[start:start + span]:
        if not signed and line.strip().startswith("-"):
            continue
        found = [a for a in _amounts(line) if a >= 1000]
        if found:
            return found[0]
    return None


_WALLET_CATEGORY = (("giai tri", "entertainment"), ("an uong", "dining"), ("di chuyen", "transport"),
                    ("mua sam", "shopping"), ("sieu thi", "groceries"), ("suc khoe", "healthcare"), ("y te", "healthcare"))


def _parse_wallet_transaction(lines: list[str]) -> dict:
    """Màn hình "Chi tiết giao dịch" của ví điện tử khi không có dòng "Nhà cung cấp": số tiền lớn đầu trang, nhãn giao dịch phía trên,
    và app tự ghi sẵn "Danh mục". Tên cửa hàng lấy từ thương hiệu quen thuộc nếu có, không thì lấy nhãn giao dịch."""
    norms = [_norm(l).strip() for l in lines]
    title = next(i for i, n in enumerate(norms) if "chi tiet giao dich" in n)
    window = lines[title + 1:title + 10]
    label = next((l.strip() for l in lines[title + 1:title + 4] if len(l.strip()) > 3 and not _ONLY_AMOUNT.match(l)), None)
    total = next((_amount_in(l) for l in lines[title + 1:title + 6] if _ONLY_AMOUNT.match(l) and _amount_in(l)), None)
    brands = [b for b in BRAND_CATEGORIES if b in _norm(" ".join(window)) and
              (b not in WHOLE_WORD_BRANDS or re.search(rf"(?<![a-z]){re.escape(b)}(?![a-z])", _norm(" ".join(window))))]
    text = _norm(" ".join(window))
    merchant = BRAND_DISPLAY.get(best_brand(brands, text), label) if brands else label
    category = None
    for i, n in enumerate(norms):
        if n.startswith("danh muc") and i + 1 < len(norms):
            category = next((c for key, c in _WALLET_CATEGORY if key in norms[i + 1]), None)
            if category is None and "hoa don" in norms[i + 1]:
                category = _bill_category(" ".join(x for x in (label, merchant) if x))  # "Hóa đơn" chung: tách theo nhà cung cấp
            break
    return {"screen_type": "wallet_transaction", "merchant": merchant, "total_amount": total, "category": category or "other",
            "force_review": category is None, "keep_lines": [x for x in (label, merchant) if x]}


def _parse_momo_result(lines: list[str]) -> dict:
    """"Kết quả giao dịch" của MoMo: số tiền lớn dưới "Giao dịch thành công", nhà cung cấp ở dòng sau nhãn "Dịch vụ/ Cửa hàng".
    Tên và địa chỉ khách hàng không được giữ lại."""
    norms = [_norm(l).strip() for l in lines]
    title = next(i for i, n in enumerate(norms) if "ket qua giao dich" in n)
    total = next((_amount_in(l) for l in lines[title + 1:title + 6] if _ONLY_AMOUNT.match(l) and _amount_in(l)), None)
    label = next((i for i, n in enumerate(norms) if n.startswith("dich vu/")), None)
    merchant = lines[label + 1].strip() if label is not None and label + 1 < len(lines) else None
    return {"screen_type": "momo_result", "merchant": merchant, "total_amount": total, "category": _bill_category(merchant or ""),
            "keep_lines": [merchant] if merchant else []}


def _parse_provider_notice(lines: list[str]) -> dict:
    """Tin nhắn "Thông báo thanh toán thành công" của tài khoản OA (Zalo...): số tiền dưới nhãn "Số tiền", nhà cung cấp là thương hiệu đã biết."""
    norms = [_norm(l).strip() for l in lines]
    label = next((i for i, n in enumerate(norms) if n == "so tien"), None)
    total = _next_amount(lines, label + 1, 2) if label is not None else None
    window = _norm(" ".join(lines[:10]))
    brands = [b for b in BRAND_CATEGORIES if b in window]
    brand = best_brand(brands, window) if brands else None
    merchant = BRAND_DISPLAY.get(brand, brand) if brand else None
    category = BRAND_CATEGORIES[brand] if brand else _bill_category(merchant or "")
    return {"screen_type": "provider_notice", "merchant": merchant, "total_amount": total, "category": category,
            "keep_lines": [merchant] if merchant else []}


def _food_variant(norms: list[str]) -> str | None:
    text = " | ".join(norms)
    if not any("tom tat don hang" in n for n in norms):
        return None
    if re.search(r"gf-?\d{2,}", text) or "voi grab" in text:
        return "grab"
    if "chi tiet giao hang" in text and "ma don hang" in text:
        return "xanh"
    if any("chi tiet don hang" in n for n in norms) and ("phi giao hang" in text or "don hoan tat" in text):
        return "shopee"
    return None


def _parse_food_delivery(lines: list[str], variant: str) -> dict:
    """Chi tiết đơn giao đồ ăn: GrabFood, Xanh SM Ngon (Green Food), ShopeeFood. Mỗi app một bố cục; ảnh có tên, địa chỉ, số điện thoại người nhận
    nên chỉ giữ lại tên quán và các dòng món để lưu."""
    norms = [_norm(l).strip() for l in lines]
    merchant = total = None
    items: list[str] = []
    if variant == "grab":
        gf = next(i for i, n in enumerate(norms) if re.search(r"gf-?\d{2,}", n))
        merchant = lines[gf - 1].strip() if gf > 0 else None
        start = next((i for i, n in enumerate(norms) if "tom tat don hang" in n), gf)
        end = next((i for i, n in enumerate(norms) if n.startswith("tong tam tinh")), len(lines))
        items = [l for l in lines[start + 1:end] if not _ONLY_AMOUNT.match(l) and not re.fullmatch(r"\s*\d+x\s*", l) and _norm(l).strip() != "dat lai"]
        t = next((i for i, n in enumerate(norms) if n.startswith("tong cong")), None)
        if t is not None:
            total = _next_amount(lines, t)
    elif variant == "xanh":
        h = next(i for i, n in enumerate(norms) if "chi tiet giao hang" in n)
        merchant = lines[h + 1].strip() if h + 1 < len(lines) else None
        m = next((re.search(r"mon\W{0,4}([\d.,]+)", n) for n in norms if re.search(r"mon\W{0,4}\d", n)), None)
        digits = re.sub(r"\D", "", m.group(1)) if m else ""
        total = int(digits) if digits and int(digits) <= _MAX_AMOUNT else None
    else:  # shopee
        for line in lines:
            m = re.match(r"^\W*\w{0,2}\s*do\s*(?:an|uong)\s*[|lI!]?\s*(.+)$", _strip_accents_keep_case(line), re.I)
            if m:
                merchant = re.sub(r"[\s>xX]+$", "", m.group(1)).strip()
                break
        start = next((i for i, n in enumerate(norms) if "tom tat don hang" in n), 0)
        tong = next((i for i, n in enumerate(norms) if n.startswith("tong (") or n.startswith("tong(")), None)
        end = next((i for i, n in enumerate(norms) if n.startswith("thong tin don hang")), len(lines))
        if tong is not None:
            pay = [a for l in lines[tong + 1:end] if _ONLY_AMOUNT.match(l) and not l.strip().startswith("-") for a in _amounts(l)]
            total = pay[-1] if pay else None  # dòng cuối của phần tổng kết là số khách thực trả (sau phí và giảm giá)
        items = [l for l in lines[start + 1:tong if tong is not None else end] if not _ONLY_AMOUNT.match(l)]
    if merchant:
        merchant = re.sub(r"\s+", " ", merchant)
    return {"screen_type": "food_delivery", "merchant": merchant, "total_amount": total, "category": "dining",
            "keep_lines": ([merchant] if merchant else []) + items}


def _strip_accents_keep_case(text: str) -> str:
    """Bỏ dấu nhưng giữ nguyên hoa/thường (để cắt đúng phần tên cửa hàng từ dòng gốc)."""
    import unicodedata
    return unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D")).encode("ascii", "ignore").decode()


def _parse_bus_ticket(lines: list[str]) -> dict:
    norms = [_norm(l).strip() for l in lines]
    title = next(i for i, n in enumerate(norms) if "thong tin ve xe" in n)
    merchant = next((l.strip() for l, n in zip(lines[title + 1:title + 6], norms[title + 1:title + 6])
                     if not re.search(r"(?<![a-z])ve (da|chua|het|bi)", n) and len(n) > 2 and not _ONLY_AMOUNT.match(l)), None)
    t = next((i for i, n in enumerate(norms) if n.startswith("tong tien")), None)
    return {"screen_type": "bus_ticket", "merchant": merchant, "total_amount": _next_amount(lines, t, 2) if t is not None else None,
            "category": "transport", "keep_lines": [merchant] if merchant else []}


_PAID_LABEL = re.compile(r"^tt\s*:?\s*(\d{1,3}(?:[.,]\d{3})+)")  # "TT: 260.000": tiền thanh toán trên phiếu lên xe


def _is_paper_bus_ticket(norms: list[str]) -> bool:
    """Phiếu lên xe khách bằng giấy (Vexere...). OCR đọc hỏng nhiều chữ nên cần ít nhất ba dấu hiệu độc lập, trong đó có tiêu đề hoặc tên nền tảng;
    so khớp trên chữ viết liền vì khoảng trắng hay bị mất ("lenxe")."""
    sq = _alnum("".join(norms))
    title = "phieulenxe" in sq or "vexere" in sq
    signals = ["phieulenxe" in sq, "vexere" in sq, "tenhk" in sq, "quetmaqr" in sq and "lenxe" in sq, any(_PAID_LABEL.match(n) for n in norms)]
    return title and sum(signals) >= 3


def _parse_paper_bus_ticket(lines: list[str]) -> dict:
    """Số phải trả là dòng "TT:"; không có thì lấy số tiền lớn nhất (giá vé) và nhắc người dùng xem lại. Tổng đài (190.888.606) và biển số không phải tiền nên
    chỉ nhận số có dấu nghìn trong khoảng giá vé. Không giữ dòng chữ nào để lưu vì phiếu có tên và số điện thoại hành khách."""
    norms = [_norm(l).strip() for l in lines]
    paid = next((int(re.sub(r"\D", "", m.group(1))) for n in norms if (m := _PAID_LABEL.match(n))), None)
    prices = [a for l in lines for m in _THOUSANDS.findall(l) if 10_000 <= (a := int(re.sub(r"\D", "", m))) <= 20_000_000]
    total = paid if paid is not None else (max(prices) if prices else None)
    vexere = "vexere" in _alnum("".join(norms))
    return {"screen_type": "bus_ticket", "merchant": "Vexere" if vexere else None, "total_amount": total, "category": "transport",
            "keep_lines": [], "force_review": paid is None}


def apply(result: dict, screen: dict, lookup, display) -> None:
    """Ghi kết quả đọc màn hình app đè lên kết quả phân tích hóa đơn thường. Cửa hàng người dùng đã lưu vẫn được ưu tiên về danh mục."""
    def category_of(name: str | None, default: str) -> tuple[str, str, float]:
        remembered = lookup(name) if name else None
        return (remembered, "memory", 1.0) if remembered else (default, "screen", 0.8)

    result["screen_type"] = screen["screen_type"]
    if screen["screen_type"] == "order_list":
        orders = []
        for o in screen["orders"]:
            cat, src, conf = category_of(o["merchant"], screen["category"])
            orders.append({**o, "merchant_display": display(o["merchant"]), "category": cat, "category_source": src,
                           "category_confidence": conf})
        first = orders[0] if orders else None
        result.update(orders=orders, incomplete_orders=screen["incomplete_orders"],
                      merchant=first["merchant"] if first else None, total_amount=first["total_amount"] if first else None,
                      category=screen["category"], category_source="screen", category_confidence=0.8,
                      needs_review=True)  # mỗi đơn cần người dùng xác nhận
        return
    cat, src, conf = category_of(screen["merchant"], screen["category"])
    result.update(merchant=screen["merchant"], total_amount=screen["total_amount"], category=cat, category_source=src,
                  category_confidence=conf,
                  needs_review=screen["merchant"] is None or screen["total_amount"] is None or screen.get("force_review", False))


def parse(lines: list[str]) -> dict | None:
    """Trả về trường đã đọc nếu là ảnh màn hình app nhận ra được, ngược lại None (xử lý như hóa đơn thường)."""
    norms = [_norm(l).strip() for l in lines]
    has_order_total = any(_order_total(n) is not None for n in norms)
    if has_order_total and any(n.startswith("hoan thanh") for n in norms) and any("mua lai" in n for n in norms):
        return _parse_order_list(lines)
    if any("ma chuyen di" in n for n in norms):
        return _parse_ride(lines)
    if any(n.startswith("nha cung cap") for n in norms) and any(n.startswith(("chi tiet giao dich", "ma giao dich")) for n in norms):
        return _parse_service_payment(lines)
    food = _food_variant(norms)
    if food:
        return _parse_food_delivery(lines, food)
    if any("thong tin ve xe" in n for n in norms) and any(n.startswith("tong tien") for n in norms):
        return _parse_bus_ticket(lines)
    if any("chi tiet giao dich" in n for n in norms) and any(n.startswith("ma giao dich") for n in norms)             and not any(n.startswith("nha cung cap") for n in norms) and sum(n.startswith(("danh muc", "thuong xu")) for n in norms) >= 1:
        return _parse_wallet_transaction(lines)
    if any("ket qua giao dich" in n for n in norms) and any(n.startswith("dich vu/") for n in norms):
        return _parse_momo_result(lines)
    if any(n.startswith("thong bao thanh toan thanh") for n in norms) and any(n == "so tien" for n in norms):
        return _parse_provider_notice(lines)
    if _is_shipping_label(norms):
        return _parse_shipping_label(lines)
    if _is_paper_bus_ticket(norms):
        return _parse_paper_bus_ticket(lines)
    if _is_cinema_ticket(norms):
        return _parse_cinema_ticket(lines)
    return None
