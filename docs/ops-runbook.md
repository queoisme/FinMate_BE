# Quy trình vận hành: Staff, Manager, Admin

Tài liệu cho đội vận hành. API và ma trận quyền ở `docs/ops-api.md`. Các mức SLA là mức khởi điểm, chỉnh trong nhóm cấu hình `support`.

## Bốn quy tắc

1. **Không ai sửa tiền thay khách.** Staff chỉ tạo giao dịch nháp, khách phải xác nhận.
2. **Chỉ xem dữ liệu khi có ca và được khách đồng ý.** Áp dụng cho cả Manager và Admin.
3. **Việc nhạy cảm cần hai người.** Không ai duyệt đề xuất của mình.
4. **Mỗi ca có đúng một người phụ trách.** Chuyển cấp phải ghi rõ đã thử gì.

## Phân vai

| | Staff | Manager | Admin |
|---|---|---|---|
| Phụ trách | Xử lý ticket, nói chuyện với khách | Hàng đợi, phân công, duyệt, điều phối khi có lỗi diện rộng | Key, cấu hình, log, sửa lỗi kỹ thuật, cấp vai trò |
| Chỉ số chính | Thời gian phản hồi, tỉ lệ xử lý xong ngay lần đầu | Tỉ lệ đúng SLA, ca tồn, điểm hài lòng | Uptime, thời gian khôi phục |

## Xử lý ca hằng ngày

1. Mở hàng đợi, nhận ca có hạn gần nhất.
2. Trả lời khách, dùng câu trả lời mẫu nếu có. Ghi lại nhận định bằng ghi chú nội bộ.
3. Cần xem dữ liệu thì xin quyền, chỉ đúng phần cần và ghi lý do dễ hiểu: khách sẽ đọc lý do đó.
4. Giải quyết xong thì chuyển sang `Resolved`. Ca tự đóng sau 7 ngày nếu khách không phản hồi.
5. Vượt khả năng thì chuyển cấp, ghi rõ đã thử gì.

## AI không đọc được hóa đơn

1. Nhờ khách gửi ảnh vào ticket, vì ảnh quét không được lưu ở đâu.
2. Xin quyền phạm vi `Draft`; khách chọn ví khi đồng ý.
3. Đọc ảnh và tạo giao dịch nháp. Khách xác nhận trong app.
4. Nếu lỗi lặp lại ở nhiều khách, báo Manager.

## Giám sát hệ thống: việc phải làm một lần

Hệ thống **không tự báo được** trong hai trường hợp, nên cần chuẩn bị trước:

| Trường hợp | Vì sao | Cần làm |
|---|---|---|
| Backend hoặc Postgres sập | Job giám sát chạy trong backend và lịch Hangfire nằm trong Postgres | Đăng ký dịch vụ theo dõi bên ngoài (ví dụ UptimeRobot), gọi `https://<domain>/health/ready` mỗi phút, báo về email khác hoặc điện thoại |
| Brevo hỏng | Email là kênh cảnh báo duy nhất | Mở trang trạng thái khi nghi ngờ; log có dòng mức Error. Đặt thêm cảnh báo của chính Brevo nếu có |

Admin và OpsManager phải dùng email thật, vì cảnh báo gửi về địa chỉ tài khoản.

## Lỗi diện rộng

1. **Phát hiện:** hệ thống tự mở sự cố và email cho Admin, OpsManager. Với OCR, sự cố `Google Vision đang lỗi` thường là key hết hạn mức hoặc không còn hiệu lực. Manager cũng có thể tự thấy qua nhiều ca cùng loại trên bảng điều hành.
2. **Nhận xử lý:** Manager bấm nhận để hệ thống ngừng nhắc, và báo Admin nếu Admin chưa nhận.
3. **Khách hàng:** nếu có khách bị ảnh hưởng, Manager chọn một ca làm ca cha, gom các ca còn lại, trả lời hàng loạt. Staff ngừng xử lý từng ca riêng lẻ.
4. **Khắc phục:** Admin xem `technicalDetail` của sự cố và danh sách lỗi. Với lỗi key, đổi ở `/admin/secrets` và kiểm tra bằng `/test`. Cần chi tiết hơn thì dùng `traceId` tra `docker logs`.
5. **Kết thúc:** sự cố tự đóng khi dịch vụ tốt lại, hoặc đóng tay kèm ghi chú. Manager trả lời hàng loạt với `resolve = true`. Trong 2 ngày, Manager và Admin viết báo cáo nguyên nhân và cách phòng lặp lại.

Trong suốt sự cố, Manager là người duy nhất điều phối và phát ngôn, còn Admin tập trung sửa lỗi.

## Tài khoản khách

| Tình huống | Staff | Manager | Admin |
|---|---|---|---|
| Nghi tài khoản bị chiếm | Khóa khẩn cấp theo ca | Rà soát ca đã chuyển cấp | |
| Mở khóa | Đề xuất sau khi xác minh | Duyệt | |
| Khách đổi ý về yêu cầu xóa tài khoản | Đề xuất trong 30 ngày | Duyệt | |
| Cấp hoặc thu hồi vai trò nội bộ | | Đề nghị | Thực hiện |

Khách bị khóa hoặc đã yêu cầu xóa thì không đăng nhập được, nên không gửi ticket trong app được. Đội cần thống nhất kênh liên hệ và cách xác minh chính chủ cho trường hợp này.

## Tranh chấp trong nhóm

1. Staff nhận ca, xem thành viên và lịch sử nhóm. Không có quyền xem giao dịch của nhóm.
2. Nếu chủ nhóm lạm quyền hoặc mất liên lạc, Staff đề xuất chuyển chủ, Manager duyệt.
3. Mọi thành viên được thông báo khi nhóm đổi chủ.

## Thay đổi hệ thống

- **Thường:** Admin ghi lý do và cách hoàn tác, Manager báo trước cho Staff.
- **Rủi ro cao** (bật cờ tính năng, đổi ngưỡng, Điều khoản mới): có người thứ hai xem qua.
- **Khẩn cấp:** Admin làm trước, bổ sung hồ sơ trong 24 giờ.

## Nhịp vận hành

- **Mỗi ca:** bàn giao ca đang mở, ca sắp trễ hạn, sự cố đang diễn ra.
- **Mỗi ngày:** Manager xem ca tồn, ca trễ và trạng thái hệ thống. Admin đọc email gom sự cố mức thường lúc 08:00.
- **Mỗi tuần:** chấm mẫu vài ca của mỗi Staff, xem lỗi lặp lại, họp ngắn với Admin.
- **Mỗi tháng:** Admin rà quyền truy cập, hạn key, hạn mức; Manager rà nhật ký xem dữ liệu khách.
