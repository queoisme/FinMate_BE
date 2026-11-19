# Dữ liệu phát triển dashboard

Đã nạp ngày 19/11/2026 trên nhánh `dash_board_dev`, BE chính `http://127.0.0.1:8080`, dashboard `http://127.0.0.1:5173`. Database QA ở cổng 8081/5174 không được nạp lại.

## Nguồn và phạm vi

Các tài khoản, giao dịch, ca hỗ trợ, bài cộng đồng, gói và đơn đăng ký là **tình huống mô phỏng nghiệp vụ**, được gắn `[Kiểm thử]`, email `dashboard-*@finmate.local` hoặc mã `DEV-DASH-*`. Đây không phải dữ liệu khách hàng hay tiền thanh toán thật. Danh mục hệ thống, ngân hàng, khảo sát và phương pháp có sẵn được giữ nguyên.

Số liệu kinh tế là dữ liệu công khai đã đối chiếu, có nguồn và ngày số liệu. Tất cả sáu giả định để ở **bản nháp**, không tự thay tỷ lệ đang áp dụng:

| Số liệu | Giá trị | Ngày số liệu | Nguồn |
|---|---:|---|---|
| CPI bình quân 2024 | 3,63% | 31/12/2024 | [Cục Thống kê](https://www.nso.gov.vn/du-lieu-va-so-lieu-thong-ke/2025/01/thong-cao-bao-chi-ve-tinh-hinh-gia-thang-muoi-hai-quy-iv-va-nam-2024/) |
| Lạm phát cơ bản 2024 | 2,71% | 31/12/2024 | Cùng nguồn 2024 |
| CPI bình quân 2025 | 3,31% | 31/12/2025 | [Cục Thống kê](https://www.nso.gov.vn/du-lieu-va-so-lieu-thong-ke/2026/01/thong-cao-bao-chi-ve-tinh-hinh-gia-thang-muoi-hai-quy-iv-va-nam-2025/) |
| Lạm phát cơ bản 2025 | 3,21% | 31/12/2025 | Cùng nguồn 2025 |
| Giá nhóm ăn uống 2025 | 3,27% | 31/12/2025 | Cùng nguồn 2025 |
| Giá nhóm nhà ở, điện nước 2025 | 6,08% | 31/12/2025 | Cùng nguồn 2025 |

Giá E10 RON95-III kỳ 19/11/2026: vùng 1 **19.100 đồng/lít**, vùng 2 **19.480 đồng/lít**, từ 15:00 giờ VN. [Nguồn công bố của Phúc Lộc Ninh Petrol](https://phuclocninh.vn/phuclocninhpetrol-dieu-chinh-gia-xang-dau-tu-15-gio-00-phut-ngay-19-11-2026/). Bản ghi ghi rõ nhập thủ công, đã đánh dấu xử lý cảnh báo để không phát cảnh báo từ việc nạp dữ liệu.

Ba nguồn thị trường đang tắt và chưa bật quyền gửi AI. Ba liên kết tài liệu chỉ có metadata, chưa được coi là crawl hoặc AI xử lý thành công. Không gieo sự kiện thị trường, lỗi giám sát, bằng chứng thanh toán, vector embedding hoặc số đo AI giả.

## Tài khoản kiểm thử

Mật khẩu chung lấy từ `DEV_SEED_DASHBOARD_PASSWORD` trong `backend/.env` (đã bị Git bỏ qua). Bản thông tin đăng nhập trên máy: `D:/Capstone/.local-run/dashboard-dev-seed/credentials.local.json`. Không ghi mật khẩu vào mã nguồn hoặc tài liệu này.

| Email | Vai trò/tình huống |
|---|---|
| `dashboard-admin@finmate.local` | Quản trị viên |
| `dashboard-staff@finmate.local` | Nhân viên hỗ trợ, có ca được phân công |
| `dashboard-manager@finmate.local` | Quản lý vận hành, duyệt đề xuất |
| `dashboard-an@finmate.local` | Thu nhập ổn định, hai ví, ngân sách, mục tiêu và lịch sử 6 tháng |
| `dashboard-binh@finmate.local` | Thu nhập thay đổi theo tháng, ngân sách cần chú ý |
| `dashboard-chi@finmate.local` | Sinh viên, mức thu chi nhỏ, ngân sách gần hạn mức |
| `dashboard-dung@finmate.local` | Đang khóa, có đề xuất mở khóa chờ duyệt |
| `dashboard-ha@finmate.local` | Đang chờ xóa, có đề xuất hủy xóa; chưa đăng nhập được |
| `dashboard-linh@finmate.local` | Người mới, chưa tạo ví/giao dịch để thử trạng thái trống |

Các đồng ý/phân quyền trong bộ dữ liệu chỉ áp dụng cho tài khoản mô phỏng này. Không bật đồng ý huấn luyện hoặc gửi dữ liệu cho AI bên thứ ba. Không đổi mật khẩu, quyền, trạng thái hay cấu hình của tài khoản có sẵn.

## Các luồng có thể thử

- **Hàng đợi hỗ trợ:** 8 ca: 2 mới, 2 đang xử lý, 1 chờ khách, 1 chuyển cấp, 1 đã giải quyết, 1 đã đóng; có một ca quá hạn. Có 20 tin nhắn và 7 câu trả lời mẫu.
- **Xem dữ liệu theo ca:** Staff có quyền mô phỏng còn hạn ở ca hóa đơn của An, gồm ví/giao dịch/ngân sách/tạo nháp. Ca nghi trùng của Bình đang chờ đồng ý, API xem dữ liệu vẫn trả 403. Không mở rộng quyền ở vai trò có sẵn.
- **Duyệt hai người:** 3 đề xuất do Staff tạo, Manager/Admin có thể duyệt: mở khóa Dũng, hủy xóa Hà, chuyển chủ nhóm mô phỏng từ An sang Bình.
- **Tài chính:** 750 giao dịch mới gồm lịch sử đã xác nhận và 3 nháp; chuyển tiền giữa ví có cả hai đầu. Số dư được đối chiếu với lịch sử, ngân sách với giao dịch cùng kỳ; mục tiêu với các khoản đóng góp. Có 513 bản tổng hợp ngày và 6 khoản định kỳ.
- **Dự báo:** 9 bản dự báo mới được tính bằng dịch vụ dự báo thật sau khi nạp; chờ tới mốc mới có số thực tế. Hai phản hồi mô phỏng có trạng thái mới/đã gán nguyên nhân. Không bịa lịch sử dự báo hay điểm độ chính xác.
- **Cộng đồng:** 4 bài, 1 bình luận, 2 báo cáo chờ xử lý và 1 ảnh JPEG tạo cho kiểm thử, có chữ đánh dấu mô phỏng, không phải hóa đơn thật. Có thể thử xem ảnh, duyệt và xử lý báo cáo.
- **Gói đăng ký:** 2 gói mô phỏng đang tắt, 3 đơn ở trạng thái chờ/hết hạn/đã hủy. Không có đơn đã trả hay mã giao dịch SePay giả; không thay gói miễn phí có sẵn.
- **Giả định và thị trường:** 6 giả định nháp có nguồn, 3 nguồn tham khảo và giá xăng có ngày hiệu lực. Có thể mở nguồn và kiểm tra trước khi duyệt/bật.

KPI tổng hợp, telemetry thiết bị và mẫu hóa đơn học AI còn thiếu API theo backlog của dashboard; nạp giao dịch không tự làm các màn đó có nguồn dữ liệu. Thống kê AI không được làm đầy bằng giao dịch nhập tay.

## Nạp lại

Từ repo BE, khi Docker đang chạy:

```powershell
./backend/scripts/seed-dashboard-development.ps1
```

Script chỉ dùng database local `finmate_main` ở `127.0.0.1:5433` và Redis `6379` mặc định. Có thể chỉ định cổng local; không dùng cho production. Không chạy từ nhánh main/develop. Bộ nạp phải bật rõ ràng và chỉ chạy ở Development; khởi động backend thường không tự nạp.

Mỗi bản ghi có ID ổn định, bộ dữ liệu có marker hoàn thành trong audit. Tất cả bản ghi mô phỏng được ghi trong một transaction. Chạy lại không ghi đè thao tác đã thử và không tạo thêm dữ liệu; dự báo hiện tại vẫn dùng cơ chế một bản/người/ngày của hệ thống. Không có lệnh xóa/reset tự động.

## Kiểm chứng

- 20 kiểm thử tích hợp liên quan đạt, gồm 4 kiểm thử mới: điều kiện Development/opt-in, trùng tài khoản, số dư/ngân sách/mục tiêu, idempotence và rollback khi xung đột.
- Build C# và image Docker mới đạt; backend đã chạy lại trên 8080 và health đạt.
- 25 kiểm tra API đạt: hàng đợi, đề xuất, dữ liệu công khai, quyền xem dữ liệu theo ca, hồ sơ mới và dự báo.
- Đối chiếu 4 tài khoản cũ bằng fingerprint trước/sau: không thay đổi. Chạy lại trên DB chính: không tăng số người dùng/giao dịch/ca/giả định/bản dự báo cùng ngày.
- Đã kiểm tra các màn dashboard với DB chính. Bằng chứng: `D:/Capstone/outputs/finmate-dashboard-dev-data-2026-11-19`.
- Ảnh chờ duyệt tải đúng 800×500; chi tiết ca hiển thị trao đổi và câu trả lời mẫu. Dashboard đã bổ sung nhãn tiếng Việt cho mã thông số và đơn vị kinh tế; build/lint FE đạt.
- Đối chiếu cuối: không có số dư âm, ngân sách lệch chi tiêu hay mục tiêu lệch tổng đóng góp.
- Backup trước khi nạp: `D:/Capstone/.local-run/dashboard-dev-seed/backups/{main-before,ai-before}.dump`. AI DB không bị nạp dữ liệu mô phỏng trong đợt này.
