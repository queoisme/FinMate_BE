"""Bóc tách từng món (tên + giá) từ các hộp chữ OCR của hóa đơn giấy."""
import re
from dataclasses import dataclass, replace
from statistics import median

from parsing.extract import _MIN_AMOUNT, _amounts, _norm
from reading.ocr import OcrBox

ROW_TOLERANCE = 0.6  # lệch tâm dọc dưới 0,6 lần chiều cao hộp trung vị thì cùng hàng
PRICE_COLUMN = 0.35  # giá nằm trong 35% bên phải tờ giấy

_HEADER_RE = re.compile(r"\b(?:sl|tong|ten hang|mat hang|don gia|thanh tien|so luong)\b")
# Dòng cộng tạm: hết danh sách món nhưng tổng bill còn có thể cộng thuế/phí, trừ giảm giá cả bill ở dưới.
# ponytail: "^t[oe0-9]ng" bắt cả "T6ng", "Téng" do OCR đọc sai "Tổng"; tên món mở đầu bằng "Tông..." sẽ bị coi là dòng tổng
_SUBTOTAL_RE = re.compile(r"^t[oe0-9]ng\b|\bt?ong cong|thanh tien|c[o0]ng tien hang|t[a4]m tinh|^tien hang")
_FINAL_RE = re.compile(r"thanh to[a4]n|phai tra|phai thu|total")  # dòng tổng cuối: hết vùng bill
_DISCOUNT_RE = re.compile(r"giam|chiet khau|voucher|khuyen mai")
# thuế/phí cộng sau dòng cộng tiền hàng; "tổng tiền chưa thuế/VAT", "trước thuế" là cộng tạm, "Tổng cộng (gồm thuế)"
# là tổng cuối, không phải thuế.
# Đồng bộ với ReceiptSplitter.IsSurcharge (backend, FinMate.Application/Transactions): thêm từ ở đây thì thêm ở BE và ở
# ReceiptSplitterTests.Every_ocr_surcharge_term_is_a_surcharge, không thì BE coi dòng phí là một món thường.
_SURCHARGE_RE = re.compile(r"\bthue\b|\bvat\b|gtgt|phi phuc vu|phi dich vu|service")  # gtgt: OCR hay đọc "Thuế" thành "Thu€"
_PRE_TAX_RE = re.compile(r"\bchua\b|\btruoc\b|\bgom\b")
# dòng trả tiền/tiền thối/điểm: luôn nằm sau các món, gặp là hết bảng món (không bao giờ là một món)
_PAYMENT_RE = re.compile(r"tien mat|chuyen khoan|^the\s*:|^the\s*$|^the\s+(?:atm|tin dung|ghi no|visa|master|jcb|napas|ngan hang)\b|khach dua|tien thua|tra lai|thanh toan|\bdiem\b")
_TOTAL_WORD_RE = re.compile(r"\bt[oe0-9]ng\b")  # "Tổng" ở bất kỳ đâu trong hàng
_NUMERIC_RE = re.compile(r"^[\d\s.,x×*+\-đdĐD]*$")  # số lượng, đơn giá: không phải chữ của tên
_NEGATIVE_RE = re.compile(r"-\s*\d")
# ô chỉ chứa một số tiền có dấu nhóm nghìn ("66.000", "1,920,500đ"); loại "2,770ĐIỂM", "Nợ: 1311", số điện thoại
_MONEY_BOX_RE = re.compile(r"[-+]?\s*\d{1,3}(?:[.,]\d{3})+(?:[.,]\d{1,2})?\s*(?:đ|d|vnd)?", re.IGNORECASE)


@dataclass(frozen=True)
class Item:
    name: str
    amount: int  # giảm giá là số âm
    parent: int | None  # chỉ số trong items của món chính (với phụ kiện "+")
    is_discount: bool


@dataclass(frozen=True)
class ItemsResult:
    items: list[Item]
    ok: bool
    # Tổng các món nếu mọi hàng "đơn giá | số lượng | thành tiền" có thành tiền lệch đúng một chữ số được thay bằng đơn giá x số lượng; None khi không có hàng nào.
    # Chưa kiểm chứng (cần tổng bill), nên chỉ để totals.choose_total thêm vào các ứng viên.
    fixed_total: int | None = None


