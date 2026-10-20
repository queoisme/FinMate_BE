"""Cắt vùng tờ hóa đơn khỏi nền (bàn phím, bàn, tay...) trước khi OCR."""
import cv2
import numpy as np

_MIN_AREA_RATIO = 0.15   # vùng sáng nhỏ hơn mức này không phải hóa đơn
_MAX_BOX_RATIO = 0.90    # hóa đơn đã chiếm gần hết khung thì không cần cắt
_MARGIN_RATIO = 0.06     # chừa lề để không mất thanh logo/chữ đậm sát mép giấy
_MIN_RECT_FILL = 0.9     # vùng giấy lấp đầy >= 90% hình chữ nhật nhỏ nhất mới xoay thẳng; méo/nhàu thì chỉ cắt hộp
_MIN_TILT_DEGREES = 3    # nghiêng ít hơn mức này thì chỉ cắt, không xoay (tránh làm mờ ảnh)
_MIN_BOX_FILL = 0.6      # vùng sáng lấp đầy hộp bao của nó ít hơn mức này = dính cả nền sáng (kệ hàng, bàn) -> thử tách theo màu
_MAX_PAPER_SATURATION = 50     # giấy hóa đơn gần như không có màu; kệ hàng, bao bì, đồ vật thì có
_MIN_AREA_RATIO_RETRY = 0.08   # sau khi tách theo màu, tờ bill dài và hẹp chiếm ít diện tích khung hơn tờ vuông


def _paper_only_contour(img_bgr: np.ndarray, mask: np.ndarray, k: int):
    """Chỉ giữ vùng vừa sáng vừa gần như không màu rồi lấy mảng giấy lấp đầy hộp bao nhất; không có thì None.

    Dùng khi vùng sáng đầu tiên dính cả vật khác (ảnh bill đặt trên bàn tối cạnh kệ hàng sáng màu)."""
    h, w = img_bgr.shape[:2]
    low_color = (cv2.cvtColor(img_bgr, cv2.COLOR_BGR2HSV)[:, :, 1] < _MAX_PAPER_SATURATION).astype(np.uint8) * 255
    paper = cv2.bitwise_and(mask, low_color)
    paper = cv2.morphologyEx(paper, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_RECT, (k, k)))
    paper = cv2.morphologyEx(paper, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_RECT, (25, 25)))  # cắt cầu nối mỏng giữa các vật
    contours, _ = cv2.findContours(paper, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    best, best_score = None, 0.0
    for c in contours:
        area = cv2.contourArea(c)
        _, _, bw, bh = cv2.boundingRect(c)
        fill = area / (bw * bh) if bw * bh else 0
        if area < _MIN_AREA_RATIO_RETRY * h * w or fill < _MIN_BOX_FILL:
            continue
        if area * fill > best_score:
            best, best_score = c, area * fill
    return best


def crop_receipt(img_bgr: np.ndarray) -> np.ndarray:
    """Cắt theo vùng giấy sáng lớn nhất; không chắc chắn thì trả lại đúng ảnh gốc (cùng đối tượng).

    Tờ phẳng thì xoay thẳng rồi cắt; tờ méo/nhàu chỉ cắt hộp bao (kém gọn nhưng không mất chữ).
    Vùng sáng dính cả vật khác (kệ hàng, bàn sáng) thì thử lại chỉ với vùng ít màu để tách tờ giấy ra.
    ponytail: thử nắn phối cảnh 4 góc trên ảnh thật làm mất chữ ở mép nên đã bỏ; nền cũng sáng
    (mặt bàn trắng, không màu) thì vùng sáng dính vào nhau và sẽ không cắt.
    """
    h, w = img_bgr.shape[:2]
    gray = cv2.GaussianBlur(cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY), (5, 5), 0)
    _, mask = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    k = max(5, int(min(h, w) * 0.03))  # nối các mảng giấy bị chữ/nếp gấp chia cắt
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_RECT, (k, k)))
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return img_bgr
    biggest = max(contours, key=cv2.contourArea)
    _, _, bbw, bbh = cv2.boundingRect(biggest)
    if cv2.contourArea(biggest) < _MIN_BOX_FILL * bbw * bbh and bbw * bbh >= _MIN_AREA_RATIO * h * w:
        retry = _paper_only_contour(img_bgr, mask, k)
        if retry is not None:
            biggest = retry
        elif cv2.contourArea(biggest) < _MIN_AREA_RATIO * h * w:
            return img_bgr
    elif cv2.contourArea(biggest) < _MIN_AREA_RATIO * h * w:
        return img_bgr
    x, y, bw, bh = cv2.boundingRect(biggest)
    if bw * bh > _MAX_BOX_RATIO * h * w:
        return img_bgr
    (cx, cy), (rw, rh), angle = cv2.minAreaRect(biggest)
    if angle > 45:  # chuẩn hóa về góc nghiêng nhỏ nhất để chữ không bị xoay ngang
        angle, rw, rh = angle - 90, rh, rw
    flat = cv2.contourArea(biggest) >= _MIN_RECT_FILL * rw * rh  # tờ phẳng, hình chữ nhật chuẩn
    if abs(angle) < _MIN_TILT_DEGREES or not flat:
        m = int(_MARGIN_RATIO * max(bw, bh))
        return img_bgr[max(0, y - m):min(h, y + bh + m), max(0, x - m):min(w, x + bw + m)]
    # Nghiêng: xoay thẳng tờ giấy rồi cắt, để không lấy dính nền ở các góc.
    rotated = cv2.warpAffine(img_bgr, cv2.getRotationMatrix2D((cx, cy), angle, 1.0), (w, h))
    m = int(_MARGIN_RATIO * max(rw, rh))
    x0, y0 = int(max(0, cx - rw / 2 - m)), int(max(0, cy - rh / 2 - m))
    x1, y1 = int(min(w, cx + rw / 2 + m)), int(min(h, cy + rh / 2 + m))
    return rotated[y0:y1, x0:x1]
