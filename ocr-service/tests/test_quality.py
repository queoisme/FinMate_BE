import io
import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import numpy as np
from PIL import Image

from quality import assess_quality, enhance, image_quality


def _img(lo: int, hi: int, text_value: int | None = None) -> np.ndarray:
    rng = np.random.default_rng(0)
    g = rng.integers(lo, hi + 1, (120, 120), dtype=np.uint8)
    if text_value is not None:
        g[10:14, :] = text_value  # vài nét "chữ" ~3% điểm ảnh
    return np.stack([g, g, g], axis=-1)


def test_normal_photo_is_ok():
    assert assess_quality(_img(20, 240)) == "ok"


def test_white_receipt_with_black_text_is_ok():
    assert assess_quality(_img(255, 255, text_value=0)) == "ok"


def test_dark_photo_is_too_dark():
    assert assess_quality(_img(2, 46)) == "too_dark"


def test_washed_out_photo_is_too_bright():
    assert assess_quality(_img(126, 255)) == "too_bright"


def test_enhance_leaves_ok_image_untouched():
    img = _img(20, 240)
    assert enhance(img, "ok") is img


def test_enhance_brightens_dark_and_darkens_bright():
    dark, bright = _img(2, 46), _img(126, 255)
    assert enhance(dark, "too_dark").mean() > dark.mean() + 40
    assert enhance(bright, "too_bright").mean() < bright.mean() - 20


def test_enhance_survives_a_solid_white_or_black_image():
    for value, quality in ((255, "too_bright"), (0, "too_dark")):
        solid = np.full((50, 50, 3), value, dtype=np.uint8)
        assert assess_quality(solid) == quality
        enhance(solid, quality)      # không được chia cho 0 hay ném lỗi


def test_image_quality_from_png_bytes():
    buf = io.BytesIO()
    Image.fromarray(_img(2, 46)).save(buf, format="PNG")
    assert image_quality(buf.getvalue()) == "too_dark"


def test_heavily_blurred_photo_is_blurry_but_sharp_one_is_ok():
    import cv2
    g = np.full((800, 800), 240, dtype=np.uint8)
    for y in range(20, 800, 60):
        g[y:y + 30, 40:760] = 20  # vạch đen như dòng chữ
    sharp = np.stack([g, g, g], axis=-1)
    png = lambda a: (lambda b: (Image.fromarray(a).save(b, "PNG"), b.getvalue())[1])(io.BytesIO())
    assert image_quality(png(sharp)) == "ok"
    assert image_quality(png(cv2.GaussianBlur(sharp, (0, 0), 12))) == "blurry"