def _rows(boxes: list[OcrBox]) -> list[list[OcrBox]]:
    """Gom hộp thành hàng ngang theo tâm dọc, mỗi hàng sắp trái sang phải."""
    tol = ROW_TOLERANCE * median(b.y1 - b.y0 for b in boxes)
    rows: list[list[OcrBox]] = []
    centers: list[float] = []
    for b in sorted(boxes, key=lambda b: (b.y0 + b.y1) / 2):
        cy = (b.y0 + b.y1) / 2
        if rows and abs(cy - centers[-1]) < tol:
            rows[-1].append(b)
            centers[-1] = sum((x.y0 + x.y1) / 2 for x in rows[-1]) / len(rows[-1])
        else:
            rows.append([b])
            centers.append(cy)
    return _join_wrapped_amounts([sorted(r, key=lambda b: b.x0) for r in rows])


_WRAPPED_AMOUNT_RE = re.compile(r"^\d{1,3}(?:[.,]\d{3})*[.,]\d{2}$")  # "452,43": số tiền bị cắt giữa ba chữ số cuối
_TOTAL_LABEL_RE = re.compile(r"tong|thanh toan|phai tra|tam tinh|tien hang|total|cong")


def _join_wrapped_amounts(rows: list[list[OcrBox]]) -> list[list[OcrBox]]:
    """Cột tổng hẹp in "452,432" thành "452,43" ở hàng nhãn và "2" ở hàng ngay dưới: ghép lại thành một ô.

    Chỉ ghép khi hàng nhãn có chữ kiểu tổng/thanh toán, ô cuối có dạng tiền bị cắt (còn thiếu đúng một chữ số) và hàng dưới chỉ có MỘT ô một chữ số nằm dưới ô đó;
    nếu không, "2" là số lượng hay số khác (bảng món có hàng toàn số lượng) và để nguyên."""
    out: list[list[OcrBox]] = []
    i = 0
    while i < len(rows):
        row, nxt = rows[i], rows[i + 1] if i + 1 < len(rows) else None
        last = row[-1]
        if (nxt is not None and len(nxt) == 1 and re.fullmatch(r"\d", nxt[0].text.strip())
                and _WRAPPED_AMOUNT_RE.fullmatch(last.text.strip())
                and _TOTAL_LABEL_RE.search(_norm(" ".join(b.text for b in row[:-1])))
                and last.x0 <= (nxt[0].x0 + nxt[0].x1) / 2 <= last.x1 + 0.5 * (last.x1 - last.x0)):
            digit = nxt[0]
            out.append(row[:-1] + [OcrBox(last.text.strip() + digit.text.strip(), min(last.conf, digit.conf), last.x0, last.y0, max(last.x1, digit.x1), digit.y1)])
            i += 2
            continue
        out.append(row)
        i += 1
    return out


def _starts_with_stt(row: list[OcrBox], left: float, right: float) -> bool:
    """Hàng mở đầu bằng số thứ tự món (cột STT): ô trái nhất là số 1-2 chữ số nằm sát mép trái tờ giấy."""
    first = row[0]
    return bool(re.fullmatch(r"\d{1,2}[.)]?", first.text.strip())) and first.x0 < left + 0.15 * (right - left)


_GLUED_STT_RE = re.compile(r"(\d{1,2})\s?(?=[^\W\d_])")  # "1Soa tuoi", "5 Giay ve sinh": OCR dính (hoặc cách một dấu) số STT vào tên


def _glued_stt(row: list[OcrBox], left: float, right: float, expected: int) -> int | None:
    """Số STT dính vào đầu tên món. Chỉ nhận khi đúng số STT kế tiếp ("7Up" ở món đầu không phải STT 7), để khỏi cắt nhầm chữ số đầu tên hàng."""
    first = row[0]
    m = _GLUED_STT_RE.match(first.text.strip())
    return int(m.group(1)) if m and int(m.group(1)) == expected and first.x0 < left + 0.15 * (right - left) else None


