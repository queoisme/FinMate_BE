"""Chuẩn bị hộp chữ trước khi phân tích: ghép nhãn với giá trị viết tay cùng hàng, và nhân số tiền người bán viết tắt theo nghìn đồng.

Gọi một lần ngay sau khi đọc ảnh (main.analyze_bill và benchmark/run_databill.py), để chữ, điểm giống hóa đơn, tổng và tách món cùng nhìn một dữ liệu.
"""
import re
from dataclasses import replace

from classification import data_files
from statistics import median

from parsing.extract import _TITLE_MARKERS, _alnum, _norm
from parsing.items import _MONEY_BOX_RE, PRICE_COLUMN, ROW_TOLERANCE
from reading.ocr import OcrBox

_MIN_SHORTHAND_CELLS = data_files.threshold("parsing.prepare._MIN_SHORTHAND_CELLS")
_MIN_VALUE_LETTERS = data_files.threshold("parsing.prepare._MIN_VALUE_LETTERS")
_BARE_INT = re.compile(r"\d{1,3}")  # "44": số nguyên 1-999, không dấu nghìn
_TAIL_INT = re.compile(r"^(?P<head>.*\S)\s+(?P<tail>\d{1,3})$")  # ô gộp "1 quả ( 1,15 ) 24": chữ rồi một số nguyên ở cuối


def join_field_values(boxes: list[OcrBox]) -> list[OcrBox]:
    """Ô nhãn kết thúc bằng ":" nhận ô chữ cùng hàng ở bên phải thành một dòng "Nhãn: giá trị".

    Google tách giá trị viết tay khỏi nhãn in; chữ viết tay cao hơn nên hộp của nó lệch lên trên và đứng một mình như một dòng tên. Bộ chọn tên cửa hàng
    đã bỏ dòng "Nhãn: giá trị" (Paddle đọc liền cả dòng), nên ghép lại là đưa Google về cùng dạng. Chỉ ghép giá trị có chữ: số điện thoại, dấu chấm điền thì giữ riêng.
    Thứ tự các ô khác giữ nguyên (luật đọc tổng theo chữ phụ thuộc thứ tự dòng)."""
    if not boxes:
        return boxes
    tol = ROW_TOLERANCE * median(b.y1 - b.y0 for b in boxes)
    markers = [_alnum(m) for m in _TITLE_MARKERS]

    def is_value(b: OcrBox) -> bool:  # có chữ, không phải nhãn khác ("...:") và không phải tiêu đề in ("HÓA ĐƠN BÁN LẺ") đứng cùng hàng với nhãn
        return sum(c.isalpha() for c in b.text) >= _MIN_VALUE_LETTERS and not b.text.rstrip().endswith(":") and not any(m in _alnum(_norm(b.text)) for m in markers)

    value_of: dict[int, int] = {}  # chỉ số nhãn -> chỉ số giá trị
    for i, label in enumerate(boxes):
        if label.text.rstrip().endswith(":"):
            cy = (label.y0 + label.y1) / 2
            nearest = min((j for j, b in enumerate(boxes) if j != i and j not in value_of.values() and b.x0 >= label.x1 and abs((b.y0 + b.y1) / 2 - cy) < tol and is_value(b)),
                          key=lambda j: boxes[j].x0, default=None)
            if nearest is not None:
                value_of[i] = nearest
    out: list[OcrBox] = []
    for i, b in enumerate(boxes):
        if i in value_of.values():
            continue  # đã ghép vào nhãn của nó, dù hộp chữ viết tay đứng trước nhãn trong thứ tự
        if i in value_of:
            v = boxes[value_of[i]]
            b = replace(b, text=f"{b.text.rstrip()} {v.text}", x1=max(b.x1, v.x1), y0=min(b.y0, v.y0), y1=max(b.y1, v.y1))
        out.append(b)
    return out


def scale_thousands(boxes: list[OcrBox]) -> tuple[list[OcrBox], bool]:
    """Cột tiền toàn số nguyên nhỏ ("44", "34") là cách người bán viết tắt nghìn đồng: nhân thành "44.000". Trả (hộp, đã nhân?).

    Chỉ nhân khi cột tiền có đủ _MIN_SHORTHAND_CELLS ô như vậy và KHÔNG ô nào có dạng tiền đầy đủ (dấu nghìn): bill thường có số tiền in nên không bao giờ bị nhân.
    Ô gộp nuốt mất thành tiền ("1 quả ( 1,15 ) 24", nằm từ cột số lượng sang cột tiền) được tách trả "24" về cột tiền trước khi nhân."""
    if not boxes:
        return boxes, False
    left, right = min(b.x0 for b in boxes), max(b.x1 for b in boxes)
    price_x = right - PRICE_COLUMN * (right - left)
    column = [b for b in boxes if b.x0 >= price_x]
    if sum(1 for b in column if _BARE_INT.fullmatch(b.text.strip())) < _MIN_SHORTHAND_CELLS or any(_MONEY_BOX_RE.fullmatch(b.text.strip()) for b in column):
        return boxes, False
    out: list[OcrBox] = []
    for b in boxes:
        m = _TAIL_INT.match(b.text.strip()) if b.x0 < price_x < b.x1 else None
        if m:
            char = (b.x1 - b.x0) / max(len(b.text.strip()), 1)
            tail_x0 = b.x1 - len(m["tail"]) * char
            out += [replace(b, text=m["head"], x1=tail_x0 - char), replace(b, text=m["tail"], x0=tail_x0)]
        else:
            out.append(b)
    return [replace(b, text=f"{b.text.strip()}.000") if b.x0 >= price_x and _BARE_INT.fullmatch(b.text.strip()) else b for b in out], True


def prepare(boxes: list[OcrBox]) -> tuple[list[OcrBox], bool]:
    return scale_thousands(join_field_values(boxes))
