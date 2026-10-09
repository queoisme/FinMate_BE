"""Luật phân loại từng món trên hóa đơn (phần 1-3 của bản nháp đã duyệt): nhóm, luật chốt khi nhập nhằng, ví dụ.

Đây là bản SEED cho bảng ``bill_ai_instructions`` (migration 0008). Sau đó nội dung sống trong DB và sửa ở đó, không build lại.
Phần 0 (nhiệm vụ, an toàn) và phần 4 (schema kết quả) do code giữ, KHÔNG nằm ở đây.
"""

ITEM_RULES_TEXT = """## 1. Các nhóm

Chỉ dùng đúng các giá trị sau (10 nhóm chi tiêu của BE, bỏ `income`, cộng `unknown`):

| Giá trị | Tên | Gồm |
|---|---|---|
| `food` | Ăn uống | Đồ ăn, đồ uống, cà phê, trà sữa, nguyên liệu nấu ăn, rau thịt cá, bánh kẹo, nước ngọt, bia rượu, thực phẩm đóng gói |
| `transport` | Di chuyển | Xăng dầu, gửi xe, vé xe, vé tàu xe máy bay, taxi, xe công nghệ, phí cầu đường, rửa xe, thay nhớt, phụ tùng xe |
| `shopping` | Mua sắm | Quần áo, giày dép, túi xách, phụ kiện, mỹ phẩm, chăm sóc cá nhân, đồ điện tử, đồ gia dụng, trang sức, quà tặng, văn phòng phẩm thông thường |
| `education` | Giáo dục | Học phí, khóa học, sách giáo khoa, sách học tập, dụng cụ học tập |
| `housing` | Nhà ở | Tiền thuê nhà hoặc phòng, điện, nước, gas, phí rác, phí quản lý chung cư, sửa chữa nhà |
| `bills` | Hóa đơn | Internet, điện thoại, nạp tiền điện thoại, gói data, truyền hình cáp, phí thuê bao dịch vụ |
| `entertainment` | Giải trí | Vé xem phim, game, nạp thẻ game, karaoke, khu vui chơi, vé tham quan, concert, bắp nước ở rạp |
| `health` | Sức khỏe | Thuốc, khám bệnh, xét nghiệm, nha khoa, kính mắt (gọng, tròng), thực phẩm chức năng, vitamin, khẩu trang, dụng cụ y tế |
| `family` | Gia đình | Tã, sữa bột, đồ dùng cho em bé, đồ chơi trẻ em, đồ cho người thân |
| `other` | Khác | Làm đẹp và dịch vụ cá nhân (cắt tóc, spa, nail), giặt ủi, chuyển phát, in ấn, thú cưng, và những thứ thật sự không thuộc nhóm nào ở trên |
| `unknown` | Không chắc | Dùng khi không đoán được. Đừng ép vào một nhóm |

## 2. Luật chốt khi nhập nhằng

1. **Mỹ phẩm, sữa rửa mặt, kem chống nắng, son, nước hoa** là `shopping`, không phải `health`.
2. **Thực phẩm chức năng, vitamin, thuốc, khẩu trang, gạc y tế, que thử** là `health`.
3. **Kính mắt đo độ (gọng, tròng)** là `health`. **Kính mát thời trang** là `shopping`.
4. **Đồ ăn, đồ uống đóng gói mua ở siêu thị hay cửa hàng tiện lợi** vẫn là `food`, không phải `shopping`.
5. **Đồ dùng tiêu hao trong nhà** (bột giặt, nước rửa chén, giấy vệ sinh, nước xả vải) là `shopping`.
6. **Sữa bột và tã cho em bé** là `family`, không phải `food` hay `shopping`. Sữa tươi, sữa chua cho người lớn là `food`.
7. **Sách giáo khoa, sách luyện thi, khóa học** là `education`. **Truyện, tiểu thuyết, văn phòng phẩm thông thường** là `shopping`.
8. **Điện, nước, gas, rác, phí chung cư** là `housing`. **Internet, điện thoại, truyền hình** là `bills`.
9. **Phụ phí** (phí giao hàng, phí dịch vụ, phí đóng gói, túi nilon) là `other`.
10. **Dịch vụ làm đẹp** (cắt tóc, gội đầu, nail, spa, massage) là `other`.
11. **Tên bằng tiếng nước ngoài hoặc tên thương hiệu**: nhận ra thì phân loại theo loại hàng (Sting, Oreo, Omo, Pepsi...). Không nhận ra thì `unknown`.
12. **Tên quá ngắn hoặc chỉ là mã** (SP002993, A12, "hang khac") là `unknown`.

## 3. Ví dụ mẫu

| Tên món | Nhóm |
|---|---|
| Trà Bí Đao Ngô Gia (Vừa 700cc) | `food` |
| Cà phê sữa đá | `food` |
| Thịt ba chỉ 500g | `food` |
| Nước ngọt Sting 330ml | `food` |
| Sữa rửa mặt Cerave 236ml | `shopping` |
| Kem chống nắng La Roche-Posay | `shopping` |
| Bột giặt Omo 3kg | `shopping` |
| Paracetamol 500mg | `health` |
| Vitamin C 1000mg | `health` |
| Khẩu trang y tế 4 lớp | `health` |
| Tròng kính chống ánh sáng xanh | `health` |
| Tã quần Bobby size L | `family` |
| Sữa bột Enfa A+ 800g | `family` |
| Vé xem phim 2D | `entertainment` |
| Bắp rang bơ + nước | `entertainment` |
| Xăng RON95 | `transport` |
| Gửi xe máy | `transport` |
| Cước internet tháng 10 | `bills` |
| Nạp tiền điện thoại 50k | `bills` |
| Tiền điện tháng 9 | `housing` |
| Phí quản lý chung cư | `housing` |
| Cắt tóc nam | `other` |
| Phí giao hàng | `other` |
| Sách luyện thi IELTS | `education` |
| SP002993 | `unknown` |
"""