def _price(row: list[OcrBox], price_x: float, skip_trailing: bool = False) -> tuple[OcrBox, int] | None:
    """Hộp ngoài cùng bên phải có dạng tiền và nằm ở cột giá.

    skip_trailing: trong bảng món, bỏ qua các ô không phải tiền ở mép phải (thuế suất "8%", "KKKNT", ghi chú tay)
    rồi lấy ô tiền gần nhất bên trái, miễn còn trong cột giá."""
    for box in reversed(row):
        if (box.x0 + box.x1) / 2 < price_x:
            return None
        found = [a for a in _amounts(box.text) if a >= _MIN_AMOUNT]
        if found:
            return box, found[-1]
        if not skip_trailing:
            return None
    return None


_QTY_RE = re.compile(r"^\d{1,3}(?:[.,]\d{1,3})?$")  # "2", "0.367"


def _unit_times_qty(row: list[OcrBox], price_box: OcrBox, amount: int) -> int | None:
    """Hàng đủ ba cột "đơn giá | số lượng | thành tiền" mà thành tiền chỉ lệch (đơn giá x số lượng) đúng MỘT chữ số: OCR đọc sai ("20.009" thay vì "20.000").

    Chỉ đoán khi bên trái ô giá có đúng một ô tiền (đơn giá) và đúng một ô số lượng. Cột khuyến mại (hai ô tiền) hay số lượng lẻ kiểu "0.367" (trông như tiền)
    thì bỏ qua. Kết quả chỉ được dùng khi nó làm tổng các món khớp tổng bill (xem extract_items): khuyến mại thật cũng có thể lệch một chữ số."""
    left = [b for b in row if b.x0 < price_box.x0 and _NUMERIC_RE.match(b.text)]
    money = [b for b in left if _MONEY_BOX_RE.fullmatch(b.text.strip())]
    qty = [b for b in left if all(b is not m for m in money) and _QTY_RE.fullmatch(b.text.strip())]
    if len(money) != 1 or len(qty) > 1:
        return None
    unit = max((a for a in _amounts(money[0].text) if a >= _MIN_AMOUNT), default=None)
    if unit is None:
        return None
    if qty:
        candidates = {round(unit * float(qty[0].text.strip().replace(",", ".")))}
    elif amount % unit == 0 and amount // unit <= _MAX_INFERRED_QTY:
        return None  # đã là bội số của đơn giá: không có gì để sửa
    else:
        # OCR bỏ mất ô số lượng (chữ số nhỏ sát đường kẻ): thử các bội số 1..20 của đơn giá; chỉ nhận khi có ĐÚNG MỘT bội số lệch thành tiền một chữ số.
        candidates = {unit * k for k in range(1, _MAX_INFERRED_QTY + 1)}
    near = {e for e in candidates if e != amount and _one_digit_apart(amount, e)}
    return near.pop() if len(near) == 1 else None


_MAX_INFERRED_QTY = 20


def _one_digit_apart(a: int, b: int) -> bool:
    x, y = str(a), str(b)
    return len(x) == len(y) and sum(c != d for c, d in zip(x, y)) == 1


_PAYABLE_ROW_RE = re.compile(r"tong thanh toan|phai tra|can thanh toan")
_TONG_CONG_ROW_RE = re.compile(r"\btong cong\b")  # nhãn tổng cuối của bill không in "Thanh toán"; chỉ dùng khi không có nhãn ở trên
_SUBTOTAL_ROW_RE = re.compile(r"tien hang|tam tinh")  # "Tổng cộng tiền hàng" là cộng tạm, chưa tính thuế/giảm giá
_CASH_ROW_RE = re.compile(r"khach dua|khach tra|tien mat|tien thua|thoi lai|tra lai|hinh thuc")


