"""Nhận ra ảnh chụp màn hình app (không phải hóa đơn giấy) và đọc đúng trường theo từng loại màn hình.

Hỗ trợ: chuyến xe công nghệ, thanh toán hóa đơn dịch vụ, danh sách đơn TMĐT, vé xem phim, nhãn kiện hàng, ví điện tử/MoMo/Zalo, đơn giao đồ ăn
(GrabFood, Xanh SM Ngon, ShopeeFood), vé xe khách (app và phiếu giấy). Viết theo ảnh thật của người dùng.

CHỮ và HẰNG SỐ gắn với giao diện từng app (nhãn như "Nhà cung cấp", "Tóm tắt đơn hàng", tên cửa hàng cố định, danh mục, số dòng tìm số tiền...) nằm ở
``data/app_screens.json``, đọc lúc dùng (``_SPEC``): app đổi chữ thì sửa dữ liệu, không sửa code. Thứ tự ưu tiên giữa các loại màn hình (``parse``) và thuật toán
đọc bố cục từng loại vẫn ở đây.
ponytail: luật theo giao diện hiện tại của từng app; app đổi bố cục (không chỉ đổi chữ) hoặc app khác thì cần thêm luật trong code.
"""
import re
import unicodedata
from functools import lru_cache

from classification.brands import BRAND_CATEGORIES, BRAND_DISPLAY, WHOLE_WORD_BRANDS
from parsing import extract  # ngưỡng đọc qua extract.<tên> lúc dùng: bản admin thay nóng không để lại giá trị cũ ở đây
from parsing.extract import _alnum, _amounts, _norm, best_brand
from classification import data_files

_SHOP_BADGE = data_files.table("parsing.app_screens._SHOP_BADGE")
_HOUSING_WORDS = data_files.table("parsing.app_screens._HOUSING_WORDS")
_CINEMA_WORDS = data_files.table("parsing.app_screens._CINEMA_WORDS")
_COURIER_SHOP = data_files.table("parsing.app_screens._COURIER_SHOP")
_WALLET_CATEGORY = data_files.table("parsing.app_screens._WALLET_CATEGORY")
_SPEC = data_files.screens_document()  # chữ/hằng số theo giao diện từng app; đọc qua _SPEC[...] lúc dùng nên bản admin thay nóng (config_api gán lại tên này) có hiệu lực ngay
_ONLY_AMOUNT = re.compile(r"^\s*[-+]?\s*\d[\d.,]*\s*\S?\s*$")
_THOUSANDS = re.compile(r"\d{1,3}(?:[.,]\d{3})+")


@lru_cache(maxsize=256)
def _rx(pattern: str, flags: int = 0) -> re.Pattern:
    """Regex lấy từ _SPEC, biên dịch một lần cho mỗi mẫu."""
    return re.compile(pattern, flags)


def _whole(word: str, text: str) -> bool:
    return bool(re.search(rf"(?<![a-z]){re.escape(word)}(?![a-z])", text))


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
    m = _rx(_SPEC["order_list"]["total_regex"]).search(norm_line)
    if not m:
        return None
    digits = re.sub(r"\D", "", m.group(1))
    return int(digits) if digits and int(digits) <= extract._MAX_AMOUNT else None


def _parse_order_list(lines: list[str]) -> dict:
    s = _SPEC["order_list"]
    norms = [_norm(l).strip() for l in lines]
    done = [i for i, n in enumerate(norms) if n.startswith(s["completed_prefix"])]
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
    return {"screen_type": "order_list", "orders": orders, "incomplete_orders": incomplete, "category": s["category"]}


def _parse_ride(lines: list[str]) -> dict:
    s = _SPEC["ride"]
    norms = [_norm(l).strip() for l in lines]
    total = None
    for i, n in enumerate(norms):
        if n == s["total_label"]:
            total = next((a for a in (_amount_in(l) for l in lines[i:i + s["total_span"]]) if a), None)
            break
    # ponytail: màn hình này không in tên hãng, nên dùng tên chung (trong dữ liệu) để người dùng tự sửa nếu muốn
    return {"screen_type": "ride", "merchant": s["merchant"], "total_amount": total, "category": s["category"]}


def _bill_category(text: str) -> str:
    """Hóa đơn dịch vụ: điện, nước, gas, chung cư, thuê nhà -> nhóm housing; còn lại (internet, điện thoại, data) -> nhóm mặc định (utilities). Tên nhóm ở dữ liệu."""
    s = _SPEC["bill_category"]
    norm = _norm(text)
    if any(w in norm for w in _HOUSING_WORDS) or any(_whole(w, norm) for w in s["housing_whole_words"]):
        return s["housing_category"]
    return s["default_category"]


