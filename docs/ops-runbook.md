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
| Giá xăng không xác nhận được (`market_data`) | Các nguồn lỗi hoặc lệch nhau nhiều lượt liền, cảnh báo giá xăng tạm ngừng | Xem `lastErrorCode` ở `GET /admin/market-sources`. Thêm nguồn khác, hạ `minAgreeingSources`, hoặc sửa giá bằng `POST /admin/fuel-prices` |
| Backend hoặc Postgres sập | Job giám sát chạy trong backend và lịch Hangfire nằm trong Postgres | Đăng ký dịch vụ theo dõi bên ngoài (ví dụ UptimeRobot), gọi `https://<domain>/health/ready` mỗi phút, báo về email khác hoặc điện thoại |
| Brevo hỏng | Email là kênh cảnh báo duy nhất | Mở trang trạng thái khi nghi ngờ; log có dòng mức Error. Đặt thêm cảnh báo của chính Brevo nếu có |

Admin và OpsManager phải dùng email thật, vì cảnh báo gửi về địa chỉ tài khoản.

## Kiểm duyệt cộng đồng

API ở `docs/ops-api.md`, phần "Kiểm duyệt cộng đồng". Nhân viên xử lý nội dung và không biết ai viết; đừng tìm cách suy ra.

**Ảnh chờ duyệt.** Không ai duyệt thì ảnh chờ mãi và tới trần 500 ảnh, người dùng không tải được nữa. Từ chối ảnh đồi trụy, bạo lực, ảnh có giấy tờ tùy thân, thẻ ngân hàng, số tài khoản hoặc hóa đơn còn thông tin cá nhân, ảnh trẻ em hoặc người khác không liên quan tới nội dung, và quảng cáo. Ảnh trông như nội dung xâm hại trẻ em thì chuyển ngay cho Manager, không tải về và không chia sẻ lại.

**Báo cáo.** Xử lý theo thứ tự hàng đợi. Giữ báo cáo thì nội dung bị gỡ, bỏ báo cáo thì nội dung hiện lại.

- `scam`: quảng cáo cho vay, kêu gọi đầu tư, đa cấp. Gỡ, nếu tái phạm thì nhờ Manager cấm đăng.
- `personal_info`: nêu tên, số điện thoại, số tài khoản của người khác hoặc của chính họ. Gỡ.
- `self_harm`: xem ngay. Bài không tự ẩn; đừng gỡ chỉ vì bài buồn. **Hệ thống hiện chưa có cách liên hệ với tác giả ẩn danh**, và chưa có đường dây hỗ trợ được kiểm chứng trong tài liệu này. Trước khi bật cờ `features.community`, chủ sản phẩm phải chốt hai việc đó; trong lúc chưa có, báo ngay cho Manager.

**Cấm đăng.** Chỉ Manager, theo nội dung vi phạm, tối đa 365 ngày. Người bị cấm vẫn đọc được.

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

## Đơn đăng ký cần kiểm tra (`NeedsReview`)

Có sự cố `subscription_payments` nghĩa là khách đã trả tiền nhưng hệ thống chưa cấp gói. Hệ thống không có nút cấp gói hay hoàn tiền tay trong đợt này. Mã đơn nằm ở `GET /ops/subscriptions/orders?status=NeedsReview`; Admin xem được mã đơn trong chi tiết kỹ thuật của sự cố.

1. Đối chiếu đơn với trang quản lý giao dịch của SePay theo `invoiceNumber`.
2. `amount_mismatch`: số tiền hoặc tiền tệ khác đơn. Danh sách đơn không có id người dùng, nên việc với khách làm khi họ mở ticket kèm mã đơn. Hoàn tiền trên SePay nếu cần.
3. `user_missing`: tài khoản đã bị xóa hẳn trước khi tiền về, không còn cách liên hệ qua hệ thống. Hoàn tiền trên SePay.
4. `void_before_payment`: SePay báo hủy giao dịch khi đơn chưa ghi nhận tiền. Kiểm tra trên SePay xem tiền còn hay không. Đơn này không tự cấp gói.
5. Ghi kết quả vào phần giải quyết sự cố rồi đóng. Sự cố không mở lại.

## Bật gói đăng ký

Trước khi bật cờ `features.subscriptions`:

1. Xác nhận với chính sách thanh toán của Google Play rằng được dẫn khách ra web để mua.
2. Điều khoản dịch vụ trả phí, chính sách hoàn tiền, nghĩa vụ xuất hóa đơn đã được duyệt.
3. Đã kích hoạt cổng SePay, cấu hình `SEPAY_*`, `SUBSCRIPTION_WEB_*` và khai báo URL IPN kiểu `SECRET_KEY`.
4. Đã chạy một lần mua thật trên Sandbox rồi trên Production.
5. Manager đã chốt hạn mức gói miễn phí và tạo gói trả phí. Bật cờ là hạn mức có hiệu lực ngay với mọi người dùng.
6. App và web đã có màn hình, app xử lý được `SUBSCRIPTION_FEATURE_LOCKED` và `SUBSCRIPTION_LIMIT_REACHED`.
