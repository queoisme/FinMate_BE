"""Bóc tách từng món (tên + giá) từ các hộp chữ OCR của hóa đơn giấy."""
import re
from dataclasses import dataclass
from statistics import median

from extract import _MIN_AMOUNT, _amounts, _norm
from ocr import OcrBox

ROW_TOLERANCE = 0.6  # lệch tâm dọc dưới 0,6 lần chiều cao hộp trung vị thì cùng hàng
PRICE_COLUMN = 0.35  # giá nằm trong 35% bên phải tờ giấy

_HEADER_RE = re.compile(r"\b(?:sl|tong|ten hang|mat hang|don gia|thanh tien|so luong)\b")
# Dòng cộng tạm: hết danh sách món nhưng tổng bill còn có thể cộng thuế/phí, trừ giảm giá cả bill ở dưới.
# ponytail: "^t[o0-9]ng" bắt cả "T6ng" do OCR đọc sai; tên món mở đầu bằng "Tông..." sẽ bị coi là dòng tổng
_SUBTOTAL_RE = re.compile(r"^t[o0-9]ng\b|tong cong|thanh tien|c[o0]ng tien hang|t[a4]m tinh|^tien hang")
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
_TOTAL_WORD_RE = re.compile(r"\bt[o0-9]ng\b")  # "Tổng" ở bất kỳ đâu trong hàng
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
    return [sorted(r, key=lambda b: b.x0) for r in rows]


def _starts_with_stt(row: list[OcrBox], left: float, right: float) -> bool:
    """Hàng mở đầu bằng số thứ tự món (cột STT): ô trái nhất là số 1-2 chữ số nằm sát mép trái tờ giấy."""
    first = row[0]
    return bool(re.fullmatch(r"\d{1,2}[.)]?", first.text.strip())) and first.x0 < left + 0.15 * (right - left)


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


def extract_items(boxes: list[OcrBox], total: int | None) -> ItemsResult:
    if not boxes:
        return ItemsResult([], False)
    left, right = min(b.x0 for b in boxes), max(b.x1 for b in boxes)
    price_x = right - PRICE_COLUMN * (right - left)
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
    after_stt_item = False  # hàng có giá vừa rồi mở đầu bằng số STT: hàng không giá kế tiếp (không STT) là mô tả của món đó
    tail = False  # đã qua dòng cộng tiền hàng: chỉ nhận thuế/phí và giảm giá cả bill tới dòng tổng cuối
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
            elif "tong cong" in text:
                break  # "Tổng cộng" sau dòng cộng tạm là tổng cuối
            # các hàng khác (tiền khách đưa, tiền thừa, cộng lặp lại) không thuộc tổng bill: bỏ
            continue
        if _SUBTOTAL_RE.search(text):
            tail = closed = True
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
        if p is None:
            if after_stt_item and not stt:
                continue  # bill có cột STT: dòng mô tả dưới món ("hương vị tôm chua cay 67g") không phải phần đầu tên món sau
            if not addon:  # "+ Ít đá" không giá là ghi chú tùy chọn: bỏ, không nối vào tên món sau
                pending += words  # hàng chỉ có số nhỏ (cột số lượng) không thêm gì
            continue
        name = " ".join(pending + [own.lstrip("+ ").strip() if addon else own]).strip()
        pending = []
        after_stt_item = stt
        if _NEGATIVE_RE.search(p[0].text) or _DISCOUNT_RE.search(_norm(name)):
            items.append(Item(name, -p[1], None, True))
        elif addon:
            count += 1
            items.append(Item(name, p[1], main, False))
        else:
            count += 1
            main = len(items)
            items.append(Item(name, p[1], None, False))

    # tổng các món + thuế/phí - giảm giá phải bằng đúng tổng bill; thuế/phí không tính vào số món
    ok = total is not None and count >= 2 and sum(i.amount for i in items) == total and (closed or not headerless)
    return ItemsResult(items, ok)
