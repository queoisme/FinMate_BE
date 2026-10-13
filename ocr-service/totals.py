"""Chọn tổng tiền hóa đơn bằng RÀNG BUỘC SỐ HỌC thay vì tin nhãn đứng cạnh con số.

Mọi hóa đơn đều cộng trừ được. Số khách phải trả T phải thỏa ít nhất một trong:
  - T = tiền khách đưa - tiền thối lại
  - T = tạm tính (hoặc "tổng tiền hàng") + thuế + phí phục vụ - giảm giá
  - T = tổng các dòng món đã tách (món + thuế/phí - giảm giá, ``items.extract_items``)
Ứng viên là các số in trên bill thỏa ràng buộc đó; số nào không thỏa (số thuế, số lặp của món cuối, tiền khách đưa) tự bị loại, nên không
cần một luật riêng cho từng kiểu bố cục. Không ứng viên nào thỏa thì trả giá trị dự phòng và nói rõ là chưa kiểm chứng.
Đọc theo HÀNG (nhãn + số cùng hàng) nên không phụ thuộc thứ tự OCR đọc các dòng.
"""
import re
from dataclasses import dataclass

from extract import _MIN_AMOUNT, _amounts, _norm
from items import _rows, extract_items
from ocr import OcrBox

_CASH = re.compile(r"khach dua|khach tra|tien khach|tien nhan")
_CHANGE = re.compile(r"thoi lai|tien thua|tra lai|tien thoi")
_TAX = re.compile(r"\bgtgt\b|\bvat\b|\bthue\b|\bthu[&€@$]")  # "Thu€ GTGT": OCR hay đọc "Thuế" thành "Thu€"
_SERVICE = re.compile(r"phi phuc vu|phi dich vu|service")
_DISCOUNT = re.compile(r"giam|chiet khau|khuyen mai|voucher")
_SUBTOTAL = re.compile(r"tam tinh|\btien hang|cong tien hang")  # "Tiền hàng", "Tổng tiền hàng", "Cộng tiền hàng": cộng tạm trước thuế
_TOTAL = re.compile(r"tong thanh toan|phai tra|can thanh toan|phai thu|tong cong|tong tien|tong so tien")
_PAYMENT_METHOD = re.compile(r"hinh thuc")
_TOTAL_ISH = re.compile(r"tong|cong|toan|phai|tra|thu")  # nhãn lạ vẫn có thể là tổng bị OCR đọc hỏng ("Tong c0ng", "Cộng"); tên món thì không  # "Hình thức thanh toán: Tiền mặt": không phải số tiền


@dataclass(frozen=True)
class _Row:
    role: str  # cash | change | tax | service | discount | subtotal | total | other
    amount: int
    label: str = ""  # chữ của hàng, đã bỏ dấu


def _role(text: str) -> str:
    if _PAYMENT_METHOD.search(text):
        return "skip"
    for role, pattern in (("cash", _CASH), ("change", _CHANGE), ("tax", _TAX), ("service", _SERVICE),
                          ("discount", _DISCOUNT), ("subtotal", _SUBTOTAL), ("total", _TOTAL)):
        if pattern.search(text):
            return role
    return "other"


def _amount_rows(boxes: list[OcrBox]) -> list[_Row]:
    """Mỗi hàng có chữ và số tiền (ô ngoài cùng bên phải có số >= mức tối thiểu) thành một dòng có vai trò."""
    out: list[_Row] = []
    for r in _rows(boxes):
        text = _norm(" ".join(b.text for b in r))
        if not any(c.isalpha() for c in text):
            continue
        amount = next((a for box in reversed(r) for a in [x for x in _amounts(box.text) if x >= _MIN_AMOUNT][-1:]), None)
        role = _role(text)
        if role == "tax" or ("gtgt" in re.sub(r"[^a-z0-9]", "", text) and role == "other"):
            role = "tax"  # nhãn thuế viết dính ("ThueGTGT8%)") không có ranh giới từ
        if amount is not None and role != "skip":
            out.append(_Row(role, amount, text))
    return out


def choose_total(boxes: list[OcrBox], fallback: int | None = None) -> tuple[int | None, bool]:
    """(tổng, đã kiểm chứng bằng số học). Chưa kiểm chứng được thì trả ``fallback``."""
    if not boxes:
        return fallback, False
    rows = _amount_rows(boxes)
    cash = [r.amount for r in rows if r.role == "cash"]
    change = [r.amount for r in rows if r.role == "change"]
    tax = sum(r.amount for r in rows if r.role in ("tax", "service"))
    discount = sum(r.amount for r in rows if r.role == "discount")
    subtotals = [r.amount for r in rows if r.role == "subtotal"]

    targets = {c - g for c in cash for g in change if c > g}
    targets |= {s + tax - discount for s in subtotals}
    try:
        items = extract_items(boxes, None).items
    except Exception:  # noqa: BLE001 - tách món lỗi thì chỉ mất một ràng buộc, không được làm hỏng việc chọn tổng
        items = []
    if sum(1 for i in items if i.parent is None and not i.is_discount) >= 2:
        targets.add(sum(i.amount for i in items))
    targets = {t for t in targets if t >= _MIN_AMOUNT}

    # Ứng viên: số thỏa ràng buộc nằm ở dòng nhãn tổng hoặc dòng nhãn lạ ("Tong c0ng", "Cộng"). Dòng tạm tính/thuế/giảm giá/tiền khách đưa/tiền thối
    # không được làm tổng dù trùng giá trị (tạm tính = tổng khi không có thuế, nhưng khi nhãn tổng in số khác thì đó là lỗi, không phải tổng).
    verified = [r.amount for r in rows if r.amount in targets and (r.role == "total" or (r.role == "other" and _TOTAL_ISH.search(r.label)))]
    if verified:
        return verified[-1], True
    return fallback, False
