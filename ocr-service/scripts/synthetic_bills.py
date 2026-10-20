"""Sinh hóa đơn giả (chỉ chữ) cho các danh mục còn thiếu dữ liệu thật, ghi ra dataset_synthetic.jsonl.

Chạy:  .\.venv\Scripts\python.exe scripts/synthetic_bills.py [số_hóa_đơn_mỗi_danh_mục]   (mặc định 40)

- KHÔNG đụng tới dataset.jsonl. Bản ghi giả có label_source = "synthetic" và không chứa dữ liệu cá nhân.
- Dữ liệu giả chỉ dùng để huấn luyện; độ chính xác để qua cổng 80% luôn đo trên hóa đơn THẬT (xem category_model.train).
- Tên mặt hàng viết tay, độc lập với _PRODUCT_KEYWORDS, rồi thêm lỗi kiểu OCR (rớt chữ, dính chữ, nhầm ký tự).
"""
import json
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent  # thư mục dịch vụ (script nằm trong scripts/): nơi các module và file dữ liệu ở
sys.path.insert(0, str(HERE))

from classification.brands import BRAND_CATEGORIES, BRAND_DISPLAY  # noqa: E402
from extract import _alnum, _norm  # noqa: E402
OUT = HERE / "dataset_synthetic.jsonl"

