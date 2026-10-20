# Ghi chú về các bảng dữ liệu mặc định

JSON không có chú thích, nên lý do đứng sau từng mục (từ nhập nhằng đã loại, nguồn học, chuỗi đã đóng cửa...) được giữ ở đây. Sửa bảng thì cập nhật cả ghi chú. Nguồn sự thật khi chạy thật là DB (`bill_brands`, `bill_keywords`); các file này chỉ để khởi tạo, chạy test và dự phòng khi không có DB.

## Thương hiệu (`data/brands.json`)

Mỗi mục có `note` (chú thích đứng cạnh) và `group` (chú thích đầu nhóm: nguồn nhập, tên đã loại và lý do). Chú thích cấp bảng (từ brands.py cũ):

- Tên chuẩn (có dấu) để hiển thị và lưu cho từng thương hiệu ở trên.
- Chuỗi phổ biến thêm sau: khóa -> (danh mục, tên hiển thị). Khóa phải đặc trưng (>= 5 chữ, đã bỏ dấu) vì khớp theo chuỗi con.
- Bảng nền mở rộng: chuỗi quán ăn / siêu thị / thời trang / giày dép / trang sức / mỹ phẩm / điện máy... phổ biến ở Việt Nam.
- Quy tắc: khóa viết thường, bỏ dấu, đặc trưng (tránh từ thông dụng như "mango", "ananas" trùng "bananas", "phu quy"...).
- ponytail: danh sách viết tay, không đầy đủ; chuỗi thiếu thì người dùng Lưu một lần là app nhớ. Thêm chuỗi mới vào data/brands.json.
- Tên ngắn hoặc là từ thông dụng: chỉ khớp khi đứng thành cả từ (không khớp giữa chữ khác như "Nikel", "Junoon", "Havana Vans").
- Rút ra từ trường "khop_an_toan" của đợt nghiên cứu và test nhận nhầm.

## Từ khóa tên cửa hàng (`data/merchant_keywords.json`)

- nhóm `groceries`, đứng cạnh `"groceries": ("sieu thi", "mart", "coop", "bach hoa xanh", "winmart", "tap hoa", "thuc pham", "cho dau moi", "`: đại lý nước uống: bán nước đóng chai/bình, không phải cước nước sinh hoạt

## Từ khóa sản phẩm (`data/product_keywords.json`)

- nhóm `dining` (chú thích đứng riêng): Bỏ các từ mơ hồ khi mất dấu: "duong" (đường ăn/đường phố), "lau" (lẩu/lầu), "chao" (cháo/chào), "muoi" (muối/mười).
- nhóm `groceries` (chú thích đứng riêng): món quen thuộc ở quán ăn nhanh, quán cơm, quán phở
- nhóm `groceries` (chú thích đứng riêng): học từ 12 bill ảnh 2026-10-14: "răng" (nha khoa) khớp nhầm "bắp rang", chặn luôn AI cho cả bill
- nhóm `healthcare` (chú thích đứng riêng): hàng tiêu dùng, thực phẩm tươi/đóng gói thường gặp ở tạp hóa, đại lý
- nhóm `entertainment` (chú thích đứng riêng): vé và dịch vụ vui chơi
- nhóm `other` (chú thích đứng riêng): dịch vụ làm đẹp/cá nhân (không có nhóm riêng nên là "other"): spa, salon, cắt tóc, nail, mi, triệt lông
- nhóm `shopping` (chú thích đứng riêng): phiếu mua/bán vàng bạc thường không có logo: nhận qua tên hàng
- nhóm `housing` (chú thích đứng riêng): hóa đơn mỹ phẩm không logo (siêu thị/nhà thuốc nhiều dòng khác nên danh mục của họ vẫn thắng)
- nhóm `shopping`, đứng cạnh `"bot giat", "nuoc rua chen", "giay ve sinh", "nuoc xa vai"`: đồ dùng gia đình tiêu hao là shopping, không phải thực phẩm
- nhóm `shopping`, đứng cạnh `"khan uot", "khan giay", "kem danh rang", "ban chai danh rang", "dau goi", "sua tam", "sua tam trang"`: đồ vệ sinh cá nhân dùng một lần
- nhóm `housing` (chú thích đứng riêng): quần áo, giày dép, túi xách, đồ điện tử nhỏ
- nhóm `housing` (chú thích đứng riêng): văn phòng phẩm ("bìa nút" không phải bia, "thước kẻ" không phải thuốc) và đồ vệ sinh nam
- nhóm `utilities`, đứng cạnh `"goi data", "cuoc di dong", "cap quang")`: "tien dienthoai" (xem _category_from_products) không bị "tien dien" (nhà ở) khớp nhầm

## Từ khóa câu chat/giọng nói (`data/chat_keywords.json`)

- nhóm `salary` (chú thích đứng riêng): thu nhập (loại chung "other_income" không có từ khóa: chỉ chọn bằng nút/tên loại, không đoán từ các từ chung như "thu nhập", "nhận được")