def _parse_service_payment(lines: list[str]) -> dict:
    s = _SPEC["service_payment"]
    norms = [_norm(l).strip() for l in lines]
    merchant = None
    label_words = len(s["provider_prefix"].split())
    for i, n in enumerate(norms):
        if n.startswith(s["provider_prefix"]):
            same_line = " ".join(lines[i].split()[label_words:])  # nhãn nhà cung cấp là vài từ; giá trị thường ở dòng kế
            merchant = same_line or (lines[i + 1].strip() if i + 1 < len(lines) else None)
            break
    title = next((i for i, n in enumerate(norms) if s["title_phrase"] in n), 0)
    banner = lines[title + 1].strip() if title + 1 < len(lines) else ""
    if merchant and banner and _norm(banner).replace(" ", "") == _norm(merchant).replace(" ", ""):
        merchant = banner  # dòng tiêu đề IN HOA phía trên đọc đúng dấu hơn dòng "Nhà cung cấp"
    total = next((_amount_in(l) for l in lines[title + 1:title + 1 + s["amount_span"]] if _ONLY_AMOUNT.match(l) and _amount_in(l)), None)
    if merchant is None and title + 1 < len(lines):
        merchant = lines[title + 1].strip().title()  # dòng tên in hoa ngay dưới tiêu đề
    return {"screen_type": "service_payment", "merchant": merchant, "total_amount": total,
            "category": _bill_category(" ".join(x for x in (merchant, banner) if x))}


def _is_cinema_ticket(norms: list[str]) -> bool:
    s = _SPEC["cinema_ticket"]
    tickets = sum(bool(_rx(s["ticket_regex"]).search(n)) for n in norms)  # OCR hay đọc hỏng chữ TICKET in lặp làm nền
    text = " | ".join(norms)
    if any(w in text for w in _CINEMA_WORDS) or any(_whole(w, text) for w in s["cinema_whole_words"]):
        return tickets >= s["min_tickets_with_cinema_word"] or s["entry_phrase"] in text
    return s["entry_phrase"] in text and tickets >= s["min_tickets_without_cinema_word"]  # "THẺ VÀO" + nền TICKET lặp lại, dù chữ khác bị mất


def _parse_cinema_ticket(lines: list[str]) -> dict:
    s = _SPEC["cinema_ticket"]
    text = " | ".join(_norm(l) for l in lines)
    merchant = next((c["name"] for c in s["chains"] if (_whole(c["match"], text) if c["whole_word"] else c["match"] in text)), None)
    amounts = [int(re.sub(r"\D", "", m)) for l in lines for m in _THOUSANDS.findall(l)]
    amounts = [a for a in amounts if s["amount_min"] <= a <= s["amount_max"]]
    # ponytail: lấy số lớn nhất; vé nhiều dòng giá (vé + dịch vụ) thì số lớn nhất là tổng. Không đọc được giá thì để trống, không đoán
    return {"screen_type": "cinema_ticket", "merchant": merchant, "total_amount": max(amounts) if amounts else None,
            "category": s["category"]}


def _is_shipping_label(norms: list[str]) -> bool:
    """Cần ít nhất hai dấu hiệu độc lập (nhãn "Mã vận đơn" hay bị OCR đọc nát nên không dựa vào một chữ nào)."""
    s = _SPEC["shipping_label"]
    text = " | ".join(norms)
    tracking = bool(_rx(rf"(?<![a-z]){s['tracking_regex']}").search(_alnum(text.replace(" | ", ""))) or _rx(s["tracking_regex"]).search(text.replace(" ", "")))  # mã vận đơn SPXVN...
    signals = [phrase in text for phrase in s["signal_phrases"]] + [all(w in text for w in s["cod_label_words"]), tracking]
    return sum(signals) >= s["min_signals"]