def payable_total(boxes: list[OcrBox]) -> int | None:
    """Số khách phải trả: số tiền nằm CÙNG HÀNG với nhãn "Tổng thanh toán" / "Phải trả" / "Cần thanh toán"; không có thì hàng "Tổng cộng".

    Đọc theo hàng nên không phụ thuộc thứ tự OCR đọc các dòng: luật theo dòng (extract.extract_total) phải đoán số nằm trước hay sau nhãn
    và đoán sai khi dòng ngay trên nhãn là một số khác (số thuế), rồi lấy nhầm thuế, tiền khách đưa, tiền thối, hay số lặp của món cuối.
    Hàng "Hình thức thanh toán: Tiền mặt", tiền khách đưa, tiền thối, "Tổng cộng tiền hàng" và "Tạm tính" không phải tổng. Nhiều hàng khớp
    thì lấy hàng cuối; nhãn thanh toán thắng nhãn "Tổng cộng" (cộng tạm trước thuế), và "Tổng cộng" chỉ nhận khi là hàng duy nhất. Không hàng nào khớp: None (giữ kết quả của luật theo dòng)."""
    if not boxes:
        return None
    payable = None
    tong_cong: list[int] = []
    for r in _rows(boxes):
        text = _norm(" ".join(b.text for b in r))
        if _CASH_ROW_RE.search(text):
            continue
        if _PAYABLE_ROW_RE.search(text):
            tier = "payable"
        elif _TONG_CONG_ROW_RE.search(text) and not _SUBTOTAL_ROW_RE.search(text):
            tier = "tong_cong"
        else:
            continue
        amount = next((a for box in reversed(r) for a in [x for x in _amounts(box.text, True) if x >= _MIN_AMOUNT][-1:]), None)
        if amount is not None:
            if tier == "payable":
                payable = amount
            else:
                tong_cong.append(amount)
    if payable is not None:
        return payable
    return tong_cong[0] if len(tong_cong) == 1 else None  # nhiều "Tổng cộng" là cộng riêng từng khối bảng (hóa đơn A4): không dòng nào là tổng bill


# Ô gộp tên món với phần số ở cuối ("8.Khau trang 10 chiec1x 25.000=25.000"): nhãn là phần đầu, đuôi là chuỗi số/dấu/x/= chạy tới cuối ô.
_MERGED_TAIL_RE = re.compile(r"^(?P<label>.*?)(?P<tail>\d[\d\s.,x×*=]*)(?P<unit>\s?(?:đ|d|vnd))?$", re.IGNORECASE)
_CALC_RE = re.compile(r"\d\s*[x×*]\s*\d[\d.,]*\s*=\s*\d")  # "SL x đơn giá = thành tiền": dấu hiệu của dòng món, không phải dòng tổng ("ONG CONG: 341.000")


def _split_merged_cells(boxes: list[OcrBox], price_x: float) -> list[OcrBox]:
    """Tách ô OCR gộp tên món với số tiền thành hai ô (tên | số), để số tiền nằm lại ở cột giá.

    Chỉ đụng ô mà tâm nằm TRƯỚC cột giá (ô tiền thật không ở đó nên số tiền trong ô sẽ bị bỏ sót), có ít nhất 3 chữ cái ở phần tên và đuôi
    đúng dạng dòng món "SL x đơn giá = thành tiền" với một số tiền hợp lệ. Dòng tổng ("ONG CONG: 341.000"), ngày ("22/10/2026") hay "500g" giữ nguyên.
    Ô tiền mới đặt trong cột giá vì số tiền luôn nằm ở mép phải ô.
    ponytail: tọa độ cắt chia theo số ký tự (chữ viết tay rộng hẹp không đều), chỉ cần đúng hàng và đúng bên của cột giá."""
    out: list[OcrBox] = []
    for b in boxes:
        text = b.text.rstrip()
        m = None if (b.x0 + b.x1) / 2 >= price_x else _MERGED_TAIL_RE.match(text)
        if m is None or sum(c.isalpha() for c in m["label"]) < 3 or not _CALC_RE.search(m["tail"])                 or not [a for a in _amounts(m["tail"]) if a >= _MIN_AMOUNT]:
            out.append(b)
            continue
        cut = b.x0 + (b.x1 - b.x0) * len(m["label"]) / len(text)
        out += [replace(b, text=m["label"].rstrip(), x1=cut), replace(b, text=text[len(m["label"]):], x0=max(cut, price_x))]
    return out


