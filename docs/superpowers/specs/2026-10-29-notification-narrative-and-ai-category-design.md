# Tách "nội dung" khỏi thông báo ngân hàng và phân loại bằng model AI khi từ điển không chắc — thiết kế

Ngày: 2026-10-29. Trạng thái: chờ duyệt. Chưa viết code. Phạm vi: `ai-service` (+ một thay đổi nhỏ ở `backend` — xem mục 5, cần duyệt riêng).

## 1. Vấn đề và mục đích

Giao dịch tạo từ thông báo ngân hàng hiện hiện **cả đoạn text** ("TK 1234 GD: -1,000,000VND luc ... SD: ... ND: mua quan ao") trong lịch sử, và danh mục thường là "Khác". Hai nguyên nhân (đã đọc code):
- `extractor.py`: khi ngân hàng/mẫu chưa có định dạng khai báo, nhánh `extract_generic` gán `description = cả đoạn thông báo` (dòng ~181); nhánh pattern cũng làm vậy nếu mẫu không có trường mô tả (dòng ~153).
- `categorizer.py`: chỉ so khớp từ khóa trong từ điển (~200 từ); không khớp thì trả `other` độ tin cậy thấp. Không có model nào được cài.

Mục đích: (A) description chỉ còn phần nội dung người chuyển/cửa hàng ghi; (B) khi từ điển không chắc, nhờ model AI bên thứ ba (Gemini, dự phòng sẵn có) đoán danh mục từ phần nội dung đó.

## 2. Mục tiêu, không phải mục tiêu, điều phải tránh

**Mục tiêu**
- A2: nhãn "nội dung" (`ND:`, `Noi dung:`, …) nằm trong **DB** (tài liệu cấu hình admin sửa được), nhánh generic và nhánh pattern-không-có-mô tả cắt phần sau nhãn làm `description`.
- B1: chỉ khi từ điển không khớp **và** người dùng đã đồng ý AI **và** là khoản chi, gửi đúng phần nội dung (đã che dãy số dài) cho Gemini; kết quả hợp lệ → danh mục.
- Mọi lỗi/quá hạn của AI → giữ kết quả luật (`other`); không bao giờ làm hỏng `/analyze`.

**Không phải mục tiêu**: huấn luyện model; sửa FE; đổ từ điển `merchant_lexicon` vào DB (việc riêng); thêm định dạng VietinBank/MB (dữ liệu, làm bằng admin); lưu kết quả AI làm nhãn huấn luyện (nhãn chỉ đến từ người dùng).

**Điều phải tránh**: log nội dung thông báo/narrative/số tiền (AGENTS.md §3.2); gửi số tiền, số dư, số tài khoản, người dùng cho bên thứ ba; gửi khi người dùng không đồng ý AI; để `/analyze` chờ quá lâu (Android chờ BE tối đa 15 giây).

## 3. Các hướng đã cân nhắc

| | Nội dung | Kết quả |
|---|---|---|
| A1 | Khai báo thêm định dạng trong DB cho từng mẫu | Giữ như cách bổ sung dữ liệu, nhưng không đủ cho mẫu chưa biết |
| **A2** | **Danh sách nhãn trong DB cho nhánh dự phòng** | **Chọn** |
| **B1** | **Dùng lại client Gemini + `classify_items` (bộ nhớ, cầu dao) khi từ điển không chắc** | **Chọn** |
| B2 | Lời gọi riêng + lời nhắc riêng cho thông báo | Để sau |

## 4. Thiết kế

### 4.1 A2 — tài liệu cấu hình `narrative_labels`
- Đăng ký thêm một `Spec` trong `config_documents.SPECS` (cơ chế có sẵn: phiên bản, kích hoạt, xem trước, nhật ký; bảng `config_documents` **không đổi**). Nội dung: `{"labels": ["nd", "noi dung", "ndung", ...]}` — chữ thường, bỏ dấu; validate: mảng chuỗi 2–20 ký tự, không trùng, tối đa N nhãn.
- Bản mặc định: `app/data/narrative_labels.json` (chỉ là **seed**). Lúc khởi động, nếu DB chưa có phiên bản nào thì chép bản mặc định vào DB thành phiên bản 1 active (chỉ thêm, DB thắng), đúng nguyên tắc dữ liệu chạy từ DB. Có phiên bản DB thì chạy theo DB và áp dụng ngay khi admin kích hoạt (như `merchant_lexicon`).
- Cơ chế: `extract_description(text)` — chuẩn hóa (bỏ dấu, chữ thường) để tìm nhãn đứng trước dấu `:` hoặc khoảng trắng, cắt phần sau nhãn từ nhãn **cuối cùng khớp ở cuối chuỗi** (nội dung luôn ở cuối thông báo), trả phần sau, bỏ khoảng trắng/dấu thừa, tối đa 255. Không thấy nhãn → giữ như cũ (cả đoạn). Dùng ở `extract_generic` và `_from_pattern` khi mẫu không có trường mô tả.

