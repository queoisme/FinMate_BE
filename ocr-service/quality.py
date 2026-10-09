"""Đánh giá và chỉnh độ sáng ảnh hóa đơn trước khi OCR."""
import io
import math

import cv2
import numpy as np
from PIL import Image

from crop import crop_receipt

# ponytail: ngưỡng chỉnh theo 4 ảnh mẫu (bình thường p99=238/p1=21, tối p99=46, cháy sáng p1=126);
# thêm ảnh thật rồi chỉnh lại nếu báo nhầm.
_DARK_P99 = 100
_BRIGHT_P1 = 100


def assess_quality(img_bgr: np.ndarray) -> str:
    """'too_dark' khi không có điểm sáng nào, 'too_bright' khi không có điểm tối nào, còn lại 'ok'."""
    gray = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY)
    p1, p99 = np.percentile(gray, [1, 99])
    if p99 < _DARK_P99:
        return "too_dark"
    if p1 > _BRIGHT_P1:
        return "too_bright"
    return "ok"


# ponytail: đo trên 28 ảnh thật: ảnh nét 9..2400, cùng ảnh đó làm mờ (sigma 2.5) chỉ còn <16;
# chọn 8 để không báo nhầm ảnh thật nào, chỉ bắt ảnh mờ nặng.
_BLUR_VAR = 8


def sharpness(img_bgr: np.ndarray) -> float:
    """Phương sai Laplacian trên ảnh đã thu về rộng 1000px (để so được giữa các độ phân giải)."""
    gray = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY)
    s = 1000 / gray.shape[1]
    gray = cv2.resize(gray, None, fx=s, fy=s, interpolation=cv2.INTER_AREA)
    return float(cv2.Laplacian(gray, cv2.CV_64F).var())


def image_quality(image_bytes: bytes) -> str:
    rgb = np.array(Image.open(io.BytesIO(image_bytes)).convert("RGB"))
    paper = crop_receipt(rgb[:, :, ::-1])  # đo trên tờ hóa đơn, không tính nền
    quality = assess_quality(paper)
    return "blurry" if quality == "ok" and sharpness(paper) < _BLUR_VAR else quality


def enhance(img_bgr: np.ndarray, quality: str) -> np.ndarray:
    """Ảnh 'ok' giữ nguyên; ảnh tối/sáng được gamma thích nghi rồi CLAHE để làm rõ chữ."""
    if quality == "ok":
        return img_bgr
    # kẹp để ảnh trắng/đen tinh (mean = 1 hoặc 0) không làm log(mean) bằng 0 và chia cho 0
    mean = min(max(float(cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY).mean()) / 255, 1e-3), 0.999)
    gamma = min(3.0, max(0.3, math.log(0.45) / math.log(mean)))
    lut = np.array([((i / 255.0) ** gamma) * 255 for i in range(256)], dtype="uint8")
    lab = cv2.cvtColor(cv2.LUT(img_bgr, lut), cv2.COLOR_BGR2LAB)
    l, a, b = cv2.split(lab)
    l = cv2.createCLAHE(clipLimit=3.0, tileGridSize=(8, 8)).apply(l)
    return cv2.cvtColor(cv2.merge((l, a, b)), cv2.COLOR_LAB2BGR)
