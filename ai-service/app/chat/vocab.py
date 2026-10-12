"""Từ vựng cho câu chat/giọng nói ngắn ("đi ăn 200k", "mua sắm 1 triệu"): bổ sung cho bảng từ khóa của bill, vì chat dùng từ chung chung
("ăn", "nhậu", "chợ", "áo") mà bill hiếm khi có. Đã bỏ dấu, chữ thường; khớp nguyên từ, từ dài nhất thắng.
Hai khóa ``education`` và ``family`` KHÔNG nằm trong 9 nhóm của bill mà là slug backend dùng thẳng (route chat nhận cả hai kiểu).
ponytail: bảng nằm trong code; muốn sửa không cần build lại thì chuyển vào DB của AI (cần migration, chờ duyệt)."""

CATEGORY_LABELS = {
    "dining": "Ăn uống", "transport": "Di chuyển", "shopping": "Mua sắm", "groceries": "Siêu thị & tạp hóa", "housing": "Nhà ở",
    "education": "Giáo dục", "family": "Gia đình",
    "utilities": "Viễn thông & internet", "healthcare": "Sức khỏe", "entertainment": "Giải trí", "other": "Khác",
}

# Loại thu nhập (tách khỏi danh mục chi tiêu; từ vựng của chúng nằm chung trong CHAT_KEYWORDS bên dưới, khóa là mã loại).
INCOME_LABELS = {
    "salary": "Lương", "bonus": "Thưởng", "gift": "Được cho/tặng", "side_income": "Làm thêm & bán đồ", "refund": "Hoàn tiền", "other_income": "Thu nhập khác",
}

CHAT_KEYWORDS = {
    "dining": ("an", "an sang", "an trua", "an toi", "an khuya", "an vat", "an uong", "an lau", "an com", "an pho", "nhau", "di nhau", "nhau nhet", "lau", "nuong",
               "quan an", "nha hang", "do an", "dat do an", "do uong", "tra sua", "tra chanh", "ca phe", "cafe", "cf", "buffet", "tiec", "banh", "kem", "uong"),
    "groceries": ("cho", "di cho", "sieu thi", "di sieu thi", "tap hoa", "mua rau", "mua thit", "mua ca", "mua trung", "mua sua", "thuc pham", "dong gia", "lit sua", "hop sua"),
    "transport": ("xang", "do xang", "gui xe", "do xe", "taxi", "grab", "xanh sm", "gojek", "ve xe", "ve tau", "ve may bay", "rua xe", "sua xe", "thay nhot",
                  "di lai", "cau duong", "phi cau duong", "di chuyen", "xe om", "xe buyt"),
    "shopping": ("mua sam", "mua ao", "mua quan", "mua giay", "mua dep", "mua tui", "mua do", "mua dien thoai", "mua may tinh", "shopee", "lazada", "tiki", "tiktok shop",
                 "quan ao", "thoi trang", "my pham", "trang suc", "phu kien", "kinh ram"),
    "housing": ("tien nha", "tien tro", "thue nha", "thue tro", "tien dien", "tien nuoc", "tien gas", "tien rac", "dien nuoc", "phi chung cu", "nha o", "tien phong"),
    "utilities": ("wifi", "internet", "tien mang", "nap dien thoai", "nap tien dien thoai", "nap data", "nap 4g", "cuoc dien thoai", "truyen hinh", "hoa don dien thoai"),
    "healthcare": ("kham benh", "di kham", "kham", "benh", "mua thuoc", "thuoc", "nha thuoc", "benh vien", "nha khoa", "nieng rang", "kinh mat", "mat kinh", "kinh can", "trong kinh", "gong kinh", "eyewear", "tiem phong", "xet nghiem", "suc khoe"),
    "entertainment": ("xem phim", "di xem phim", "rap phim", "phim", "game", "nap game", "karaoke", "bowling", "ca nhac", "concert", "vui choi", "giai tri", "du lich"),
    # "sua bot" thắng "mua sua" (groceries) nhờ dài hơn; "sua" đứng một mình bị bỏ vì "sửa nhà" bỏ dấu cũng là "sua nha".
    "education": ("hoc phi", "khoa hoc", "hoc them", "luyen thi", "sach giao khoa", "tien hoc"),
    "family": ("sua bot", "mua sua bot", "binh sua", "ta quan", "ta dan", "do so sinh", "do cho be", "do choi tre em", "do choi"),
    "other": ("cat toc", "giat ui", "gui hang", "khac"),
    # thu nhập (loại chung "other_income" không có từ khóa: chỉ chọn bằng nút/tên loại, không đoán từ các từ chung như "thu nhập", "nhận được")
    "salary": ("luong", "nhan luong", "luong ve", "co luong", "luong thang"),
    "bonus": ("thuong", "tien thuong", "thuong tet", "hoa hong", "bonus"),
    "gift": ("duoc cho", "duoc tang", "me cho", "bo cho", "ba cho", "me gui", "bo gui", "ba gui", "li xi", "mung tuoi", "qua tang", "bieu"),
    "side_income": ("ban do", "ban xe", "ban duoc", "ban hang", "freelance", "lam them", "khach tra", "tien cong"),
    "refund": ("hoan tien", "tien hoan", "duoc hoan", "cashback"),
}
