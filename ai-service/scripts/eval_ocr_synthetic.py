"""Đo OCR bằng hóa đơn TỔNG HỢP (không phải ảnh thật) qua Tesseract thật."""
import io, random
from datetime import datetime, UTC
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from app.pipeline.stages.receipt_ocr import TesseractOcrEngine, read_receipt

random.seed(7)
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
STORES = ["Highlands Coffee", "WinMart Nguyễn Trãi", "Circle K", "Phở 24", "The Coffee House",
          "Bách Hóa Xanh", "Cơm Tấm Ba Ghiền", "GS25 Lê Văn Sỹ", "Pharmacity", "Co.opmart Sư Vạn Hạnh"]
ITEMS = [("Cà phê sữa", 29000), ("Bánh mì", 15000), ("Trà đào", 45000), ("Phở bò", 55000),
         ("Nước suối", 8000), ("Sữa tươi", 32000), ("Mì gói", 6500), ("Cơm sườn", 48000)]


def money(v):  # 110.000
    return f"{v:,}".replace(",", ".")


def make(seed_layout):
    store = random.choice(STORES)
    picks = random.sample(ITEMS, random.randint(1, 4))
    qty = [random.randint(1, 3) for _ in picks]
    total = sum(p[1] * q for p, q in zip(picks, qty))
    day = random.randint(1, 28); mon = random.randint(1, 9)
    date = f"{day:02d}/{mon:02d}/2026"
    paid = ((total // 50000) + 1) * 50000
    lines = [store, "ĐC: 123 Đường ABC, Q.1, TP.HCM", "MST: 0312345678  ĐT: 02838123456",
             "HÓA ĐƠN BÁN HÀNG", f"Ngày: {date} 14:{random.randint(10,59)}", "-" * 32]
    for (n, p), q in zip(picks, qty):
        lines.append(f"{n[:16]:<16}{q:>2}x{money(p):>8}")
        lines.append(f"{'':<16}{'Thành tiền':>10}{money(p*q):>9}")
    lines += ["-" * 32]
    label = random.choice(["Tổng cộng", "TỔNG CỘNG", "Tổng thanh toán", "Tổng tiền"])
    lines.append(f"{label}:{money(total):>{32-len(label)-1}}")
    if seed_layout % 2 == 0:
        lines.append(f"Tiền khách đưa:{money(paid):>{32-15}}")
        lines.append(f"Tiền thối lại:{money(paid-total):>{32-14}}")
    lines += ["Cảm ơn quý khách!"]
    return store, total, date, lines


def render(lines, cond):
    size = {"clean": 26, "phone": 22, "faded": 22, "lowres": 13}[cond]
    font = ImageFont.truetype(FONT, size)
    lh = int(size * 1.35)
    w = int(size * 0.62 * 34) + 40
    img = Image.new("RGB", (w, lh * len(lines) + 40), (255, 255, 255))
    d = ImageDraw.Draw(img)
    ink = {"clean": (0, 0, 0), "phone": (30, 30, 30), "faded": (150, 150, 150), "lowres": (20, 20, 20)}[cond]
    for i, ln in enumerate(lines):
        d.text((20, 20 + i * lh), ln, font=font, fill=ink)
    if cond == "phone":
        bg = Image.new("RGB", img.size, (245, 235, 200))  # ám vàng
        img = Image.blend(img, bg, 0.0)
        img = Image.eval(img, lambda p: min(255, p)).rotate(random.uniform(-2.5, 2.5), expand=True, fillcolor=(245, 235, 200))
        img = img.filter(ImageFilter.GaussianBlur(0.9))
    if cond == "faded":
        img = img.filter(ImageFilter.GaussianBlur(0.7))
    if cond == "lowres":
        img = img.filter(ImageFilter.GaussianBlur(0.4))
    buf = io.BytesIO(); img.save(buf, "JPEG", quality=70 if cond != "clean" else 95)
    return buf.getvalue()


engine = TesseractOcrEngine()
now = datetime(2026, 9, 30, tzinfo=UTC)
N = 25
print(f"{'condition':<8} {'amount':>8} {'merchant':>9} {'date':>6}  n={N}")
allfails = []
for cond in ["clean", "phone", "faded", "lowres"]:
    a = m = dt = 0
    for i in range(N):
        store, total, date, lines = make(i)
        r = read_receipt(engine, render(lines, cond), now)
        a_ok = r.amount_cents == total
        m_ok = bool(r.merchant_name) and store.lower()[:8] in r.merchant_name.lower()
        d_ok = bool(r.transacted_at) and r.transacted_at.strftime("%d/%m/%Y") == date
        a += a_ok; m += m_ok; dt += d_ok
        if not a_ok and len(allfails) < 12:
            allfails.append((cond, total, r.amount_cents, r.total_marker))
    print(f"{cond:<8} {a/N:>8.0%} {m/N:>9.0%} {dt/N:>6.0%}")
print("\namount failures (cond, want, got, marker):")
for f in allfails:
    print(" ", f)