def extract_items(boxes: list[OcrBox], total: int | None) -> ItemsResult:
    if not boxes:
        return ItemsResult([], False)
    left, right = min(b.x0 for b in boxes), max(b.x1 for b in boxes)
    price_x = right - PRICE_COLUMN * (right - left)
    boxes = _split_merged_cells(boxes, price_x)
    raw = _rows(boxes)
    texts = [_norm(" ".join(b.text for b in r)).strip() for r in raw]

    # vùng lấy món: sau dòng tiêu đề cột. Tiêu đề phải đứng trước hàng món đầu tiên ("SL: 2" ở chân bill không phải
    # tiêu đề). Hàng món đầu tiên = ô tiền đúng dạng (có dấu nghìn) và không phải dòng tổng, nên mã KH, điểm tích lũy,
    # Nợ/Có, điện thoại, "Tổng tiền các đơn" ở đầu phiếu không chặn mất tiêu đề. Lấy tiêu đề gần bảng món nhất.
    # Bill không in tiêu đề cột (cửa hàng tiện lợi): bảng món bắt đầu từ hàng món đầu tiên, nhưng phải khép bằng
    # dòng cộng tạm/tổng (closed) và vẫn phải cộng đúng tổng bill; thiếu một trong hai thì ok=False.
    def money_row(i: int) -> bool:
        p = _price(raw[i], price_x, skip_trailing=True)
        return p is not None and bool(_MONEY_BOX_RE.fullmatch(p[0].text.strip())) \
            and not (_TOTAL_WORD_RE.search(texts[i]) or _SUBTOTAL_RE.search(texts[i]) or _FINAL_RE.search(texts[i]))
    first_priced = next((i for i in range(len(raw)) if money_row(i)), None)
    if first_priced is None:  # bill in giá không dấu nghìn ("30000"): giữ luật cũ, hàng có giá đầu tiên bất kỳ
        first_priced = next((i for i, r in enumerate(raw) if _price(r, price_x)), len(raw))
    # hàng có tiền ở cột giá ("Tổng cộng 50,000") là dòng tổng, không phải tiêu đề cột
    start = next((i + 1 for i in reversed(range(first_priced))
                  if _HEADER_RE.search(texts[i]) and _price(raw[i], price_x) is None), None)
    headerless = start is None
    if headerless:
        start = first_priced
    closed = False  # gặp dòng cộng tạm hoặc tổng cuối sau các món

    items: list[Item] = []
    count = 0  # số món thật (không tính giảm giá, thuế, phí)
    main: int | None = None
    pending: list[str] = []  # tên xuống dòng: hàng không giá ngay trên hàng có giá
    next_stt = 1  # số STT kế tiếp mong đợi (để nhận STT dính vào tên)
    after_stt_item = False  # hàng có giá vừa rồi mở đầu bằng số STT: hàng không giá kế tiếp (không STT) là mô tả của món đó
    tail = False  # đã qua dòng cộng tiền hàng: chỉ nhận thuế/phí và giảm giá cả bill tới dòng tổng cuối
    tail_discounts: list[Item] = []  # giảm giá cả bill ở vùng tổng: có bill giá từng dòng ĐÃ trừ khuyến mại, nên khoản này không được trừ lần nữa
    subtotal_amount: int | None = None  # số in ở dòng cộng tạm
    lines_sum_at_tail = 0  # tổng các dòng ngay trước dòng cộng tạm
    fixes: dict[int, int] = {}  # chỉ số món -> thành tiền suy ra từ đơn giá x số lượng (xem _unit_times_qty)
    for r, text in zip(raw[start:], texts[start:]):
        if _FINAL_RE.search(text):
            closed = True
            break
        if tail:
            p = _price(r, price_x)  # vùng tổng không phải bảng: chỉ lấy ô ngoài cùng bên phải
            if p is None:
                continue
            label = " ".join(b.text for b in r if b is not p[0]).strip()
            if _SURCHARGE_RE.search(_norm(label)) and not _PRE_TAX_RE.search(_norm(label)):
                items.append(Item(label, p[1], None, False))
            elif _NEGATIVE_RE.search(p[0].text) or _DISCOUNT_RE.search(_norm(label)):
                items.append(Item(label, -p[1], None, True))
                tail_discounts.append(items[-1])
            elif "tong cong" in text:
                break  # "Tổng cộng" sau dòng cộng tạm là tổng cuối
            # các hàng khác (tiền khách đưa, tiền thừa, cộng lặp lại) không thuộc tổng bill: bỏ
            continue
        if _SUBTOTAL_RE.search(text):
            tail = closed = True
            if (sub := _price(r, price_x)) is not None:
                subtotal_amount, lines_sum_at_tail = sub[1], sum(i.amount for i in items)
            continue
        p = _price(r, price_x, skip_trailing=True)
        cells = r if p is None else r[:next(k for k, b in enumerate(r) if b is p[0])]  # ô bên phải giá không thuộc tên
        words = [b.text for b in cells if not _NUMERIC_RE.match(b.text)]
        own = " ".join(words).strip()
        if _PAYMENT_RE.search(_norm(own)):
            break  # tiền khách trả không phải món; không khép bảng (closed giữ nguyên)
        if p is not None and not own and not pending and count >= 2 and p[1] == sum(i.amount for i in items):
            tail = closed = True  # số trần bằng đúng tổng đang cộng: dòng cộng tiền hàng in tách khỏi nhãn của nó
            continue
        addon = own.startswith("+")  # phụ kiện xét theo chữ của chính hàng có giá, không theo tên mang xuống
        stt = _starts_with_stt(r, left, right)
        glued = None if stt else _glued_stt(r, left, right, next_stt)
        if glued is not None:
            stt = True
        if stt:
            next_stt += 1
        if p is None:
            if after_stt_item and not stt:
                continue  # bill có cột STT: dòng mô tả dưới món ("hương vị tôm chua cay 67g") không phải phần đầu tên món sau
            if not addon:  # "+ Ít đá" không giá là ghi chú tùy chọn: bỏ, không nối vào tên món sau
                pending += words  # hàng chỉ có số nhỏ (cột số lượng) không thêm gì
            continue
        if glued is not None:
            own = _GLUED_STT_RE.sub("", own, count=1)  # bỏ số STT dính ở đầu tên: "1Soa tuoi" -> "Soa tuoi"
        name = " ".join(pending + [own.lstrip("+ ").strip() if addon else own]).strip()
        pending = []
        after_stt_item = stt
        if _NEGATIVE_RE.search(p[0].text) or _DISCOUNT_RE.search(_norm(name)):
            items.append(Item(name, -p[1], None, True))
        else:
            count += 1
            if not addon:
                main = len(items)
            items.append(Item(name, p[1], main if addon else None, False))
            if (fixed := _unit_times_qty(r, p[0], p[1])) is not None:
                fixes[len(items) - 1] = fixed

    # tổng các món + thuế/phí - giảm giá phải bằng đúng tổng bill; thuế/phí không tính vào số món
    def balanced(candidate: list[Item]) -> bool:
        return total is not None and count >= 2 and sum(i.amount for i in candidate) == total and (closed or not headerless)

    def repaired(candidate: list[Item]) -> list[Item]:
        return [replace(it, amount=fixes[k]) if k in fixes else it for k, it in enumerate(candidate)]

    # Không khớp: thử (a) bỏ giảm giá cả bill đã nằm sẵn trong giá từng dòng, (b) sửa thành tiền OCR đọc lệch một chữ số, (c) cả hai.
    # Chỉ nhận khi PHÉP SỬA làm tổng khớp đúng tổng bill: có bằng chứng số học mới đổi số đã đọc.
    chosen = items
    if total is not None and not balanced(items):
        # Giảm giá cuối chỉ là "đã nằm trong giá từng dòng" khi tạm tính in trên bill đúng bằng tổng các dòng cộng giảm giá (906.500 + 22.500 = 929.000).
        # Tạm tính bằng đúng tổng các dòng thì giá dòng là giá gốc và giảm giá phải được trừ.
        netted = bool(tail_discounts) and subtotal_amount == lines_sum_at_tail + sum(-d.amount for d in tail_discounts)
        without_tail = [i for i in items if all(i is not d for d in tail_discounts)] if netted else items
        chosen = next((c for c in (without_tail, repaired(items), repaired(without_tail)) if balanced(c)), items)
    return ItemsResult(chosen, balanced(chosen), sum(i.amount for i in repaired(items)) if fixes else None)
