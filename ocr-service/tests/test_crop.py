import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import numpy as np

from crop import crop_receipt


def _scene(bg=40, paper=230, rect=(150, 40, 100, 220), size=(300, 400)) -> np.ndarray:
    """Nền tối với một tờ giấy sáng (x, y, w, h) có vài dòng chữ đen."""
    h, w = size
    img = np.full((h, w, 3), bg, dtype=np.uint8)
    x, y, rw, rh = rect
    img[y:y + rh, x:x + rw] = paper
    for row in range(y + 20, y + rh - 20, 25):
        img[row:row + 4, x + 10:x + rw - 10] = 0  # dòng "chữ"
    return img


def test_crops_to_the_paper_with_a_small_margin():
    out = crop_receipt(_scene())
    h, w = out.shape[:2]
    assert 100 <= w <= 135 and 220 <= h <= 260      # tờ giấy 100x220 + lề 6%
    assert out.mean() > 150                          # phần lớn là giấy, không còn nền tối


def _tilted_scene(angle: float) -> np.ndarray:
    """Tờ giấy 100x220 nghiêng `angle` độ trên nền tối, có dòng chữ đen."""
    import cv2

    img = np.full((300, 300, 3), 40, dtype=np.uint8)
    box = cv2.boxPoints(((150, 150), (100, 220), angle)).astype(np.int32)
    cv2.fillPoly(img, [box], (230, 230, 230))
    paper = np.zeros(img.shape[:2], np.uint8)
    cv2.fillPoly(paper, [box], 255)
    rows = np.zeros_like(paper)
    for r in range(60, 240, 25):
        rows[r:r + 4, :] = 255
    img[(paper > 0) & (rows > 0)] = 0
    return img


def test_straightens_and_crops_a_tilted_receipt_without_background_corners():
    for angle in (15, -15, 25):
        out = crop_receipt(_tilted_scene(angle))
        h, w = out.shape[:2]
        assert 100 <= w <= 135 and 220 <= h <= 260, (angle, w, h)   # đã xoay thẳng, không phải hộp bao nghiêng
        assert out.mean() > 142, (angle, out.mean())                  # đã bỏ góc nền tối (cắt hộp bao nghiêng chỉ ~132)


def test_never_cuts_paper_pixels_of_a_distorted_receipt():
    """Tờ giấy méo phối cảnh/nhàu (hình thang): cắt có thể kém gọn nhưng không được mất chữ."""
    import cv2

    img = np.full((300, 300, 3), 40, dtype=np.uint8)
    trapezoid = np.array([[110, 30], [200, 40], [250, 270], [60, 260]], np.int32)
    cv2.fillPoly(img, [trapezoid], (230, 230, 230))
    img[100:104, 100:220] = 0
    out = crop_receipt(img)
    paper_in = int((img > 200).all(axis=2).sum())
    paper_out = int((out > 200).all(axis=2).sum())
    assert paper_out == paper_in      # toàn bộ điểm ảnh giấy vẫn còn trong ảnh cắt


def test_keeps_black_header_bar_touching_the_paper_edge():
    img = _scene()
    img[40:46, 150:250] = 0                          # thanh đen sát mép trên của giấy (như logo)
    out = crop_receipt(img)
    assert (out < 20).any(axis=2).any(axis=1)[:10].any()   # thanh đen vẫn còn trong ảnh cắt


def test_returns_original_when_receipt_fills_the_frame():
    img = np.full((300, 400, 3), 235, dtype=np.uint8)
    img[100:104, 20:380] = 0
    assert crop_receipt(img) is img


def test_returns_original_when_no_clear_paper():
    flat = np.full((300, 400, 3), 40, dtype=np.uint8)
    assert crop_receipt(flat) is flat


def test_returns_original_when_bright_region_is_tiny():
    img = _scene(rect=(10, 10, 30, 30))              # chấm sáng nhỏ, không phải hóa đơn
    assert crop_receipt(img) is img
