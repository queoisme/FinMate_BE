# Nội dung cần nạp cho AI Financial Agent

Tình trạng đo ngày 2026-11-08: **0 tài liệu tri thức** (`agent_kb_documents`) và **0 giả định kinh tế đã duyệt**. Vì vậy mọi câu hỏi về tri thức hoặc giả định đều ra `insufficient_data` (9/9 câu `knowledge` của bộ v1), và dự báo mục tiêu giữ giá không đổi theo thời gian.

Cách nạp: trang admin → *Giả định kinh tế* (`/api/v1/admin/assumptions`) và *Tri thức* (`/api/v1/admin/knowledge`). Staff soạn bản nháp, **OpsManager hoặc Admin duyệt**. Agent chỉ dùng bản đã duyệt. Mọi con số phải lấy từ trang nguồn do người nhập mở và kiểm; không điền theo trí nhớ. `sourceUrl` phải là trang cụ thể (không phải trang chủ), `asOf` là ngày số liệu.

## 1. Giả định kinh tế (`economic_assumptions`)

| Khóa | Ý nghĩa | Đơn vị | Nguồn nên dùng | Bắt buộc |
|---|---|---|---|---|
| `general_price_growth` | Lạm phát bình quân hằng năm; mặc định của `forecast_goal` và `sensitivity` | `pct_per_year` | báo cáo CPI của Tổng cục Thống kê (gso.gov.vn) | **có** |
| `house_price_growth` | Tăng giá nhà ở / căn hộ | `pct_per_year` | báo cáo thị trường bất động sản của cơ quan nhà nước hoặc đơn vị nghiên cứu có phương pháp công bố | nên có |
| `car_price_growth` | Tăng giá ô tô | `pct_per_year` | số liệu giá niêm yết hãng / Hiệp hội các nhà sản xuất ô tô Việt Nam (VAMA) | nên có |
| `tuition_growth` | Tăng học phí | `pct_per_year` | quy định / thông báo học phí của Bộ Giáo dục và Đào tạo hoặc trường | tùy chọn |
| `wage_growth` | Tăng lương bình quân | `pct_per_year` | Tổng cục Thống kê, báo cáo lao động việc làm | tùy chọn |
| `savings_deposit_rate` | Lãi suất tiền gửi kỳ hạn 12 tháng | `pct_per_year` | biểu lãi suất công bố của ngân hàng, hoặc Ngân hàng Nhà nước | tùy chọn |

Lưu ý: agent hỏi theo tên khóa, nên khóa chưa có cho kết quả `assumption_missing` (giữ giá không đổi, có ghi rõ trong câu trả lời) chứ không bịa. Nếu thêm khóa mới thì chỉ cần nạp qua admin; không cần đổi mã.

## 2. Tài liệu tri thức (`agent_kb_documents`)

Mỗi tài liệu: tiêu đề, nội dung ngắn gọn dễ trích, nguồn (`sourceName`, `sourceUrl`), ngày. Tên tài liệu là tên nguồn mà bộ đo dùng để chấm truy xuất (`expect.relevant_sources`).

**Của riêng FinMate (nhóm sản phẩm viết, phải khớp hành vi thật của app):**
- Cảnh báo ngân sách mốc T1 và T2; cách tính mục tiêu tiết kiệm; dải dự báo thu nhập và độ tin cậy; số dư thấp nhất trong kỳ; mục tiêu theo phần trăm so với theo số tiền.
- Các phương pháp quản lý tiền mà FinMate gợi ý (phong bì, 50/30/20, ngân sách từ số 0, cuốn chiếu, hũ tiền): lấy từ chính cấu hình phương pháp trong hệ thống để không mâu thuẫn với app.

**Kiến thức tài chính cá nhân chung (cần nguồn):**
- Quỹ khẩn cấp (mấy tháng chi tiêu, giữ ở đâu), thu nhập không ổn định, chi tiêu theo mùa (học phí, Tết).
- Trả nợ (tuyết lăn, ưu tiên nợ lãi cao), tỷ lệ nợ trên thu nhập, lãi kép, chi phí cơ hội.
- Thuế thu nhập cá nhân với tiền lương, bảo hiểm y tế / xã hội ảnh hưởng thế nào đến kế hoạch: nguồn là văn bản pháp luật hiện hành, ghi số hiệu và ngày hiệu lực.

Không nạp: khuyến nghị mua cổ phiếu, coin, vàng hay sản phẩm đầu tư cụ thể. Agent từ chối loại câu này theo thiết kế (`refusal_reason: investment_advice`).

## 3. Kiểm sau khi nạp

1. Gọi `POST /api/v1/internal/agent/run` (môi trường Development) với một câu về từng khóa và từng chủ đề: phải có bằng chứng `source = document` kèm `sourceName` và ngày.
2. Khóa còn nháp hoặc đã gỡ vẫn phải ra `assumption_missing`.
3. Gắn `expect.relevant_sources` vào các câu `knowledge` trong `ai-service/agent_eval/dataset/questions.jsonl` theo tên tài liệu vừa duyệt, rồi chạy lại để có `retrieval_recall_at_5`.
