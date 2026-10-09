"""Bóc tách từng món (tên + giá) từ các hộp chữ OCR của hóa đơn giấy."""
import re
from dataclasses import dataclass
from statistics import median

from extract import _MIN_AMOUNT, _amounts, _norm
from ocr import OcrBox

ROW_TOLERANCE = 0.6  # lệch tâm dọc dưới 0,6 lần chiều cao hộp trung vị thì cùng hàng
PRICE_COLUMN = 0.35  # giá nằm trong 35% bên phải tờ giấy

_HEADER_RE = re.compile(r"\b(?:sl|tong|ten hang|mat hang|don gia|thanh tien|so luong)\b")
# ponytail: "^t[o0-9]ng" bắt cả "T6ng" do OCR đọc sai; tên món mở đầu bằng "Tông..." sẽ bị coi là dòng tổng
_END_RE = re.compile(r"^t[o0-9]ng\b|tong cong|thanh tien|thanh toan|phai tra|total")
_DISCOUNT_RE = re.compile(r"giam|chiet khau|voucher|khuyen mai")
_NUMERIC_RE = re.compile(r"^[\d\s.,x×*+\-đdĐD]*$")  # số lượng, đơn giá: không phải chữ của tên
_NEGATIVE_RE = re.compile(r"-\s*\d")


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


def _price(row: list[OcrBox], price_x: float) -> tuple[OcrBox, int] | None:
    """Hộp ngoài cùng bên phải có dạng tiền và nằm ở cột giá."""
    last = row[-1]
    found = [a for a in _amounts(last.text) if a >= _MIN_AMOUNT]
    if found and (last.x0 + last.x1) / 2 >= price_x:
        return last, found[-1]
    return None


def extract_items(boxes: list[OcrBox], total: int | None) -> ItemsResult:
    if not boxes:
        return ItemsResult([], False)
    left, right = min(b.x0 for b in boxes), max(b.x1 for b in boxes)
    price_x = right - PRICE_COLUMN * (right - left)
    rows = [(r, _price(r, price_x)) for r in _rows(boxes)]

    # vùng lấy món: sau dòng tiêu đề cột tới dòng tổng đầu tiên. Tiêu đề phải đứng trước hàng có giá đầu tiên
    # ("SL: 2" ở chân bill không phải tiêu đề). Giới hạn: bill không có tiêu đề cột thì không tách món, ok=False.
    first_priced = next((i for i, (_, p) in enumerate(rows) if p is not None), len(rows))
    start = next((i + 1 for i, (r, p) in enumerate(rows[:first_priced])
                  if _HEADER_RE.search(_norm(" ".join(b.text for b in r)))), None)
    if start is None:
        return ItemsResult([], False)

    items: list[Item] = []
    main: int | None = None
    pending: list[str] = []  # tên xuống dòng: hàng không giá ngay trên hàng có giá
    for r, p in rows[start:]:
        text = " ".join(b.text for b in r)
        if _END_RE.search(_norm(text).strip()):
            break
        words = [b.text for b in r if (p is None or b is not p[0]) and not _NUMERIC_RE.match(b.text)]
        own = " ".join(words).strip()
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
            items.append(Item(name, p[1], main, False))
        else:
            main = len(items)
            items.append(Item(name, p[1], None, False))

    count = sum(not i.is_discount for i in items)
    ok = total is not None and count >= 2 and sum(i.amount for i in items) == total
    return ItemsResult(items, ok)
