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

from classification import data_files
from parsing import extract  # ngưỡng đọc qua extract.<tên> lúc dùng: bản admin thay nóng không để lại giá trị cũ ở đây
from parsing.extract import _amounts, _norm
from parsing.items import _rows, extract_items, payable_total
from reading.ocr import OcrBox

_alt = data_files.shared_alternation  # từ vựng chung ocr-service / ai-service / backend: data/shared_words.json
_CASH = re.compile(_alt("cash_given"))
_CHANGE = re.compile(_alt("change"))
_TAX = re.compile(rf"\b(?:{_alt('tax')})\b|{_alt('tax_glued')}|\b(?:{_alt(extra_regex=('tax_ocr_misread',))})")  # "Thu€ GTGT": OCR hay đọc "Thuế" thành "Thu€"
_SERVICE = re.compile(_alt("service", "service_glued"))
_DISCOUNT = re.compile(_alt("discount"))
_SUBTOTAL = re.compile(r"tam tinh|\btien hang|cong tien hang")  # "Tiền hàng", "Tổng tiền hàng", "Cộng tiền hàng": cộng tạm trước thuế
_TOTAL = re.compile(r"tong thanh toan|phai tra|can thanh toan|phai thu|tong cong|tong tien|tong so tien")
_PAYMENT_METHOD = re.compile(_alt("payment_label"))
_TAX_GLUED = data_files.shared_words("tax_glued")  # nhãn thuế viết dính ("ThueGTGT8%)") không có ranh giới từ
_MIN_NAME_LETTERS = data_files.threshold("parsing.totals._MIN_NAME_LETTERS")
_TOTAL_ISH = re.compile(r"tong|cong|toan|phai|tra|thu")  # nhãn lạ vẫn có thể là tổng bị OCR đọc hỏng ("Tong c0ng", "Cộng"); tên món thì không  # "Hình thức thanh toán: Tiền mặt": không phải số tiền


@dataclass(frozen=True)
class _Row:
    role: str  # cash | change | tax | service | discount | subtotal | total | bare (chỉ có số, không chữ) | other
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
        bare = not any(c.isalpha() for c in text)  # hàng chỉ có số (mẫu in sẵn không in chữ "Tổng"): chỉ dùng khi cộng đúng các món, xem choose_total
        amount = next((a for box in reversed(r) for a in [x for x in _amounts(box.text) if x >= extract._MIN_AMOUNT][-1:]), None)
        role = "bare" if bare else _role(text)
        if role == "tax" or (role == "other" and any(word in re.sub(r"[^a-z0-9]", "", text) for word in _TAX_GLUED)):
            role = "tax"
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
    split, items = _split_items(boxes)
    items_sum = None
    if sum(1 for i in items if i.parent is None and not i.is_discount) >= 2:
        items_sum = sum(i.amount for i in items)
        targets.add(items_sum)
        if split.fixed_total is not None:  # thành tiền OCR đọc lệch một chữ số đã sửa bằng đơn giá x số lượng: tổng đã sửa cũng là ứng viên
            targets.add(split.fixed_total)
    targets = {t for t in targets if t >= extract._MIN_AMOUNT}

    # Ứng viên: số thỏa ràng buộc nằm ở dòng nhãn tổng hoặc dòng nhãn lạ ("Tong c0ng", "Cộng"). Dòng tạm tính/thuế/giảm giá/tiền khách đưa/tiền thối
    # không được làm tổng dù trùng giá trị (tạm tính = tổng khi không có thuế, nhưng khi nhãn tổng in số khác thì đó là lỗi, không phải tổng).
    # Hàng chỉ có số (không chữ) cũng được, nhưng chỉ khi nó bằng ĐÚNG tổng các món: bằng chứng số học đủ mạnh khi bill không in nhãn "Tổng".
    verified = [r.amount for r in rows if r.amount in targets and (r.role == "total" or (r.role == "other" and _TOTAL_ISH.search(r.label))
                                                                  or (r.role == "bare" and r.amount == items_sum))]
    if verified:
        return verified[-1], True
    return fallback, False


def _split_items(boxes: list[OcrBox]):
    try:
        split = extract_items(boxes, None)
        return split, split.items
    except Exception:  # noqa: BLE001 - tách món lỗi thì chỉ mất một ràng buộc, không được làm hỏng việc chọn tổng
        return None, []


def resolve_total(boxes: list[OcrBox], texts: list[str]) -> tuple[int | None, bool, str]:
    """Tổng của bill: ràng buộc số học (``choose_total``) > số đọc theo hàng/theo chữ > tổng các món khi bill KHÔNG in tổng nào.

    Trả (tổng, đã kiểm chứng, nguồn): nguồn "items" là tổng các món, dự phòng cuối, luôn chưa kiểm chứng (không có số in nào để đối chiếu): người gọi phải nhắc người dùng kiểm tra; "read" là các cách còn lại."""
    total, verified = choose_total(boxes, fallback=payable_total(boxes))
    if total is None:
        guess, source = extract._total_with_source(texts)
        _, items = _split_items(boxes)
        main = [i for i in items if i.parent is None and not i.is_discount]
        # Số đoán theo chữ không có nhãn Tổng mà MỌI lần nó xuất hiện đều là giá của một dòng món (bill không in tổng, hai món trùng giá) thì không phải tổng.
        # Nếu còn một lần xuất hiện ngoài các dòng món (ở vùng tổng) thì đó vẫn là bằng chứng của một tổng in ra: giữ.
        in_text = sum(1 for t in texts for a in extract._amounts(t) if a == guess)
        readable = all(sum(c.isalpha() for c in i.name) >= _MIN_NAME_LETTERS for i in main)  # bảng món đọc ra rác (món không tên) thì cộng của nó không đáng tin
        if len(main) >= 2 and readable and (guess is None or (source != "label" and sum(1 for i in main if i.amount == guess) >= in_text)):
            return sum(i.amount for i in items), False, "items"
    return total, verified, "read"
