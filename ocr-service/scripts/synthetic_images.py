"""Vẽ bill giả thành ẢNH, làm hỏng như ảnh chụp thật, rồi đọc bằng đúng OCR của app. Nhãn đã biết nên không cần gán tay.

Chạy:  .\\.venv\\Scripts\\python.exe scripts/synthetic_images.py [số_ảnh=300] [seed=11]
Ghi:   ..\\data-synthetic\\*.jpg + labels.json   và   dataset_synthetic_ocr.jsonl (chữ đã qua OCR + nhãn, label_source = "synthetic_ocr")
In:    độ chính xác của luật hiện tại trên ảnh giả, theo danh mục (đây là thước đo phụ: nội dung do mình viết nên dễ hơn bill thật).
Không đụng dataset.jsonl. Dữ liệu giả không chứa thông tin cá nhân.
"""
import json
import random
import sys
import textwrap
import time
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent.parent  # thư mục dịch vụ (script nằm trong scripts/): nơi các module và file dữ liệu ở
sys.path.insert(0, str(HERE))

from reading import ocr  # noqa: E402
from parsing.extract import _alnum, _norm, analyze  # noqa: E402
from parsing.merchant_name import format_merchant_name  # noqa: E402
from parsing.privacy import sanitize_lines  # noqa: E402
from synthetic_bills import ITEMS, make_bill, money  # noqa: E402
OUT_DIR = HERE.parent / "data-synthetic"
OUT_JSONL = HERE / "dataset_synthetic_ocr.jsonl"
FONTS = [f"C:/Windows/Fonts/{n}.ttf" for n in ("cour", "consola", "CascadiaMono", "tahoma")]  # đã kiểm tra: đủ mọi chữ có dấu tiếng Việt (Arial Narrow và Lucida Console thì thiếu)
TITLES = ["HÓA ĐƠN BÁN HÀNG", "PHIẾU THANH TOÁN", "HÓA ĐƠN", "PHIẾU TÍNH TIỀN", "BIÊN LAI THU TIỀN"]
STREETS = ["12 Nguyễn Trãi", "45 Lê Lợi", "88 Trần Hưng Đạo", "3 Phạm Văn Đồng", "210 Cách Mạng Tháng 8", "17 Võ Văn Tần"]