### 4.2 B1 — danh mục bằng Gemini khi từ điển không chắc
- Điều kiện gọi (tất cả): kết quả luật là nhánh dự phòng (`other`, không khớp từ điển, không phải `income`); khoản chi; `allow_ai = true` (mục 5); nội dung không rỗng sau khi bỏ số tiền; không phải nội dung sức khỏe (như chat: `resolve(...).slug != "health"`).
- Cách gọi: dùng client Gemini **của chat** (`get_chat_gemini_client`: model nhanh, cầu dao riêng, trần 3,5 s/model và 7,5 s cả chuỗi, không đi OpenRouter) qua `classify_items` (có bộ nhớ tên món dùng chung, hướng dẫn nhóm lấy từ DB `kb_ai_instructions`), bọc `asyncio.wait_for` với trần riêng `NOTIFICATION_AI_TIMEOUT_SECONDS` (mặc định 6,0 s, ghi trong `Settings`). Chỉ gửi **một** cụm nội dung đã qua `anonymize_body` (che dãy số ≥ 8 chữ số) và bỏ số tiền.
- Kết quả: slug hợp lệ trong `CATEGORY_SLUGS`, khác `income`, nguồn `ai`/`cache` → dùng làm danh mục. `unknown`/lỗi/quá hạn → giữ `other`. Độ tin cậy lấy từ bảng độ tin cậy hiện có (khóa mới `categorizer.ai`, đặt **dưới 0,85** để khoản này luôn đi nhánh hộp thoại cho người dùng xác nhận, đúng quy tắc "AI khác luật thì không tự lưu").
- `Categorizer.categorize` giữ nguyên (đồng bộ, luật/ML); thêm bước async trong `orchestrator` sau nó. Response `/analyze` **không đổi cấu trúc**.

### 4.3 Lỗi và biên
Gemini tắt/không có khóa → bỏ qua. Cầu dao mở → bỏ qua. Narrative rỗng sau khi che → không gửi. Hai thông báo cùng nội dung → bộ nhớ trả luôn, không gọi lại. Đồng ý AI tắt → không gửi gì (chỉ luật).

## 5. Thay đổi hợp đồng BE ↔ ai-service (cần bạn duyệt riêng — AGENTS.md §5)

Hiện `POST /api/v1/analyze` không mang cờ đồng ý AI, trong khi quét hóa đơn và chat đều gửi `allow_ai` (BE đọc `IAiConsentReader` mỗi lần). Để B1 tôn trọng đồng ý của người dùng:
- ai-service: `AnalyzeRequest` thêm `allow_ai: bool = False` (tùy chọn, mặc định tắt: BE cũ không gửi thì không bao giờ gọi AI).
- backend: `AnalyzeNotificationCommandHandler` đọc `IAiConsentReader` rồi đặt `AllowAi` trong `AnalyzeRequest` (C#) gửi đi; thêm test đúng kiểu `ScanReceiptSplitTests` (có đồng ý → true; không đồng ý/không có người dùng → false).
Tương thích ngược hai chiều. Không đổi schema DB.

## 6. Kiểm thử
ai-service (pytest, đúng cấu trúc `tests/unit` hiện có): nhãn tách đúng với mẫu của bạn ("…ND: mua quan ao" → "mua quan ao"; không nhãn → cả đoạn; nhiều chữ "nd" trong tên người nhận không cắt sai); validate/seed tài liệu `narrative_labels` (idempotent, DB thắng); orchestrator với client Gemini giả: chỉ gọi khi không khớp từ điển + `allow_ai` + khoản chi, không gọi khi tiền vào/từ điển khớp/sức khỏe/không đồng ý; lỗi/quá hạn/`unknown` giữ `other`; không gửi số tiền/dãy số dài; response `/analyze` giữ nguyên cấu trúc. backend: hai test cờ `allow_ai`. Sau khi xong: chạy lại test liên quan, cập nhật `.context/TASKS.md` (AGENTS.md §6).

## 7. Cần bạn xác nhận
1. Thêm cờ `allow_ai` vào hợp đồng analyze (mục 5): đồng ý không?
2. Trần AI cho thông báo 6 giây, độ tin cậy `categorizer.ai` dưới 0,85 (gợi ý 0,70): đồng ý không?
3. Người xây tự quyết: danh sách nhãn mặc định (gợi ý `nd`, `noi dung`, `ndung`, `content`), số nhãn tối đa, tên khóa độ tin cậy.