# (tên mặt hàng, giá thấp, giá cao)
ITEMS = {
    "transport": [
        ("Xăng RON 95-III", 40000, 400000), ("Xăng E5 RON 92", 30000, 300000), ("Dầu diesel DO 0,05S", 50000, 800000),
        ("Phí gửi xe máy", 3000, 10000), ("Phí gửi xe ô tô theo giờ", 10000, 80000), ("Vé xe khách Sài Gòn - Đà Lạt", 250000, 450000),
        ("Vé xe buýt tuyến 08", 6000, 12000), ("Vé tàu hỏa SE1 ghế mềm", 400000, 1200000), ("Cước taxi", 50000, 400000),
        ("Phí cầu đường BOT", 35000, 140000), ("Rửa xe và hút bụi", 40000, 150000), ("Thay nhớt động cơ", 90000, 300000),
        ("Vé máy bay Hà Nội - TP.HCM", 900000, 3000000), ("Cước chuyến đi xe ôm công nghệ", 15000, 90000),
        ("Phí đỗ xe tầng hầm", 5000, 60000), ("Sửa xe vá lốp", 20000, 150000),
    ],
    "housing": [
        ("Tiền điện tháng", 150000, 2500000), ("Tiền nước tháng", 50000, 400000), ("Tiền thuê phòng trọ tháng", 1200000, 5000000),
        ("Phí quản lý chung cư", 200000, 1500000), ("Tiền rác sinh hoạt", 20000, 60000), ("Tiền gas bình 12kg", 380000, 480000),
        ("Hóa đơn điện lực kỳ này", 150000, 2500000), ("Phí vệ sinh môi trường", 15000, 50000), ("Tiền điện phòng", 100000, 800000),
        ("Thanh toán hóa đơn nước sạch", 60000, 400000), ("Tiền thuê nhà tháng", 2000000, 12000000), ("Phí gửi xe chung cư tháng", 70000, 150000),
    ],
    "utilities": [
        ("Cước Internet cáp quang", 180000, 400000), ("Cước điện thoại trả sau", 100000, 500000), ("Nạp tiền điện thoại", 20000, 500000),
        ("Phí truyền hình cáp", 80000, 250000), ("Dịch vụ wifi gia đình", 160000, 350000), ("Gói data 4G tháng", 70000, 200000),
        ("Nạp data Viettel", 50000, 200000), ("Gia hạn gói cước di động", 90000, 300000),
    ],
    "entertainment": [
        ("Vé xem phim 2D người lớn", 70000, 120000), ("Combo bắp nước", 79000, 130000), ("Vé xem phim 3D", 90000, 150000),
        ("Vé vào cổng khu vui chơi", 100000, 600000), ("Giờ chơi bowling", 60000, 200000), ("Phòng karaoke VIP 2 giờ", 250000, 900000),
        ("Nạp thẻ game", 50000, 500000), ("Vé công viên nước", 120000, 350000), ("Thuê sân cầu lông 1 giờ", 70000, 150000),
        ("Vé ca nhạc khu A", 400000, 2500000), ("Gói xem phim trực tuyến tháng", 70000, 260000), ("Vé tham quan bảo tàng", 30000, 100000),
        ("Máy chơi game trẻ em", 30000, 200000), ("Vé xem kịch sân khấu", 150000, 600000), ("Vé khu trượt băng", 100000, 280000),
    ],
    "healthcare": [
        ("Paracetamol 500mg hộp 10 viên", 8000, 30000), ("Thuốc ho Prospan siro", 60000, 120000), ("Vitamin C 1000mg ống sủi", 40000, 120000),
        ("Khẩu trang y tế hộp 50 cái", 25000, 70000), ("Khám bệnh tổng quát", 100000, 500000), ("Xét nghiệm máu", 150000, 800000),
        ("Nhỏ mắt Natri Clorid 0,9%", 3000, 20000), ("Amoxicillin 500mg vỉ", 15000, 60000), ("Băng gạc y tế và cồn sát khuẩn", 15000, 60000),
        ("Nhổ răng khôn", 500000, 2000000), ("Cạo vôi răng", 150000, 400000), ("Kính cận tròng chống ánh sáng xanh", 400000, 1800000),
        ("Thuốc nhỏ mũi", 15000, 50000), ("Viên uống bổ sung canxi D3", 120000, 400000), ("Tiêm phòng cúm", 300000, 400000),
        ("Siêu âm thai", 150000, 400000), ("Máy đo huyết áp", 400000, 1200000), ("Que thử thai", 10000, 40000),
    ],
    "other": [
        ("Phí dịch vụ giặt ủi", 20000, 150000), ("Cắt tóc nam", 40000, 150000), ("Gội đầu thư giãn", 50000, 200000),
        ("Photo tài liệu A4", 500, 1000), ("In màu tài liệu", 2000, 5000), ("Phí chuyển phát nhanh", 15000, 90000),
        ("Học phí khóa tiếng Anh", 500000, 5000000), ("Sửa điện thoại thay màn hình", 300000, 2500000), ("Phí làm thẻ",  50000, 200000),
        ("Dịch vụ sửa chữa điều hòa", 150000, 800000), ("Gói chụp ảnh thẻ", 30000, 100000), ("Phí công chứng giấy tờ", 20000, 200000),
        ("Phí gửi hành lý", 20000, 100000), ("Hoa tươi bó nhỏ", 100000, 500000), ("Quà tặng gói hộp", 20000, 100000),
        ("Phí đăng ký khóa học", 300000, 3000000), ("Dịch vụ spa massage 60 phút", 200000, 800000), ("Nối mi làm nail", 100000, 500000),
    ],
    "groceries": [
        ("Sữa tươi không đường 1L", 28000, 45000), ("Trứng gà vỉ 10 quả", 28000, 45000), ("Gạo thơm túi 5kg", 90000, 150000),
        ("Mì gói Hảo Hảo thùng", 100000, 130000), ("Dầu ăn chai 1L", 45000, 70000), ("Nước mắm Nam Ngư 500ml", 30000, 50000),
        ("Rau cải xanh 500g", 10000, 25000), ("Thịt ba chỉ heo 500g", 60000, 110000), ("Bột giặt túi 3kg", 90000, 160000),
        ("Nước rửa chén 750ml", 25000, 45000), ("Giấy vệ sinh lốc 10 cuộn", 55000, 110000), ("Cà chua 1kg", 18000, 40000),
        ("Xúc xích tiệt trùng gói", 20000, 45000), ("Bánh mì sandwich", 15000, 30000), ("Nước suối chai 500ml", 4000, 8000),
        ("Đường cát trắng 1kg", 22000, 32000), ("Sữa chua có đường lốc 4 hộp", 25000, 40000), ("Cá basa phi lê 500g", 45000, 85000),
    ],
    "dining": [
        ("Cơm sườn nướng", 35000, 70000), ("Phở bò tái", 40000, 80000), ("Bún chả", 40000, 75000), ("Bạc xỉu đá", 25000, 55000),
        ("Trà sữa trân châu size L", 35000, 65000), ("Cà phê sữa đá", 20000, 50000), ("Lẩu thái hải sản", 250000, 600000),
        ("Gà rán 2 miếng", 60000, 120000), ("Pizza hải sản size M", 120000, 250000), ("Mì Ý bò bằm", 70000, 150000),
        ("Bánh mì thịt nguội", 15000, 35000), ("Nước cam ép", 25000, 55000), ("Combo burger khoai chiên", 70000, 150000),
        ("Hủ tiếu nam vang", 35000, 70000), ("Bún bò Huế", 40000, 80000), ("Gỏi cuốn tôm thịt", 30000, 60000),
    ],
    "shopping": [
        ("Áo thun cổ tròn", 120000, 350000), ("Quần jean nam ống đứng", 250000, 650000), ("Giày thể thao nữ", 350000, 1500000),
        ("Dép sandal quai ngang", 80000, 300000), ("Túi xách tay", 200000, 900000), ("Ốp lưng điện thoại", 30000, 200000),
        ("Tai nghe bluetooth", 150000, 1200000), ("Son môi lì", 90000, 400000), ("Kem chống nắng SPF50", 150000, 500000),
        ("Nhẫn vàng 24K 1 chỉ", 6500000, 8500000), ("Đầm công sở", 200000, 700000), ("Balo laptop", 250000, 900000),
        ("Sạc dự phòng 10000mAh", 150000, 500000), ("Vở học sinh và bút bi", 20000, 120000), ("Nồi chiên không dầu", 900000, 3000000),
        ("Áo khoác gió", 250000, 800000),
    ],
}

