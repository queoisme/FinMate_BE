"""Đọc chữ từ ảnh hóa đơn bằng PaddleOCR (tiếng Việt), chạy tại máy."""
import io
from difflib import SequenceMatcher
import os
import re
from dataclasses import dataclass
from functools import lru_cache
from statistics import median

import cv2
import numpy as np
from PIL import Image

from reading.crop import crop_receipt
from classification.brands import BRAND_CATEGORIES, WHOLE_WORD_BRANDS
from parsing.extract import _alnum, _norm
from reading.quality import assess_quality, enhance
from reading import google_vision, vietocr_onnx
from reading.text_clean import clean_ocr_text


class NoTextError(Exception):
    """Ảnh không đọc ra được chữ nào."""


@lru_cache(maxsize=1)
def _engine():
    from paddleocr import PaddleOCR  # nạp lười: lần đầu tải model, các test không cần

    return PaddleOCR(use_angle_cls=True, lang="vi", show_log=False)


@dataclass(frozen=True)
class OcrBox:
    """Một dòng chữ kèm hộp (tọa độ trên ảnh đã cắt/phóng to dùng để đọc, ĐÃ CHỈNH NGHIÊNG bởi _deskew:
    x giữ nguyên, y là tọa độ sau khi xoay trang về ngang, nên hàng in thẳng có cùng y dù ảnh chụp lệch)."""
    text: str
    conf: float
    x0: float
    y0: float
    x1: float
    y1: float


def _cut_off(text: str) -> bool:
    """Dòng chữ bị rơi ký tự cuối có dấu: kết thúc bằng một chữ cái lẻ ("banh khoai m")."""
    last = text.split()[-1] if text.split() else ""
    return len(text.split()) >= 3 and len(last) == 1 and last.isalpha() and last.lower() not in "ai"


def repair_cut_off(lines: list[tuple[str, float]], retry: list[tuple[str, float]]) -> list[tuple[str, float]]:
    """Thay dòng bị cụt bằng dòng dài hơn 1-2 ký tự từ lần đọc lại ở kích thước khác (cùng phần đầu)."""
    out = []
    for text, conf in lines:
        if _cut_off(text):
            head = text.lower()
            better = next((t for t, c in retry if t.lower().startswith(head) and 1 <= len(t) - len(text) <= 2), None)
            if better:
                text = better
        out.append((text, conf))
    return out


_VIET_HEAD_LINES = 8  # tên cửa hàng nằm ở đầu phiếu: chỉ đọc lại vài dòng đầu cho nhanh


def _use_vietocr() -> bool:
    return os.environ.get("FINMATE_VIETOCR", "1") != "0" and vietocr_onnx.available()  # đặt 0 để tắt


def _literal_brand(text: str) -> str | None:
    """Thương hiệu có mặt NGUYÊN VĂN (cả dạng viết liền) trong dòng chữ; khớp gần do OCR sai một ký tự không tính."""
    norm = _norm(text)
    def literal(b: str) -> bool:
        if b in WHOLE_WORD_BRANDS:
            return re.search(rf"(?<![a-z0-9]){re.escape(b)}(?![a-z0-9])", norm) is not None
        return b in norm or (len(_alnum(b)) >= 6 and _alnum(b) in _alnum(norm))

    return next((b for b in BRAND_CATEGORIES if literal(b)), None)


def _prefer(viet: str, paddle: str) -> str:
    """Phân xử giữa hai kết quả: VietOCR chỉ được thêm dấu (chữ gốc y hệt Paddle) — trừ khi nó đọc ra đúng một thương hiệu
    đã biết mà Paddle đọc sai ("co.cpmart" -> "coopmart", "N/inMart" -> "Winmart"). Mọi trường hợp lệch khác giữ Paddle:
    đo trên data-bill thì VietOCR đọc sai hơn Paddle ở chữ in nhỏ ("PizzaHut" -> "Pizzahd")."""
    same = _alnum(_norm(viet)) == _alnum(_norm(paddle)) != ""
    close = SequenceMatcher(None, _alnum(_norm(viet)), _alnum(_norm(paddle))).ratio() >= 0.7  # chặn VietOCR bịa chữ
    if not same and not (close and _literal_brand(viet) and not _literal_brand(paddle)):
        return paddle
    return viet.upper() if paddle.isupper() else viet  # VietOCR hay đọc chữ IN HOA thành chữ thường