def render_paper(bill: dict, rng: random.Random) -> Image.Image:
    font_path = rng.choice(FONTS)
    size = rng.randint(17, 21)
    font, big = ImageFont.truetype(font_path, size), ImageFont.truetype(font_path, size + 6)
    width = rng.randint(380, 520)
    cols = max(18, int(width / (size * 0.6)))
    lines: list[tuple[str, str, ImageFont.FreeTypeFont]] = [("c", bill["merchant"], big)]
    if rng.random() < 0.7:
        lines.append(("c", f"ĐC: {rng.choice(STREETS)}, TP.HCM", font))
    lines += [("c", rng.choice(TITLES), font), ("l", f"Ngày: {rng.randint(1, 28):02d}/{rng.randint(1, 12):02d}/2026 {rng.randint(7, 21):02d}:{rng.randint(0, 59):02d}", font),
              ("l", "-" * cols, font)]
    rows = bill["lines"]
    # bill["lines"] (noise=False): [tên cửa hàng, (tiêu đề?), "Tên hàng SL Đơn giá Thành tiền", (tên, SL, đơn giá, thành tiền)*, "Tổng cộng", tổng, "Cảm ơn quý khách"]
    start = next(i for i, r in enumerate(rows) if r.startswith("Tên hàng")) + 1
    items = rows[start:-3]
    for k in range(0, len(items), 4):
        name, qty, price, amount = items[k:k + 4]
        for part in textwrap.wrap(name, cols) or [name]:
            lines.append(("l", part, font))
        lines.append(("r", f"{qty} x {price}   {amount}", font))
    lines += [("l", "-" * cols, font), ("r", f"TỔNG CỘNG: {rows[-2]} VND", big if rng.random() < 0.5 else font), ("c", "Cảm ơn quý khách", font)]
    lh = size + 8
    img = Image.new("RGB", (width, lh * (len(lines) + 3)), (rng.randint(238, 255),) * 3)
    d = ImageDraw.Draw(img)
    y = lh
    for align, text, f in lines:
        w = d.textlength(text, font=f)
        x = {"c": (width - w) / 2, "l": 12, "r": width - w - 12}[align]
        d.text((max(4, x), y), text, fill=(rng.randint(10, 60),) * 3, font=f)
        y += lh + (6 if f is big else 0)
    return img.crop((0, 0, width, y + lh // 2))


def scene(paper: Image.Image, rng: random.Random) -> np.ndarray:
    """Đặt tờ giấy lên nền (bàn tối/sáng) rồi làm hỏng: xa/nhỏ, nghiêng, mờ, nhiễu, thiếu sáng, nén JPEG."""
    cw = rng.randint(768, 1100)
    ch = int(cw * rng.uniform(1.3, 2.2))
    base = rng.choice([(40, 40, 42), (70, 55, 45), (190, 175, 150), (120, 120, 125), (230, 230, 230)])
    bg = np.clip(np.full((ch, cw, 3), base, np.float32) + np.random.default_rng(rng.randint(0, 10**6)).normal(0, 14, (ch, cw, 3)), 0, 255).astype(np.uint8)
    frac = rng.uniform(0.38, 0.95)  # tờ bill chiếm bao nhiêu chiều rộng khung: nhỏ = chụp xa
    scale = min(frac * cw / paper.width, 0.95 * ch / paper.height)
    p = paper.resize((max(50, int(paper.width * scale)), max(50, int(paper.height * scale))), Image.LANCZOS)
    p = p.rotate(rng.uniform(-6, 6), expand=True, fillcolor=base, resample=Image.BICUBIC)
    x, y = rng.randint(0, max(0, cw - p.width)), rng.randint(0, max(0, ch - p.height))
    arr = bg.copy()
    arr[y:y + p.height, x:x + p.width] = np.array(p)[: ch - y, : cw - x][..., ::-1]
    if (blur := rng.choice([0, 0, 0.6, 1.0, 1.5])):
        arr = cv2.GaussianBlur(arr, (0, 0), blur)
    arr = np.clip(arr.astype(np.float32) * rng.uniform(0.5, 1.1) + np.random.default_rng(rng.randint(0, 10**6)).normal(0, rng.choice([3, 6, 12]), arr.shape), 0, 255)
    ok, buf = cv2.imencode(".jpg", arr.astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, rng.randint(50, 90)])
    return buf.tobytes()


def merchant_ok(expected: str, got: str) -> bool:
    e = _alnum(_norm(expected))
    return bool(e) and e in _alnum(_norm(got or ""))


def main() -> None:
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 300
    rng = random.Random(int(sys.argv[2]) if len(sys.argv) > 2 else 11)
    OUT_DIR.mkdir(exist_ok=True)
    cats = list(ITEMS)
    labels, stats = {}, {c: [0, 0, 0, 0, 0] for c in cats}  # n, tổng đúng, tên đúng, danh mục đúng, lỗi âm thầm
    t0 = time.time()
    with OUT_JSONL.open("w", encoding="utf-8") as out:
        for i in range(n):
            cat = cats[i % len(cats)]
            bill = make_bill(cat, rng, use_brands=True, noise=False)
            name = f"syn_{i:04d}_{cat}.jpg"
            data = scene(render_paper(bill, rng), rng)
            (OUT_DIR / name).write_bytes(data)
            labels[name] = {"merchant": bill["merchant"], "total": bill["total_amount"], "category": cat}
            try:
                lines = ocr.read_lines(data)
            except ocr.NoTextError:
                lines = []
            r = analyze(lines) if lines else {"merchant": None, "total_amount": None, "category": "other", "needs_review": True}
            display = format_merchant_name(r["merchant"]) or r["merchant"] or ""
            t, m, c = r["total_amount"] == bill["total_amount"], merchant_ok(bill["merchant"], display), r["category"] == cat
            s = stats[cat]
            s[0] += 1; s[1] += t; s[2] += m; s[3] += c; s[4] += (not (t and m and c)) and not r["needs_review"]
            out.write(json.dumps({"merchant": bill["merchant"], "category": cat, "label_source": "synthetic_ocr", "total_amount": bill["total_amount"],
                                  "lines": sanitize_lines([x for x, _ in lines])}, ensure_ascii=False) + "\n")
            if (i + 1) % 25 == 0:
                print(f"{i + 1}/{n} ảnh ({time.time() - t0:.0f}s)", flush=True)
    (OUT_DIR / "labels.json").write_text(json.dumps(labels, ensure_ascii=False, indent=1), encoding="utf-8")
    print("\nLuật hiện tại trên ảnh giả (n | tổng | tên | danh mục | lỗi âm thầm):")
    tot = [0] * 5
    for c, s in stats.items():
        print(f"  {c:14} {s[0]:3} | {s[1]:3} | {s[2]:3} | {s[3]:3} | {s[4]:3}")
        tot = [a + b for a, b in zip(tot, s)]
    print(f"  {'TỔNG':14} {tot[0]:3} | {tot[1]:3} | {tot[2]:3} | {tot[3]:3} | {tot[4]:3}   ({tot[1] / tot[0]:.0%} | {tot[2] / tot[0]:.0%} | {tot[3] / tot[0]:.0%})")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