# Tên cửa hàng mô tả chung (không phải thương hiệu trong bảng) để mô hình không chỉ nhớ tên chuỗi.
GENERIC_MERCHANTS = {
    "transport": ["Cây xăng số 12", "Trạm xăng Petrolimex", "Bãi giữ xe Nguyễn Trãi", "Nhà xe Hoàng Long", "Garage ô tô Minh Phát", "Tiệm rửa xe Hùng"],
    "housing": ["Công ty Điện lực TP", "Cấp nước khu vực", "Nhà trọ Hòa Bình", "Ban quản lý chung cư Sunrise", "Đại lý gas Nam Việt", "Phòng trọ Minh Anh"],
    "utilities": ["Viễn thông tỉnh", "Đại lý viễn thông Mai Linh", "Cửa hàng điện thoại Thành Công", "Trung tâm giao dịch internet"],
    "entertainment": ["Rạp chiếu phim Galaxy", "Khu vui chơi Kids Zone", "Karaoke Nice", "Sân cầu lông Thành Công", "Nhà hát lớn", "Công viên nước Đầm Sen"],
    "healthcare": ["Nhà thuốc Minh Châu", "Phòng khám đa khoa Tâm Đức", "Nha khoa Smile", "Bệnh viện quận 7", "Phòng xét nghiệm Medlatec", "Mắt kính Sài Gòn"],
    "other": ["Tiệm giặt ủi Sạch Sẽ", "Salon tóc Hoàng", "Tiệm photo copy 24", "Trung tâm Anh ngữ Apollo", "Tiệm sửa điện thoại Phát", "Spa Hương Sen"],
    "groceries": ["Tạp hóa Bà Tư", "Chợ đầu mối Bình Điền", "Siêu thị mini Hoa Mai", "Cửa hàng thực phẩm sạch", "Đại lý sữa Thu Hà", "Tiệm tạp hóa Cô Lan"],
    "dining": ["Quán cơm tấm Cô Ba", "Phở Hùng", "Quán trà sữa Mộc", "Nhà hàng hải sản Biển Xanh", "Quán bún bò Huế O Hiền", "Cà phê Sân Vườn"],
    "shopping": ["Shop thời trang Linh", "Cửa hàng giày Phát Đạt", "Tiệm vàng Kim Ngọc", "Shop phụ kiện điện thoại", "Cửa hàng mỹ phẩm Rosa", "Siêu thị điện máy Nam Anh"],
}