def _reread_with_vietocr(bgr, box, text: str) -> str:
    """Đọc lại dòng chữ bằng VietOCR để có đủ dấu; dòng nhiều chữ số (mã, số tiền) giữ kết quả Paddle vì VietOCR hay nhầm 0/O."""
    letters = sum(c.isalpha() for c in text)
    digits = sum(c.isdigit() for c in text)
    if letters < 3 or digits > 0.3 * (letters + digits):
        return text
    xs, ys = [p[0] for p in box], [p[1] for p in box]
    h, w = bgr.shape[:2]
    crop = bgr[max(int(min(ys)) - 2, 0):min(int(max(ys)) + 2, h), max(int(min(xs)) - 2, 0):min(int(max(xs)) + 2, w)]
    if crop.size == 0:
        return text
    viet = vietocr_onnx.recognize(Image.fromarray(crop[:, :, ::-1]))
    # VietOCR hay bịa chữ trên dòng khó: chỉ nhận khi gần như trùng (không tính dấu) với kết quả Paddle,
    # tức là chỉ dùng nó để thêm dấu và sửa chữ cuối bị rơi.
    return _prefer(viet, text)


_SLOPE_MIN_WIDTH = 80  # quad hẹp hơn (số lượng, "8%") đo độ dốc không tin được
_SLOPE_MIN_QUADS = 3  # ít quad rộng hơn thì một quad nhiễu quyết cả trang
_SLOPE_MAX = 0.2  # ~11 độ; ảnh nghiêng hơn thế thì OCR đã hỏng, đừng chỉnh


def _deskew(quads) -> list[tuple[float, float, float, float]]:
    """Chỉnh nghiêng tọa độ hộp: trang chụp lệch làm giá ở mép phải trôi sang hàng của món dưới.

    Độ dốc trang s = trung vị độ dốc cạnh trên các quad rộng; mỗi hộp giữ x, tâm dọc thành cy - s*cx (xoay ngược
    quanh trục x=0) và chiều cao lấy theo cạnh trái của quad, không theo hộp bao (hộp bao của chữ nghiêng cao quá).
    Trang phẳng (s = 0) ra đúng hộp bao như cũ. ponytail: một độ dốc cho cả trang; giấy cong thì chưa xử lý."""
    slopes = [(q[1][1] - q[0][1]) / (q[1][0] - q[0][0]) for q in quads if q[1][0] - q[0][0] > _SLOPE_MIN_WIDTH]
    s = median(slopes) if len(slopes) >= _SLOPE_MIN_QUADS else 0.0
    if abs(s) > _SLOPE_MAX:
        s = 0.0  # độ dốc sai có thể hoán giá giữa các hàng mà tổng vẫn khớp: không tin thì giữ hộp thẳng trục
    out = []
    for q in quads:
        xs, ys = [p[0] for p in q], [p[1] for p in q]
        x0, x1 = min(xs), max(xs)
        if s == 0:
            out.append((x0, min(ys), x1, max(ys)))
            continue
        h = ((q[3][0] - q[0][0]) ** 2 + (q[3][1] - q[0][1]) ** 2) ** 0.5
        cy = sum(ys) / 4 - s * (x0 + x1) / 2
        out.append((x0, cy - h / 2, x1, cy + h / 2))
    return out


def _run(bgr) -> list[OcrBox]:
    result = _engine().ocr(bgr, cls=True)
    reread = _use_vietocr()
    lines = result[0] or []
    out = []
    for i, ((box, (text, conf)), coords) in enumerate(zip(lines, _deskew([box for box, _ in lines]))):
        text = clean_ocr_text(_reread_with_vietocr(bgr, box, text) if reread and i < _VIET_HEAD_LINES else text)
        out.append(OcrBox(text, float(conf), *coords))
    return out


