"""Đọc chữ từ ảnh hóa đơn bằng PaddleOCR (tiếng Việt), chạy tại máy."""
import io
from difflib import SequenceMatcher
import os
import re
from functools import lru_cache

import cv2
import numpy as np
from PIL import Image

from crop import crop_receipt
from brands import BRAND_CATEGORIES, WHOLE_WORD_BRANDS
from extract import _alnum, _norm
from quality import assess_quality, enhance
import vietocr_onnx
from text_clean import clean_ocr_text


class NoTextError(Exception):
    """Ảnh không đọc ra được chữ nào."""


@lru_cache(maxsize=1)
def _engine():
    from paddleocr import PaddleOCR  # nạp lười: lần đầu tải model, các test không cần

    return PaddleOCR(use_angle_cls=True, lang="vi", show_log=False)


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


def _run(bgr) -> list[tuple[str, float]]:
    result = _engine().ocr(bgr, cls=True)
    reread = _use_vietocr()
    return [(clean_ocr_text(_reread_with_vietocr(bgr, box, text) if reread and i < _VIET_HEAD_LINES else text), float(conf))
            for i, (box, (text, conf)) in enumerate(result[0] or [])]


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


def read_lines(image_bytes: bytes) -> list[tuple[str, float]]:
    """Trả về [(dòng chữ, độ tin cậy)] theo thứ tự từ trên xuống."""
    img = Image.open(io.BytesIO(image_bytes)).convert("RGB")
    bgr = crop_receipt(np.array(img)[:, :, ::-1])  # Paddle nhận BGR; bỏ nền trước khi đo sáng
    bgr = upscale_small(enhance(bgr, assess_quality(bgr)), img.width)
    lines = _run(bgr)
    if not lines:
        raise NoTextError()
    if any(_cut_off(t) for t, _ in lines[:15]):  # chỉ đọc lại khi đầu phiếu có dòng bị cụt, để không chậm ảnh thường
        h, w = bgr.shape[:2]
        for scale in (1.3, 0.6):
            lines = repair_cut_off(lines, _run(cv2.resize(bgr, (int(w * scale), int(h * scale)))))
    return lines
