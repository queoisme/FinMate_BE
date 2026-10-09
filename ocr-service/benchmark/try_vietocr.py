"""So sánh dòng chữ PaddleOCR (Latin) với VietOCR ONNX trên các dòng đầu phiếu của vài ảnh thật."""
import glob
import io
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.stdout.reconfigure(encoding="utf-8")

import ocr  # noqa: E402
import vietocr_onnx  # noqa: E402
from crop import crop_receipt  # noqa: E402

files = sys.argv[1:] or sorted(glob.glob(str(Path(__file__).resolve().parents[2] / "data-bill" / "*.jpg")))[:6]
for f in files:
    img = Image.open(f).convert("RGB")
    bgr = crop_receipt(np.array(img)[:, :, ::-1])
    t = time.time()
    res = ocr._engine().ocr(bgr, cls=True)[0] or []
    t_paddle = time.time() - t
    rgb = Image.fromarray(bgr[:, :, ::-1])
    print(f"\n== {Path(f).name[:8]}  paddle {t_paddle:.1f}s, {len(res)} dòng")
    t = time.time()
    for box, (text, conf) in res[:10]:
        xs, ys = [p[0] for p in box], [p[1] for p in box]
        crop = rgb.crop((max(min(xs) - 2, 0), max(min(ys) - 2, 0), max(xs) + 2, max(ys) + 2))
        print(f"  paddle: {text!r:50} viet: {vietocr_onnx.recognize(crop)!r}")
    print(f"  (10 dòng VietOCR: {time.time() - t:.1f}s)")
