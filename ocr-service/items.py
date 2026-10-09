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
# thuế/phí cộng sau dòng cộng tiền hàng; "tổng tiền chưa thuế/VAT", "trước thuế" là cộng tạm, không phải thuế
_SURCHARGE_RE = re.compile(r"\bthue\b|\bvat\b|phi phuc vu|phi dich vu|service")
_PRE_TAX_RE = re.compile(r"\bchua\b|\btruoc\b")
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
    # Giới hạn: bill không có tiêu đề cột thì không tách món, ok=False.
    def money_row(i: int) -> bool:
        p = _price(raw[i], price_x, skip_trailing=True)
        return p is not None and bool(_MONEY_BOX_RE.fullmatch(p[0].text.strip())) \
            and not (_TOTAL_WORD_RE.search(texts[i]) or _SUBTOTAL_RE.search(texts[i]) or _FINAL_RE.search(texts[i]))
    first_priced = next((i for i in range(len(raw)) if money_row(i)), None)
    if first_priced is None:  # bill in giá không dấu nghìn ("30000"): giữ luật cũ, hàng có giá đầu tiên bất kỳ
        first_priced = next((i for i, r in enumerate(raw) if _price(r, price_x)), len(raw))
    start = next((i + 1 for i in reversed(range(first_priced)) if _HEADER_RE.search(texts[i])), None)
    if start is None:
        return ItemsResult([], False)

    items: list[Item] = []
    count = 0  # số món thật (không tính giảm giá, thuế, phí)
    main: int | None = None
    pending: list[str] = []  # tên xuống dòng: hàng không giá ngay trên hàng có giá
    tail = False  # đã qua dòng cộng tiền hàng: chỉ nhận thuế/phí và giảm giá cả bill tới dòng tổng cuối
    for r, text in zip(raw[start:], texts[start:]):
        if _FINAL_RE.search(text):
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
            tail = True
            continue
        p = _price(r, price_x, skip_trailing=True)
        cells = r if p is None else r[:next(k for k, b in enumerate(r) if b is p[0])]  # ô bên phải giá không thuộc tên
        words = [b.text for b in cells if not _NUMERIC_RE.match(b.text)]
        own = " ".join(words).strip()
        if p is not None and not own and not pending and count >= 2 and p[1] == sum(i.amount for i in items):
            tail = True  # số trần bằng đúng tổng đang cộng: dòng cộng tiền hàng in tách khỏi nhãn của nó
            continue
        addon = own.startswith("+")  # phụ kiện xét theo chữ của chính hàng có giá, không theo tên mang xuống
        if p is None:
            if not addon:  # "+ Ít đá" không giá là ghi chú tùy chọn: bỏ, không nối vào tên món sau
                pending += words  # hàng chỉ có số nhỏ (cột số lượng) không thêm gì
            continue
        name = " ".join(pending + [own.lstrip("+ ").strip() if addon else own]).strip()
        pending = []
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
    ok = total is not None and count >= 2 and sum(i.amount for i in items) == total
    return ItemsResult(items, ok)