_CONFUSE = {"o": "0", "l": "1", "i": "l", "e": "c", "a": "o", "u": "v", "n": "h", "m": "rn"}


def noisy(text: str, rng: random.Random, rate: float = 0.04) -> str:
    """Lỗi kiểu OCR: rớt ký tự, nhầm ký tự giống nhau, dính hai từ lại."""
    out = []
    for ch in text:
        r = rng.random()
        if r < rate / 2:
            continue
        if r < rate and ch.lower() in _CONFUSE:
            out.append(_CONFUSE[ch.lower()])
            continue
        out.append(ch)
    s = "".join(out)
    if rng.random() < 0.15 and " " in s:
        s = s.replace(" ", "", 1)
    return s.upper() if rng.random() < 0.3 else s


def money(v: int) -> str:
    return f"{v:,}".replace(",", ".")


def make_bill(category: str, rng: random.Random, use_brands: bool, noise: bool = True) -> dict:
    noisy_ = noisy if noise else (lambda text, rng, rate=0: text)  # noise=False: chữ sạch để vẽ thành ảnh (OCR sẽ tự làm hỏng)
    # chỉ dùng thương hiệu in đúng tên khóa (bill thật in "Mango", bảng chỉ khớp "mango fashion" để khỏi nhầm trái xoài nên không công bằng)
    brands = [b for b, c in BRAND_CATEGORIES.items() if c == category and b in BRAND_DISPLAY and _alnum(_norm(BRAND_DISPLAY[b])) == _alnum(b)]
    if use_brands and brands and rng.random() < 0.5:
        merchant = BRAND_DISPLAY[rng.choice(brands)]
    else:
        merchant = rng.choice(GENERIC_MERCHANTS[category])
    picks = rng.sample(ITEMS[category], k=rng.randint(1, min(4, len(ITEMS[category]))))
    lines, total = [noisy_(merchant, rng, 0.02)], 0
    if rng.random() < 0.6:
        lines.append(noisy_(rng.choice(["HÓA ĐƠN BÁN HÀNG", "PHIẾU THANH TOÁN", "HÓA ĐƠN GTGT", "BIÊN LAI"]), rng))
    lines.append(noisy_("Tên hàng  SL  Đơn giá  Thành tiền", rng))
    for name, lo, hi in picks:
        qty = rng.choice([1, 1, 1, 2, 3])
        price = max(500, round(rng.randint(lo, hi) / 500) * 500)
        lines += [noisy_(name, rng), f"{qty}", money(price), money(price * qty)]
        total += price * qty
    lines += [noisy_("Tổng cộng", rng), money(total), noisy_("Cảm ơn quý khách", rng)]
    return {"merchant": merchant, "category": category, "label_source": "synthetic", "total_amount": total, "lines": lines}


def generate(per_category: int = 40, seed: int = 7, use_brands: bool = True, noise: bool = True) -> list[dict]:
    rng = random.Random(seed)
    return [make_bill(c, rng, use_brands, noise) for c in ITEMS for _ in range(per_category)]


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 40
    rows = generate(n)
    OUT.write_text("".join(json.dumps(r, ensure_ascii=False) + "\n" for r in rows), encoding="utf-8")
    print(f"Đã ghi {len(rows)} hóa đơn giả ({n}/danh mục) vào {OUT.name}; dataset.jsonl không bị thay đổi.")
