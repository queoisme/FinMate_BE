"""Đo độ chính xác OCR + bóc tách trên ảnh hóa đơn thật.

Chạy:  .\.venv\Scripts\python.exe benchmark\run_benchmark.py            (cấu hình đang dùng trong ocr.py)
       .\.venv\Scripts\python.exe benchmark\run_benchmark.py --grid     (so sánh nhiều cấu hình PaddleOCR)
Thêm ảnh: bỏ vào benchmark/images và khai báo đáp án trong benchmark/expected.json.
Ảnh chứa thông tin cá nhân: giữ cục bộ, không đưa lên nơi công khai.
"""
import io
import json
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.stdout.reconfigure(encoding="utf-8")

from crop import crop_receipt                      # noqa: E402
from extract import _alnum, _amounts, _norm, analyze  # noqa: E402
from merchant_name import format_merchant_name     # noqa: E402
from quality import assess_quality, enhance        # noqa: E402

HERE = Path(__file__).parent
EXPECTED = json.loads((HERE / "expected.json").read_text(encoding="utf-8"))
EXPECTED = {k: v for k, v in EXPECTED.items() if not k.startswith("_")}

# Các cấu hình PaddleOCR để so sánh (tên -> tham số truyền thêm).
GRID = {
    "mac dinh (960)": {},
    "canh dai 1280": {"det_limit_side_len": 1280},
    "canh dai 1600": {"det_limit_side_len": 1600},
    "canh dai 2000": {"det_limit_side_len": 2000},
    "1600 + giu dong yeu (drop .3)": {"det_limit_side_len": 1600, "drop_score": 0.3},
    "1600 + unclip 2.0": {"det_limit_side_len": 1600, "det_db_unclip_ratio": 2.0},
    "1600 + box_thresh .5": {"det_limit_side_len": 1600, "det_db_box_thresh": 0.5},
    "1600 + tat ca (unclip 1.8, thresh .5, drop .3)": {
        "det_limit_side_len": 1600, "det_db_unclip_ratio": 1.8, "det_db_box_thresh": 0.5, "drop_score": 0.3},
}


# So sánh mô hình tìm vùng chữ (chạy bằng --det). Mô hình v4 cần được tải trước vào thư mục dưới đây.
_DET_V4 = str(Path.home() / ".paddleocr" / "whl" / "det" / "ch" / "ch_PP-OCRv4_det_infer")
DET_GRID = {
    "tim vung chu hien tai (en v3)": {},
    "tim vung chu ch v4": {"det_model_dir": _DET_V4},
    "ch v4 + canh dai 1280": {"det_model_dir": _DET_V4, "det_limit_side_len": 1280},
    "ch v4 + unclip 1.8": {"det_model_dir": _DET_V4, "det_db_unclip_ratio": 1.8},
}


def preprocess(path: Path) -> np.ndarray:
    rgb = np.array(Image.open(path).convert("RGB"))[:, :, ::-1]
    bgr = crop_receipt(rgb)
    return enhance(bgr, assess_quality(bgr))


def read(engine, bgr: np.ndarray) -> list[tuple[str, float]]:
    result = engine.ocr(bgr, cls=True)[0] or []
    return [(t, float(c)) for _, (t, c) in result]


def _median_tilt(result) -> float:
    """Độ dốc trung vị (độ) của các hộp chữ dài; dương = chữ dốc xuống về bên phải."""
    angles = []
    for box, (text, conf) in result:
        (x0, y0), (x1, y1) = box[0], box[1]
        if conf >= 0.8 and (x1 - x0) > 3 * abs(y1 - y0) + 40:
            angles.append(np.degrees(np.arctan2(y1 - y0, x1 - x0)))
    return float(np.median(angles)) if len(angles) >= 5 else 0.0