def _parse_shipping_label(lines: list[str]) -> dict:
    """Nhãn dán trên kiện hàng (SPX, GHN...): lấy số tiền "Tiền thu người nhận" (COD) nếu có. Không giữ dòng chữ nào để lưu vì nhãn có tên, địa chỉ người nhận."""
    s = _SPEC["shipping_label"]
    norms = [_norm(l) for l in lines]
    text = " | ".join(norms)
    total = None
    label = next((i for i, n in enumerate(norms) if all(w in n for w in s["cod_label_words"])), None)
    if label is not None:
        for line in lines[label:label + s["amount_window"]]:  # OCR hay đưa số tiền xuống cách nhãn vài dòng (xen "Khối lượng", "Chữ ký")
            if s["currency_word"] in _norm(line):
                amounts = [a for a in _amounts(line) if a >= s["amount_min"]]
                if amounts:
                    total = amounts[0]
                    break
    merchant = next((shop for key, shop in _COURIER_SHOP if _whole(key, text)), None)
    return {"screen_type": "shipping_label", "merchant": merchant, "total_amount": total, "category": s["category"],
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


def _parse_wallet_transaction(lines: list[str]) -> dict:
    """Màn hình "Chi tiết giao dịch" của ví điện tử khi không có dòng "Nhà cung cấp": số tiền lớn đầu trang, nhãn giao dịch phía trên,
    và app tự ghi sẵn "Danh mục". Tên cửa hàng lấy từ thương hiệu quen thuộc nếu có, không thì lấy nhãn giao dịch."""
    s = _SPEC["wallet_transaction"]
    norms = [_norm(l).strip() for l in lines]
    title = next(i for i, n in enumerate(norms) if s["title_phrase"] in n)
    window = lines[title + 1:title + 1 + s["window_span"]]
    label = next((l.strip() for l in lines[title + 1:title + 1 + s["label_span"]] if len(l.strip()) >= s["label_min_chars"] and not _ONLY_AMOUNT.match(l)), None)
    total = next((_amount_in(l) for l in lines[title + 1:title + 1 + s["amount_span"]] if _ONLY_AMOUNT.match(l) and _amount_in(l)), None)
    brands = [b for b in BRAND_CATEGORIES if b in _norm(" ".join(window)) and
              (b not in WHOLE_WORD_BRANDS or re.search(rf"(?<![a-z]){re.escape(b)}(?![a-z])", _norm(" ".join(window))))]
    text = _norm(" ".join(window))
    merchant = BRAND_DISPLAY.get(best_brand(brands, text), label) if brands else label
    category = None
    for i, n in enumerate(norms):
        if n.startswith(s["category_label_prefix"]) and i + 1 < len(norms):
            category = next((c for key, c in _WALLET_CATEGORY if key in norms[i + 1]), None)
            if category is None and s["generic_bill_phrase"] in norms[i + 1]:
                category = _bill_category(" ".join(x for x in (label, merchant) if x))  # "Hóa đơn" chung: tách theo nhà cung cấp
            break
    return {"screen_type": "wallet_transaction", "merchant": merchant, "total_amount": total, "category": category or s["default_category"],
            "force_review": category is None, "keep_lines": [x for x in (label, merchant) if x]}


def _parse_momo_result(lines: list[str]) -> dict:
    """"Kết quả giao dịch" của MoMo: số tiền lớn dưới "Giao dịch thành công", nhà cung cấp ở dòng sau nhãn "Dịch vụ/ Cửa hàng".
    Tên và địa chỉ khách hàng không được giữ lại."""
    s = _SPEC["momo_result"]
    norms = [_norm(l).strip() for l in lines]
    title = next(i for i, n in enumerate(norms) if s["title_phrase"] in n)
    total = next((_amount_in(l) for l in lines[title + 1:title + 1 + s["amount_span"]] if _ONLY_AMOUNT.match(l) and _amount_in(l)), None)
    label = next((i for i, n in enumerate(norms) if n.startswith(s["provider_label_prefix"])), None)
    merchant = lines[label + 1].strip() if label is not None and label + 1 < len(lines) else None
    return {"screen_type": "momo_result", "merchant": merchant, "total_amount": total, "category": _bill_category(merchant or ""),
            "keep_lines": [merchant] if merchant else []}


def _parse_provider_notice(lines: list[str]) -> dict:
    """Tin nhắn "Thông báo thanh toán thành công" của tài khoản OA (Zalo...): số tiền dưới nhãn "Số tiền", nhà cung cấp là thương hiệu đã biết."""
    s = _SPEC["provider_notice"]
    norms = [_norm(l).strip() for l in lines]
    label = next((i for i, n in enumerate(norms) if n == s["amount_label"]), None)
    total = _next_amount(lines, label + 1, s["amount_span"]) if label is not None else None
    window = _norm(" ".join(lines[:s["brand_window_lines"]]))
    brands = [b for b in BRAND_CATEGORIES if b in window]
    brand = best_brand(brands, window) if brands else None
    merchant = BRAND_DISPLAY.get(brand, brand) if brand else None
    category = BRAND_CATEGORIES[brand] if brand else _bill_category(merchant or "")
    return {"screen_type": "provider_notice", "merchant": merchant, "total_amount": total, "category": category,
            "keep_lines": [merchant] if merchant else []}


def _food_variant(norms: list[str]) -> str | None:
    f = _SPEC["food_delivery"]
    text = " | ".join(norms)
    if not any(f["summary_phrase"] in n for n in norms):
        return None
    if _rx(f["grab"]["order_code_regex"]).search(text) or f["grab"]["alt_phrase"] in text:
        return "grab"
    if f["xanh"]["detail_phrase"] in text and f["xanh"]["order_phrase"] in text:
        return "xanh"
    if any(f["shopee"]["detail_phrase"] in n for n in norms) and (f["shopee"]["fee_phrase"] in text or f["shopee"]["done_phrase"] in text):
        return "shopee"
    return None


def _parse_food_delivery(lines: list[str], variant: str) -> dict:
    """Chi tiết đơn giao đồ ăn: GrabFood, Xanh SM Ngon (Green Food), ShopeeFood. Mỗi app một bố cục; ảnh có tên, địa chỉ, số điện thoại người nhận
    nên chỉ giữ lại tên quán và các dòng món để lưu."""
    f = _SPEC["food_delivery"]
    norms = [_norm(l).strip() for l in lines]
    merchant = total = None
    items: list[str] = []
    if variant == "grab":
        g = f["grab"]
        gf = next((i for i, n in enumerate(norms) if _rx(g["order_code_regex"]).search(n)), None)  # nhận ra GrabFood chỉ nhờ chữ "với Grab" thì không có dòng mã đơn
        merchant = lines[gf - 1].strip() if gf else None
        start = next((i for i, n in enumerate(norms) if f["summary_phrase"] in n), 0)
        end = next((i for i, n in enumerate(norms) if n.startswith(g["end_prefix"])), len(lines))
        items = [l for l in lines[start + 1:end] if not _ONLY_AMOUNT.match(l) and not re.fullmatch(r"\s*\d+x\s*", l) and _norm(l).strip() not in g["skip_lines"]]
        t = next((i for i, n in enumerate(norms) if n.startswith(g["total_prefix"])), None)
        if t is not None:
            total = _next_amount(lines, t)
    elif variant == "xanh":
        x = f["xanh"]
        h = next(i for i, n in enumerate(norms) if x["detail_phrase"] in n)
        merchant = lines[h + 1].strip() if h + 1 < len(lines) else None
        m = next((_rx(x["dish_total_regex"]).search(n) for n in norms if _rx(x["dish_probe_regex"]).search(n)), None)
        digits = re.sub(r"\D", "", m.group(1)) if m else ""
        total = int(digits) if digits and int(digits) <= extract._MAX_AMOUNT else None
    else:  # shopee
        sh = f["shopee"]
        for line in lines:
            m = _rx(sh["merchant_regex"], re.I).match(_strip_accents_keep_case(line))
            if m:
                merchant = re.sub(r"[\s>xX]+$", "", m.group(1)).strip()
                break
        start = next((i for i, n in enumerate(norms) if f["summary_phrase"] in n), 0)
        tong = next((i for i, n in enumerate(norms) if n.startswith(tuple(sh["total_prefixes"]))), None)
        end = next((i for i, n in enumerate(norms) if n.startswith(sh["end_prefix"])), len(lines))
        if tong is not None:
            pay = [a for l in lines[tong + 1:end] if _ONLY_AMOUNT.match(l) and not l.strip().startswith("-") for a in _amounts(l)]
            total = pay[-1] if pay else None  # dòng cuối của phần tổng kết là số khách thực trả (sau phí và giảm giá)
        items = [l for l in lines[start + 1:tong if tong is not None else end] if not _ONLY_AMOUNT.match(l)]
    if merchant:
        merchant = re.sub(r"\s+", " ", merchant)
    return {"screen_type": "food_delivery", "merchant": merchant, "total_amount": total, "category": f["category"],
            "keep_lines": ([merchant] if merchant else []) + items}


def _strip_accents_keep_case(text: str) -> str:
    """Bỏ dấu nhưng giữ nguyên hoa/thường (để cắt đúng phần tên cửa hàng từ dòng gốc)."""
    return unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D")).encode("ascii", "ignore").decode()


def _parse_bus_ticket(lines: list[str]) -> dict:
    s = _SPEC["bus_ticket"]
    norms = [_norm(l).strip() for l in lines]
    title = next(i for i, n in enumerate(norms) if s["title_phrase"] in n)
    window = slice(title + 1, title + 1 + s["merchant_span"])
    merchant = next((l.strip() for l, n in zip(lines[window], norms[window])
                     if not _rx(s["status_regex"]).search(n) and len(n) > 2 and not _ONLY_AMOUNT.match(l)), None)
    t = next((i for i, n in enumerate(norms) if n.startswith(s["total_prefix"])), None)
    return {"screen_type": "bus_ticket", "merchant": merchant, "total_amount": _next_amount(lines, t, s["total_span"]) if t is not None else None,
            "category": s["category"], "keep_lines": [merchant] if merchant else []}


def _is_paper_bus_ticket(norms: list[str]) -> bool:
    """Phiếu lên xe khách bằng giấy (Vexere...). OCR đọc hỏng nhiều chữ nên cần ít nhất ba dấu hiệu độc lập, trong đó có tiêu đề hoặc tên nền tảng;
    so khớp trên chữ viết liền vì khoảng trắng hay bị mất ("lenxe")."""
    s = _SPEC["paper_bus_ticket"]
    sq = _alnum("".join(norms))
    title = any(token in sq for token in s["title_tokens"])
    signals = [token in sq for token in s["signal_tokens"]] + [all(token in sq for token in s["qr_tokens"]), any(_rx(s["paid_regex"]).match(n) for n in norms)]
    return title and sum(signals) >= s["min_signals"]


def _parse_paper_bus_ticket(lines: list[str]) -> dict:
    """Số phải trả là dòng "TT:"; không có thì lấy số tiền lớn nhất (giá vé) và nhắc người dùng xem lại. Tổng đài (190.888.606) và biển số không phải tiền nên
    chỉ nhận số có dấu nghìn trong khoảng giá vé. Không giữ dòng chữ nào để lưu vì phiếu có tên và số điện thoại hành khách."""
    s = _SPEC["paper_bus_ticket"]
    norms = [_norm(l).strip() for l in lines]
    paid = next((int(re.sub(r"\D", "", m.group(1))) for n in norms if (m := _rx(s["paid_regex"]).match(n))), None)
    prices = [a for l in lines for m in _THOUSANDS.findall(l) if s["price_min"] <= (a := int(re.sub(r"\D", "", m))) <= s["price_max"]]
    total = paid if paid is not None else (max(prices) if prices else None)
    platform = s["platform_token"] in _alnum("".join(norms))
    return {"screen_type": "bus_ticket", "merchant": s["platform_name"] if platform else None, "total_amount": total, "category": s["category"],
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
    """Trả về trường đã đọc nếu là ảnh màn hình app nhận ra được, ngược lại None (xử lý như hóa đơn thường). Thứ tự các nhánh là thứ tự ưu tiên."""
    norms = [_norm(l).strip() for l in lines]
    order, ride, pay, wallet = _SPEC["order_list"], _SPEC["ride"], _SPEC["service_payment"], _SPEC["wallet_transaction"]
    bus, momo, notice = _SPEC["bus_ticket"], _SPEC["momo_result"], _SPEC["provider_notice"]
    has_order_total = any(_order_total(n) is not None for n in norms)
    if has_order_total and any(n.startswith(order["completed_prefix"]) for n in norms) and any(order["rebuy_phrase"] in n for n in norms):
        return _parse_order_list(lines)
    if any(ride["anchor_phrase"] in n for n in norms):
        return _parse_ride(lines)
    if any(n.startswith(pay["provider_prefix"]) for n in norms) and any(n.startswith(tuple(pay["title_prefixes"])) for n in norms):
        return _parse_service_payment(lines)
    food = _food_variant(norms)
    if food:
        return _parse_food_delivery(lines, food)
    if any(bus["title_phrase"] in n for n in norms) and any(n.startswith(bus["total_prefix"]) for n in norms):
        return _parse_bus_ticket(lines)
    if (any(wallet["title_phrase"] in n for n in norms) and any(n.startswith(wallet["transaction_id_prefix"]) for n in norms)
            and not any(n.startswith(wallet["excluded_when_prefix"]) for n in norms)
            and sum(n.startswith(tuple(wallet["category_prefixes"])) for n in norms) >= 1):
        return _parse_wallet_transaction(lines)
    if any(momo["title_phrase"] in n for n in norms) and any(n.startswith(momo["provider_label_prefix"]) for n in norms):
        return _parse_momo_result(lines)
    if any(n.startswith(notice["title_prefix"]) for n in norms) and any(n == notice["amount_label"] for n in norms):
        return _parse_provider_notice(lines)
    if _is_shipping_label(norms):
        return _parse_shipping_label(lines)
    if _is_paper_bus_ticket(norms):
        return _parse_paper_bus_ticket(lines)
    if _is_cinema_ticket(norms):
        return _parse_cinema_ticket(lines)
    return None