_MIN_OCR_WIDTH = 800   # tờ bill hẹp hơn mức này (ảnh chụp xa rồi cắt) thì chữ quá nhỏ để đọc đúng, phóng to trước
_TARGET_OCR_WIDTH = 1000
_MAX_UPSCALE = 2.2     # phóng quá mức này chỉ làm mờ thêm và chậm
_MAX_SIDE = 3200


_MAX_CROP_RATIO = 0.7  # chỉ phóng to khi tờ bill thật sự nhỏ trong khung (cắt còn <= 70% chiều rộng ảnh gốc)


def upscale_small(bgr: np.ndarray, original_width: int | None = None) -> np.ndarray:
    """Phóng to ảnh đã cắt khi tờ bill hẹp: ảnh chụp xa có chữ chỉ cao vài pixel nên Paddle đọc sai số và dính chữ.

    Ảnh vốn đã nhỏ nhưng chữ đủ nét (không bị cắt bớt nhiều) thì giữ nguyên: phóng to làm kết quả kém hơn trên các ảnh tối/mờ."""
    h, w = bgr.shape[:2]
    if w >= _MIN_OCR_WIDTH or (original_width is not None and w > _MAX_CROP_RATIO * original_width):
        return bgr
    scale = min(_MAX_UPSCALE, _TARGET_OCR_WIDTH / w, _MAX_SIDE / max(h, w))
    if scale <= 1.05:
        return bgr
    return cv2.resize(bgr, (int(w * scale), int(h * scale)), interpolation=cv2.INTER_CUBIC)


def _boxes_from_google(lines) -> list[OcrBox]:
    """Dòng Google Vision (chữ, độ tin cậy, 4 góc) thành OcrBox: chỉnh nghiêng như hộp của OCR cục bộ rồi xếp từ trên xuống.
    Chữ Google giữ nguyên (đã đủ dấu): không qua sửa dấu lạ hay VietOCR."""
    coords = _deskew([quad for _text, _conf, quad in lines])
    return sorted((OcrBox(text, float(conf), *c) for (text, conf, _quad), c in zip(lines, coords)), key=lambda b: (b.y0, b.x0))


def read_boxes(image_bytes: bytes) -> list[OcrBox]:
    """Trả về các dòng chữ kèm hộp bao theo thứ tự từ trên xuống. Google Cloud Vision đọc trước; lỗi hoặc chưa cấu hình thì OCR cục bộ."""
    if (google := google_vision.read_lines_or_none(image_bytes)) is not None:
        return _boxes_from_google(google)
    img = Image.open(io.BytesIO(image_bytes)).convert("RGB")
    bgr = crop_receipt(np.array(img)[:, :, ::-1])  # Paddle nhận BGR; bỏ nền trước khi đo sáng
    bgr = upscale_small(enhance(bgr, assess_quality(bgr)), img.width)
    boxes = _run(bgr)
    if not boxes:
        raise NoTextError()
    if any(_cut_off(b.text) for b in boxes[:15]):  # chỉ đọc lại khi đầu phiếu có dòng bị cụt, để không chậm ảnh thường
        h, w = bgr.shape[:2]
        for scale in (1.3, 0.6):
            retry = [(b.text, b.conf) for b in _run(cv2.resize(bgr, (int(w * scale), int(h * scale))))]
            fixed = repair_cut_off([(b.text, b.conf) for b in boxes], retry)
            # chỉ chữ được thay; hộp giữ nguyên tọa độ ảnh đã cắt (hộp của lần đọc lại khác tỉ lệ bị bỏ)
            boxes = [OcrBox(t, c, b.x0, b.y0, b.x1, b.y1) for (t, c), b in zip(fixed, boxes)]
    return boxes


def read_lines(image_bytes: bytes) -> list[tuple[str, float]]:
    """Trả về [(dòng chữ, độ tin cậy)] theo thứ tự từ trên xuống."""
    return [(b.text, b.conf) for b in read_boxes(image_bytes)]