def read_deskewed(engine, bgr: np.ndarray) -> list[tuple[str, float]]:
    """Đọc 1 lần; nếu chữ nghiêng >= 1.5 độ thì xoay thẳng và đọc lại, giữ kết quả nhiều chữ đọc rõ hơn."""
    import cv2
    first = engine.ocr(bgr, cls=True)[0] or []
    tilt = _median_tilt(first)
    lines = [(t, float(c)) for _, (t, c) in first]
    if abs(tilt) < 1.5 or abs(tilt) > 15:
        return lines
    h, w = bgr.shape[:2]
    rotated = cv2.warpAffine(bgr, cv2.getRotationMatrix2D((w / 2, h / 2), tilt, 1.0), (w, h),
                             borderMode=cv2.BORDER_REPLICATE)
    second = [(t, float(c)) for _, (t, c) in (engine.ocr(rotated, cls=True)[0] or [])]
    good = lambda ls: sum(c >= 0.85 for _, c in ls)
    return second if good(second) > good(lines) else lines


def score_image(name: str, lines: list[tuple[str, float]]) -> dict:
    exp = EXPECTED[name]
    texts = [t for t, _ in lines]
    squeezed = _alnum(_norm(" ".join(texts)))
    found_amounts = {a for t in texts for a in _amounts(t)}
    r = analyze(lines) if lines else {"total_amount": None, "merchant": None}
    display = format_merchant_name(r["merchant"] or "") or (r["merchant"] or "")
    return {
        "total_ok": r["total_amount"] == exp["total"],
        # merchant = null nghĩa là đáp án đúng là "không đọc được tên" (để trống), không phải một cái tên bất kỳ
        "merchant_ok": (not display) if exp["merchant"] is None else exp["merchant"] in _alnum(_norm(display)),
        "amount_recall": sum(a in found_amounts for a in exp["amounts"]) / len(exp["amounts"]),
        "word_recall": sum(w in squeezed for w in exp["words"]) / len(exp["words"]),
        "total": r["total_amount"], "merchant": display,
    }


def run(config: dict, verbose: bool) -> dict:
    from paddleocr import PaddleOCR
    config = dict(config)
    reader = read_deskewed if config.pop("_deskew", False) else read
    engine = PaddleOCR(use_angle_cls=True, lang="vi", show_log=False, **config)
    rows, elapsed = [], 0.0
    for name in EXPECTED:
        bgr = preprocess(HERE / "images" / name)
        t0 = time.time()
        lines = reader(engine, bgr)
        elapsed += time.time() - t0
        s = score_image(name, lines)
        rows.append(s)
        if verbose:
            print(f"   {name:9s} tổng {'ĐÚNG' if s['total_ok'] else 'sai '} ({s['total']}) | tên {'ĐÚNG' if s['merchant_ok'] else 'sai '} ({s['merchant']}) "
                  f"| số tiền {s['amount_recall']:.0%} | chữ {s['word_recall']:.0%}")
    n = len(rows)
    return {
        "total_acc": sum(r["total_ok"] for r in rows) / n,
        "merchant_acc": sum(r["merchant_ok"] for r in rows) / n,
        "amount_recall": sum(r["amount_recall"] for r in rows) / n,
        "word_recall": sum(r["word_recall"] for r in rows) / n,
        "sec_per_image": elapsed / n,
    }


if __name__ == "__main__":
    if "--deskew" in sys.argv:
        configs = {"hien tai": {}, "xoay thang khi chu nghieng (2 luot)": {"_deskew": True}}
    elif "--det" in sys.argv:
        configs = DET_GRID
    elif "--grid" in sys.argv:
        configs = GRID
    else:
        configs = {"cau hinh hien tai (ocr.py)": {}}
    print(f"{'cấu hình':48s} {'tổng đúng':>9s} {'tên đúng':>9s} {'số tiền':>8s} {'chữ':>6s} {'giây/ảnh':>9s}")
    for name, cfg in configs.items():
        m = run(cfg, verbose="--verbose" in sys.argv)
        print(f"{name:48s} {m['total_acc']:9.0%} {m['merchant_acc']:9.0%} {m['amount_recall']:8.0%} {m['word_recall']:6.0%} {m['sec_per_image']:9.1f}")
