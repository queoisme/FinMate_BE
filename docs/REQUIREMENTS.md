# FinMate — Đặc tả yêu cầu chức năng & phi chức năng

Cập nhật: 2026-09-22 · Bản sống (sửa, comment): https://claude.ai/code/artifact/a3155967-6d9e-4d91-aff5-07f16d5e0ac1

## 1. Giới thiệu

Tài liệu này liệt kê các yêu cầu chức năng (FR) và phi chức năng (NFR) mà phía server của FinMate phải đáp ứng, viết lại từ hệ thống đang chạy (Phase 1–18) và từ `FinMate_Core_User_Flows.docx`. Mỗi yêu cầu có mã riêng, mức ưu tiên và tiêu chí kiểm chứng được.

**Phạm vi.** Hai dịch vụ: Backend ASP.NET Core 9 (REST `/api/v1`, PostgreSQL, Redis, Hangfire) và AI Service FastAPI Python 3.11 (CSDL `finmate_ai` riêng). Ứng dụng Android (`android/`) nằm ngoài phạm vi — chỉ được nhắc tới ở vai trò client.

**Quy ước mã.** `FR-<NHÓM>-<số>` cho chức năng, `NFR-<NHÓM>-<số>` cho phi chức năng. Nhóm: AUTH, ACC, CAT, TXN, BUD, GOAL, RPT, NOTI, GAME, AI, ADM; SEC, DATA, REL, PERF, OPS, MNT.

**Mức ưu tiên** (MoSCoW):

| Mức | Ý nghĩa |
| --- | --- |
| Must | Thiếu thì sản phẩm không phát hành được hoặc gây mất tiền / lộ dữ liệu |
| Should | Quan trọng, có cách vòng tạm thời |
| Could | Cải thiện trải nghiệm, có thể lùi sang bản sau |
| Won't (MVP) | Đã quyết định không làm trong MVP |

## 2. Tác nhân và kênh đầu vào

Hệ thống có hai tác nhân con người (User, Admin), một tác nhân nội bộ (AI Service) và bốn dịch vụ bên ngoài.

| Tác nhân | Loại | Tương tác với hệ thống |
| --- | --- | --- |
| User | Người dùng cuối qua app Android | Quản lý ví, giao dịch, ngân sách, mục tiêu, báo cáo, nhiệm vụ |
| Admin | Nhân sự vận hành, vai trò `Admin` trong JWT | Quản trị người dùng, cấu hình provider, danh mục hệ thống, nhiệm vụ, audit, AI stats |
| AI Service | Dịch vụ nội bộ, gọi bằng `INTERNAL_API_KEY` | Phân tích thông báo, OCR hoá đơn, nhận phản hồi, báo thống kê mô hình |
| Google | OAuth 2.0 / OpenID Connect | Đăng nhập bằng ID token (native) hoặc qua trình duyệt trong app |
| Firebase Cloud Messaging | Push | Gửi thông báo tới thiết bị đã đăng ký |
| Brevo | Email giao dịch | Gửi mã OTP xác minh email và đặt lại mật khẩu |
| Hangfire scheduler | Tác nhân thời gian | Chạy 8 job định kỳ (cảnh báo, streak, tổng hợp, dọn dữ liệu…) |

**Bốn kênh nhập giao dịch** — giá trị ghi vào `transactions.source`:

| Kênh | Core Flow | Đường vào server | Ai tạo giá trị tiền |
| --- | --- | --- | --- |
| Thông báo ngân hàng / ví | Flow 1 | `POST /notifications/analyze` | Regex theo provider (AI Service) |
| Gõ ngôn ngữ tự nhiên | Flow 2 | `POST /transactions/parse` | Parser số tiền ("75k", "1.5tr") |
| Giọng nói | Flow 2 | `POST /transactions/parse` (client đã speech-to-text) | Parser số bằng chữ tiếng Việt có dấu |
| Ảnh hoá đơn | Flow 2 | OCR Tesseract qua AI Service | OCR + trích xuất, luôn cần người dùng rà soát |

Ngoài ra còn nhập tay thông thường qua `POST /transactions` và chuyển khoản nội bộ qua `POST /transactions/transfer`.

## 3. FR — Xác thực và tài khoản người dùng

Người dùng đăng ký bằng email/mật khẩu hoặc Google, xác minh email bằng OTP, và có thể tự khôi phục mật khẩu mà không cần đăng nhập.

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-AUTH-01 | Đăng ký bằng email, mật khẩu, tên hiển thị | Must | Email đúng định dạng, ≤ 320 ký tự, chưa tồn tại; mật khẩu ≥ 8 ký tự; tên ≤ 200 ký tự. Gửi OTP xác minh sau khi tạo |
| FR-AUTH-02 | Xác minh email bằng OTP 6 chữ số (`verify-email`, `resend-verification`) | Must | Mã hết hạn sau 10 phút, dùng một lần, bị huỷ sau 5 lần sai; gửi lại cách nhau ≥ 60 giây. Khi `REQUIRE_EMAIL_VERIFICATION` bật, chưa xác minh thì không đăng nhập được |
| FR-AUTH-03 | Đăng nhập email/mật khẩu | Must | Trả access token JWT (15 phút) + refresh token opaque (30 ngày). Tài khoản bị khoá hoặc đã yêu cầu xoá thì từ chối |
| FR-AUTH-04 | Đăng nhập Google native (`POST /auth/google`, ID token) | Must | Xác minh chữ ký và `aud` = `GOOGLE_CLIENT_ID`; tạo user mới nếu email chưa có, email Google coi như đã xác minh |
| FR-AUTH-05 | Đăng nhập Google qua trình duyệt (`/auth/google/start` → `callback` → `exchange`) | Should | `state` dùng một lần, sống 10 phút; redirect chỉ mang **handoff code** sống 2 phút, đổi được đúng một lần lấy token. Thiếu `GOOGLE_CLIENT_SECRET` → ba endpoint trả 422, route native vẫn chạy |
| FR-AUTH-06 | Làm mới token có xoay vòng (`/auth/refresh`) | Must | Mỗi lần làm mới huỷ token cũ, cấp cặp mới; token đã dùng bị dùng lại → huỷ **tất cả** refresh token của user. Vai trò được đọc lại từ DB |
| FR-AUTH-07 | Đăng xuất một thiết bị / tất cả thiết bị (`logout`, `logout-all`) | Must | Refresh token tương ứng bị thu hồi, dùng lại trả 401 |
| FR-AUTH-08 | Quên mật khẩu (`forgot-password`) | Must | Phản hồi giống hệt từng byte cho email có thật, email không tồn tại và trường hợp đang cooldown |
| FR-AUTH-09 | Đặt lại mật khẩu bằng OTP (`reset-password`) | Must | OTP theo mục đích: mã xác minh email không dùng được để reset. Áp quy tắc mật khẩu như đăng ký; thành công thì thu hồi mọi refresh token |
| FR-AUTH-10 | Đổi mật khẩu khi đã đăng nhập (`change-password`) | Must | Phải đúng mật khẩu cũ; mật khẩu mới ≥ 8 ký tự |
| FR-AUTH-11 | Xem và sửa hồ sơ (`GET/PATCH /users/me`) | Must | Sửa được tên hiển thị và thu nhập kỳ vọng hàng tháng (`monthly_income_cents`); cache hồ sơ bị vô hiệu ngay khi sửa |
| FR-AUTH-12 | Cài đặt thông báo (`PATCH /users/me/notification-prefs`) | Must | Bật/tắt push và từng loại cảnh báo; có hiệu lực với mọi đường gửi push (xem FR-NOTI) |
| FR-AUTH-13 | Yêu cầu xoá tài khoản (`DELETE /auth/account`) | Must | User bị xoá mềm ngay, không đăng nhập được nữa; xoá cứng sau 30 ngày bởi `DataDeletionJob` (03:00). Chỉ Admin huỷ được yêu cầu (FR-ADM-07) |
| FR-AUTH-14 | Đăng ký / gỡ thiết bị nhận push (`POST/DELETE /devices`) | Must | Token FCM là duy nhất trên toàn hệ thống: đăng ký dưới tài khoản khác thì **chuyển** chủ, không nhân bản |
| FR-AUTH-15 | Đăng ký bằng SĐT + OTP SMS | Won't (MVP) | Đã quyết định ngày 2026-09-11; email OTP thay thế |

## 4. FR — Tài khoản tài chính và danh mục

Mỗi giao dịch thuộc một tài khoản tài chính (ngân hàng, ví điện tử, tiền mặt) và — trừ chuyển khoản — một danh mục. Không xóa được thứ gì đang có giao dịch trỏ tới.

### 4.1 Tài khoản tài chính

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-ACC-01 | Liệt kê nhà cung cấp đang hoạt động (`GET /financial-accounts/providers`) | Must | Chỉ trả provider `is_active`; Techcombank, VPBank, ShopeePay bị ẩn cho tới khi `package_name` được kiểm trên thông báo thật |
| FR-ACC-02 | Tạo tài khoản: tên, loại (`Bank`, `EWallet`, `Cash`), số dư ban đầu | Must | Tên 1–100 ký tự; số dư ≥ 0; `Cash` không được có provider, `Bank`/`EWallet` bắt buộc có |
| FR-ACC-03 | Sửa tên tài khoản | Must | Tên 1–100 ký tự |
| FR-ACC-04 | Bật/tắt theo dõi thông báo (`PATCH …/monitoring`) | Must | Thông báo từ ví đang tắt bị ghi trạng thái `Ignored`, không tạo nháp |
| FR-ACC-05 | Xem số dư (`GET …/{id}/balance`) | Must | Số dư là giá trị cộng dồn cập nhật theo mỗi giao dịch, không tính lại từ lịch sử |
| FR-ACC-06 | Xoá tài khoản | Must | Xoá mềm; còn giao dịch → 409 `HasTransactions` |
| FR-ACC-07 | Mọi thao tác chỉ trên tài khoản của chính user | Must | Tài khoản của người khác trả 404, không phải 403 |

### 4.2 Danh mục

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-CAT-01 | Liệt kê danh mục = danh mục hệ thống đang hoạt động + danh mục riêng của user | Must | Danh mục hệ thống được cache 60 phút |
| FR-CAT-02 | Tạo / sửa danh mục riêng | Must | Tên 1–100 ký tự, tên icon ≤ 50 ký tự |
| FR-CAT-03 | Xoá danh mục riêng | Must | Xoá mềm; còn giao dịch → 409 `HasTransactions`. User không sửa/xoá được danh mục hệ thống |
| FR-CAT-04 | Danh mục hệ thống được nhận diện bằng `slug` bất biến | Must | `slug` là khoá AI Service trả về (`category_slug`); đổi slug làm gãy liên kết phân loại nên bị cấm |

## 5. FR — Giao dịch (Core Flow 1 & 2)

Giao dịch sinh ra từ thông báo luôn bắt đầu ở trạng thái `Draft` và chỉ ảnh hưởng số dư, ngân sách, báo cáo khi được xác nhận; giao dịch nhập tay được xác nhận ngay.

```mermaid
stateDiagram-v2
    [*] --> Pending: POST /notifications/analyze
    Pending --> Ignored: ví không theo dõi / không phải giao dịch
    Pending --> Failed: AI Service lỗi
    Failed --> Pending: RetryFailedNotifJob (≤ 3 lần)
    Pending --> Draft: tạo giao dịch nháp
    Draft --> Confirmed: POST /transactions/{id}/confirm
    Draft --> [*]: xoá
    Confirmed --> [*]: xoá (hoàn số dư và ngân sách)
```

Vòng đời một thông báo ngân hàng tới khi thành giao dịch đã xác nhận.

### 5.1 Flow 1 — Từ thông báo ngân hàng

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-TXN-01 | Nhận thông báo từ client (`POST /notifications/analyze`): package, tiêu đề, nội dung, thời điểm nhận | Must | Ghi `notification_logs`, gọi AI Service, trả giao dịch nháp khi là giao dịch tài chính |
| FR-TXN-02 | Chống trùng khi đồng bộ offline | Must | Khóa chống trùng = `contentHash` (đã gồm `ReceivedAt` làm tròn tới phút), **không** có cửa sổ thời gian; gửi lại sau 5 phút vẫn không sinh nháp thứ hai |
| FR-TXN-03 | Thông báo từ ví chưa theo dõi → `Ignored` | Must | `Ignored` chỉ chặn trùng trong 5 phút, để thông báo được đánh giá lại nếu user vừa thêm ví đó |
| FR-TXN-04 | Thử lại khi AI Service lỗi | Should | `RetryFailedNotifJob` mỗi 15 phút, tối đa 3 lần cho mỗi log `Failed` |
| FR-TXN-05 | Chọn nhánh xác nhận theo độ tin cậy | Must | Độ tin cậy = `min(extraction, categorization)`; null bên nào cũng coi là không chắc. ≥ 0,85 → push `confirm_one_tap`; < 0,85 → push `choose_category`. Điểm classifier không tính |
| FR-TXN-06 | Push mang dữ liệu có cấu trúc | Must | Payload `data` gồm `action`, `transactionId`, `amountCents`… để client biết xác nhận giao dịch nào |
| FR-TXN-07 | Xác nhận nháp (`POST /transactions/{id}/confirm`), có thể kèm `categoryId` | Must | Một lệnh gọi: áp danh mục **trước** khi cộng ngân sách; cập nhật số dư, ngân sách, streak, nhiệm vụ. Danh mục bị sửa → gửi `category_correction` về AI |
| FR-TXN-08 | Tab "Chờ duyệt" (`GET /transactions?status=Draft`) | Must | Chỉ trả nháp của user hiện tại |

### 5.2 Flow 2 — Nhập thủ công

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-TXN-10 | Tạo giao dịch tay (`POST /transactions`) | Must | Tiền > 0 (cents VND), loại `Debit`/`Credit`, merchant ≤ 200, mô tả ≤ 500 ký tự. Trả 201 |
| FR-TXN-11 | Client không được tự khai `source = Notification` | Must | Trả 400; `Notification` chỉ backend đặt, vì nó là điều kiện lọc của chỉ số chất lượng AI |
| FR-TXN-12 | Idempotency qua `clientRequestId` tuỳ chọn | Must | Duy nhất theo `(user_id, client_request_id)`; gửi lại trả **200** với giao dịch cũ thay vì 201 |
| FR-TXN-13 | Phân tích câu tự nhiên (`POST /transactions/parse`) | Must | Text 1–500 ký tự; hiểu "75k", "1.5tr"; trả đề xuất, **không** lưu |
| FR-TXN-14 | Nhập bằng giọng nói | Must | Client gửi văn bản đã speech-to-text tới `/parse`; hiểu số bằng chữ có dấu ("bốn mươi lăm ngàn" = 45.000); phân biệt mười/mươi, từ/tư, "công ty"/tỷ |
| FR-TXN-15 | Quét hoá đơn (`POST /transactions/scan-receipt`) | Must | Ảnh ≤ 5 MB, JPEG/PNG/WebP/HEIC; trả một trong `success`, `no_amount`, `unreadable`; độ tin cậy luôn < 0,85 để buộc rà soát; không lưu ảnh |
| FR-TXN-16 | Chuyển khoản nội bộ (`POST /transactions/transfer`) | Must | Hai ví khác nhau cùng thuộc user; không có danh mục; trừ ví nguồn, cộng ví đích; **không** tính vào chi tiêu, thu nhập, ngân sách, báo cáo, dự báo |

### 5.3 Xem, sửa, xoá

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-TXN-20 | Danh sách giao dịch có lọc và phân trang (`GET /transactions`) | Must | Lọc theo trạng thái, ví, danh mục, khoảng ngày |
| FR-TXN-21 | Xem chi tiết (`GET /transactions/{id}`) | Must | Giao dịch của người khác → 404 |
| FR-TXN-22 | Sửa giao dịch (`PATCH`) | Must | Nếu đã xác nhận: hoàn tác tác động cũ lên số dư và ngân sách rồi áp tác động mới, trong cùng một lượt lưu |
| FR-TXN-23 | Xoá giao dịch (`DELETE`) | Must | Xoá mềm; nếu đã xác nhận thì hoàn số dư (cả ví đích với chuyển khoản) và trừ lại ngân sách |

## 6. FR — Ngân sách, mục tiêu tiết kiệm, báo cáo

Cảnh báo ngân sách phải tới ngay khi giao dịch được lưu, không chờ job hàng giờ; mọi con số tổng hợp loại trừ chuyển khoản nội bộ.

### 6.1 Ngân sách

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-BUD-01 | Tạo ngân sách tháng theo danh mục hoặc tổng (danh mục rỗng) | Must | Hạn mức > 0; mỗi (user, danh mục, kỳ `Monthly`) chỉ một ngân sách → trùng trả 409. Khi tạo, `spent_cents` được backfill từ giao dịch đã xác nhận trong tháng |
| FR-BUD-02 | Xem, sửa hạn mức, xoá ngân sách | Must | Chỉ ngân sách của user; cache tóm tắt ngân sách (5 phút) bị vô hiệu khi thay đổi |
| FR-BUD-03 | Cảnh báo ngưỡng 70% / 90% / 100% tức thì | Must | Đánh giá trong cùng thao tác tạo/xác nhận/sửa giao dịch; push được gửi **sau** khi lưu thành công, không trước |
| FR-BUD-04 | Mỗi ngưỡng báo đúng một lần mỗi kỳ | Must | Vượt thẳng từ 60% lên 105% chỉ báo mốc cao nhất và đóng cả ba cờ. User tắt cảnh báo thì **không** đóng cờ |
| FR-BUD-05 | Lưới vét `BudgetAlertJob` mỗi giờ | Should | Bắt các trường hợp vượt ngưỡng không do giao dịch: hạ hạn mức, backfill lúc tạo |

### 6.2 Mục tiêu tiết kiệm

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-GOAL-01 | Tạo / sửa mục tiêu: tên, số tiền đích, hạn tuỳ chọn | Must | Tên ≤ 200 ký tự; đích > 0; hạn (nếu có) ở tương lai |
| FR-GOAL-02 | Đóng góp vào mục tiêu (`/contribute`) | Must | Số tiền > 0, ghi chú ≤ 500 ký tự; chỉ mục tiêu `Active` nhận đóng góp |
| FR-GOAL-03 | Tự hoàn thành | Must | `saved_cents ≥ target_cents` → `Completed`, ghi `completed_at`, thưởng gamification và push |
| FR-GOAL-04 | Huỷ mục tiêu (`/cancel`) | Must | Chỉ huỷ được mục tiêu `Active` |
| FR-GOAL-05 | Xem tiến độ (`/progress`) | Must | Trả đã góp, còn thiếu, phần trăm và lịch sử đóng góp |

### 6.3 Báo cáo và phân tích

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-RPT-01 | Tóm tắt tháng (`/reports/monthly-summary`) | Must | Tổng thu, tổng chi, so với tháng trước, thu nhập kỳ vọng; tính theo giờ Việt Nam (UTC+7) |
| FR-RPT-02 | Cơ cấu chi theo danh mục (`/category-breakdown`) | Must | Chỉ giao dịch `Confirmed`, loại `Debit` |
| FR-RPT-03 | Dòng thời gian (`/timeline`) | Must | Đọc từ `daily_summaries` tính sẵn bởi `DailySummaryJob` (00:05) |
| FR-RPT-04 | Dự báo chi tiêu cuối tháng (`/forecast`) | Should | Thống kê run-rate tháng hiện tại, rơi về lịch sử khi chưa đủ ngày; tính sẵn bởi `ForecastJob` (06:00) |
| FR-RPT-05 | Insight chi tiêu và đánh dấu đã đọc | Should | `InsightGeneratorJob` (02:00) phát hiện khoản định kỳ, khoản bất thường, so sánh cùng kỳ tháng trước |

## 7. FR — Thông báo đẩy và gamification

Push đi qua một cửa duy nhất tôn trọng cài đặt của user; gamification thưởng EXP cho việc ghi chép đều đặn và thu hồi khi giao dịch bị xoá.

### 7.1 Thông báo đẩy (FCM)

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-NOTI-01 | Gửi push tới mọi thiết bị đã đăng ký của user | Must | Các sự kiện: nháp mới từ thông báo (Flow 1), cảnh báo ngân sách, hoàn thành mục tiêu, lên level / mở khoá vật phẩm |
| FR-NOTI-02 | Tôn trọng `NotificationPrefs.PushEnabled` ở một điểm duy nhất | Must | User tắt push thì không có đường gửi nào lọt qua, kể cả đường thêm sau này |
| FR-NOTI-03 | Dọn token chết | Must | Chỉ xoá token khi FCM trả `Unregistered` hoặc `SenderIdMismatch`; lỗi mạng tạm thời không xoá |
| FR-NOTI-04 | Push không bao giờ làm hỏng thao tác gốc | Must | Lỗi gửi không ném ra ngoài; giao dịch đã lưu vẫn trả thành công |
| FR-NOTI-05 | Chế độ dự phòng khi thiếu thông tin FCM | Should | Không có `FCM_CREDENTIALS_PATH`/`FCM_CREDENTIALS_JSON` → ghi log thay vì gửi; log khởi động nêu chế độ đang dùng |
| FR-NOTI-06 | Email OTP qua Brevo | Must | Thiếu `BREVO_API_KEY` → ghi mã ra log (chỉ dành cho dev/test) |

### 7.2 Gamification

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-GAME-01 | Cộng EXP khi ghi nhận hoạt động (xác nhận / tạo giao dịch, hoàn thành nhiệm vụ) | Must | Level tính theo đường cong bậc hai: tổng EXP cho level N = 50·N·(N−1); trần level 999 |
| FR-GAME-02 | Thu hồi EXP khi xoá giao dịch đã xác nhận | Must | EXP không âm; level tính lại |
| FR-GAME-03 | Chuỗi ngày (streak) | Must | Hoạt động ngày liền sau → +1; `StreakCheckJob` 23:55 đặt lại nếu hôm đó không có giao dịch. Ngày theo giờ Việt Nam |
| FR-GAME-04 | Nhiệm vụ `Daily`, `Weekly`, `OneTime` | Must | `MissionResetJob` 00:01 làm mới; tuần tính từ thứ Hai; mỗi nhiệm vụ thưởng một lần mỗi kỳ |
| FR-GAME-05 | Xem hồ sơ, nhiệm vụ hiện tại, lịch sử nhiệm vụ | Must | `GET /gamification/profile`, `/missions`, `/missions/history` |
| FR-GAME-06 | Mascot: mở khoá vật phẩm và thay trang phục | Should | Mở khoá theo `Default`, `Level` hoặc `Mission`; `PUT /mascot/outfit` chỉ nhận vật phẩm đã mở khoá |

## 8. FR — AI Service

Số tiền do regex theo từng provider trích ra, không bao giờ do mô hình đoán; mô hình ML chỉ phân loại và gán danh mục, và luôn có luật dự phòng.

```mermaid
flowchart LR
    A[Classifier<br/>financial?] -->|financial| B[Extractor<br/>regex theo provider]
    A -->|khác| X[early return]
    B --> C[Categorizer<br/>category_slug]
    C --> D[Duplicate Detector]
    D --> E[pipeline_requests]
```

Pipeline của `POST /api/v1/analyze`; mỗi bước trả độ tin cậy riêng.

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-AI-01 | Phân loại thông báo: `financial` / `non_financial` / `uncertain` | Must | Không phải `financial` → dừng pipeline, backend không tạo nháp |
| FR-AI-02 | Trích xuất số tiền, loại (debit/credit), merchant, mô tả, thời điểm, số dư sau GD | Must | Dùng regex trong bảng `provider_patterns`, nạp lại mỗi 60 giây; không khớp → `extraction_failed` |
| FR-AI-03 | Gán danh mục theo `category_slug` kèm độ tin cậy | Must | Khớp từ điển → 0,90; chưa promote mô hình nào thì chạy bằng luật |
| FR-AI-04 | Phát hiện giao dịch có thể trùng | Should | So với `pipeline_requests` gần đây cùng user hash, số tiền, provider; trả `is_potential_duplicate` |
| FR-AI-05 | OCR hoá đơn (`POST /api/v1/ocr`) | Must | Tesseract `--psm 6`, tiếng Việt; xử lý trong bộ nhớ; trả `success` / `no_amount` / `unreadable` |
| FR-AI-06 | Nhận phản hồi (`POST /api/v1/feedback`) | Must | Lưu `category_correction` vào `user_feedback` cho lần huấn luyện sau |
| FR-AI-07 | Thống kê mô hình (`GET /api/v1/stats`) | Must | Phiên bản, accuracy, macro-F1 mỗi stage; số mẫu theo split; job huấn luyện cuối; feedback đang chờ. Không chứa dữ liệu theo user |
| FR-AI-08 | Vòng đời mô hình: `seed_dataset` → `train` → `evaluate` → `promote` → `feedback_batch` | Must | `promote` từ chối mô hình chưa đánh giá trên tập test hoặc accuracy < 0,70; mô hình mới được nạp nóng trong 60 giây |
| FR-AI-09 | Dọn mẫu thô chưa gán nhãn | Should | `purge_raw_samples` qua cron ngoài, giữ 90 ngày; mẫu đã gán nhãn được giữ |
| FR-AI-10 | A/B testing mô hình (bảng `ab_*`) | Won't (MVP) | Hoãn; tạo migration khi tính năng ship |

## 9. FR — Quản trị (Admin)

Mọi endpoint `/api/v1/admin/*` chỉ dành cho vai trò `Admin`, không xoá cứng thứ gì, và mọi thao tác ghi đều vào `audit_logs`.

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận |
| --- | --- | --- | --- |
| FR-ADM-01 | Danh sách và chi tiết người dùng, lọc theo trạng thái khoá, vai trò | Must | Không trả số tiền hay mô tả giao dịch của user cụ thể |
| FR-ADM-02 | Khoá / mở khoá tài khoản (`PATCH …/lock`) | Must | Không tự khoá mình (`CannotLockSelf`); khoá thì thu hồi mọi refresh token |
| FR-ADM-03 | Đổi vai trò (`PATCH …/role`) | Must | Không đổi vai trò của chính mình, kể cả nâng; không hạ admin **đang hoạt động** cuối cùng (admin bị khoá không tính); không nâng tài khoản đang chờ xoá. Hạ cấp → thu hồi refresh token |
| FR-ADM-04 | Quản lý cấu hình provider (tạo, sửa, bật/tắt) | Must | `provider_key` bất biến sau khi tạo; tắt bằng `is_active`; vô hiệu cache `provider_configs:active` |
| FR-ADM-05 | Quản lý danh mục hệ thống (tạo, sửa, bật/tắt) | Must | `slug` bất biến; vô hiệu cache `categories:system` |
| FR-ADM-06 | Quản lý nhiệm vụ (tạo, sửa, bật/tắt) | Must | `code` bất biến |
| FR-ADM-07 | Giám sát hàng đợi xoá dữ liệu và huỷ yêu cầu (`POST …/{id}/cancel`) | Must | Huỷ phải xóa `user.DeletedAt` — đây là đường khôi phục duy nhất. Không có endpoint xoá ngay |
| FR-ADM-08 | Xem audit log (chỉ đọc) | Must | Ghi đủ 25 sự kiện: đăng ký, đăng nhập thành công/thất bại, đổi/đặt lại mật khẩu, tái sử dụng token, Google, mọi thao tác ghi của admin |
| FR-ADM-09 | Thống kê chất lượng AI (`/admin/ai-stats`) | Should | Gộp stats của AI Service với tỉ lệ user sửa danh mục (chỉ giao dịch `source = Notification`, cửa sổ mặc định 30 ngày). AI Service chết vẫn trả 200 với phần của backend |
| FR-ADM-10 | Tài khoản admin hạt giống | Must | `AdminUserSeeder` sửa lại vai trò, trạng thái khoá, xoá mềm mỗi lần khởi động — **không** đổi mật khẩu |

## 10. NFR — Bảo mật và quyền riêng tư

Dữ liệu tài chính của một người không bao giờ được lọt sang người khác, vào log, hay vào CSDL AI dưới dạng nhận diện được.

### 10.1 Xác thực và phân quyền

| Mã | Yêu cầu | Cách kiểm chứng |
| --- | --- | --- |
| NFR-SEC-01 | Mật khẩu băm bằng BCrypt work factor 12 | Unit test hasher; không có cột mật khẩu dạng rõ |
| NFR-SEC-02 | JWT HMAC, khoá ≥ 64 ký tự, `ClockSkew = 0`, access token sống 15 phút | App từ chối khởi động khi khoá ngắn hơn |
| NFR-SEC-03 | Refresh token lưu dạng SHA-256, không lưu bản gốc | Kiểm tra bảng `refresh_tokens` |
| NFR-SEC-04 | Mọi endpoint yêu cầu `[Authorize]` trừ `/auth/*` và health check | Test tích hợp gọi không token → 401 |
| NFR-SEC-05 | Admin controller kế thừa `AdminControllerBase` (policy `AdminOnly`), không gắn lại attribute từng controller | User thường gọi `/admin/*` → 403 |
| NFR-SEC-06 | Cô lập theo chủ sở hữu: mọi truy vấn repository lọc theo `userId` lấy từ JWT, không từ body/path | Test cross-user: tài nguyên của người khác → 404 |
| NFR-SEC-07 | AI Service không mở ra Internet; xác thực bằng `INTERNAL_API_KEY` | Gọi thiếu/sai key → 401 |

### 10.2 Chống lạm dụng

| Mã | Yêu cầu | Cách kiểm chứng |
| --- | --- | --- |
| NFR-SEC-10 | Giới hạn chung 120 request/phút, phân vùng theo user id (IP khi ẩn danh) | Vượt ngưỡng → 429 |
| NFR-SEC-11 | Endpoint xác thực: 10 request/phút theo IP, **cộng dồn** với giới hạn chung | Đo `/auth/login` → request thứ 11 trả 429 |
| NFR-SEC-12 | `TRUSTED_PROXIES` rỗng thì tắt forwarded headers; "tin tất cả" phải ghi rõ `0.0.0.0/0` và `::/0` | Giả `X-Forwarded-For` không thoát được rate limit |
| NFR-SEC-13 | Endpoint ẩn danh không để lộ email nào tồn tại | So sánh phản hồi `forgot-password` theo byte |
| NFR-SEC-14 | OAuth trình duyệt chống CSRF bằng `state` dùng một lần; URL redirect không chứa token | Callback với `state` lạ hoặc đã dùng → từ chối |
| NFR-SEC-15 | Phát hiện tái sử dụng refresh token → thu hồi toàn bộ phiên, ghi `Auth.TokenReuse.Detected` | Test dùng lại token cũ |

### 10.3 Quyền riêng tư và ghi log

| Mã | Yêu cầu | Cách kiểm chứng |
| --- | --- | --- |
| NFR-SEC-20 | Không log mật khẩu, token (JWT, refresh, FCM, OTP trừ chế độ dev), `amount_cents`, `notification_body`, payload `data` của push | Review log khi chạy test tích hợp |
| NFR-SEC-21 | CSDL AI chỉ lưu `SHA-256(user_id)`, không FK sang CSDL backend | Kiểm tra schema `finmate_ai` |
| NFR-SEC-22 | Nội dung thông báo vào tập huấn luyện đã che số tài khoản, thẻ, SĐT, email | Unit test `anonymizer` |
| NFR-SEC-23 | `notification_body` gốc bị xoá sau 90 ngày (`DataCleanupJob`) | Test job với bản ghi 91 ngày tuổi |
| NFR-SEC-24 | Ảnh hoá đơn chỉ xử lý trong bộ nhớ, không ghi đĩa, blob hay DB | Review code OCR |
| NFR-SEC-25 | Quyền được xoá: xoá cứng toàn bộ dữ liệu user sau 30 ngày kể từ yêu cầu | Test `DataDeletionJob` |
| NFR-SEC-26 | Production không trả stack trace; lỗi theo dạng ProblemDetails thống nhất | `ExceptionHandlingMiddleware` + test |
| NFR-SEC-27 | Ngoài Development, app từ chối khởi động nếu bí mật còn chứa `change-me` (`JWT_SECRET`, `AI_SERVICE_API_KEY`, `ADMIN_SEED_PASSWORD`, `HANGFIRE_DASHBOARD_PASS`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`) | `StartupSecretGuard` test |

## 11. NFR — Toàn vẹn dữ liệu, độ tin cậy, hiệu năng

Tiền phải đúng tới từng đồng và không bao giờ bị ghi hai lần; các chỉ tiêu độ trễ dưới đây là **đề xuất**, repo chưa đo chúng.

### 11.1 Toàn vẹn dữ liệu

| Mã | Yêu cầu | Cách kiểm chứng |
| --- | --- | --- |
| NFR-DATA-01 | Tiền là `BIGINT` đơn vị đồng VND; cấm `decimal`/`double`/`float` | Review schema và entity |
| NFR-DATA-02 | ID là UUID; không lộ khoá tự tăng ra API | Review schema |
| NFR-DATA-03 | Thời gian là `TIMESTAMPTZ` / `DateTimeOffset`; ranh giới ngày, tuần, tháng tính theo UTC+7 | Test giao dịch lúc 23:30 và 00:30 giờ VN |
| NFR-DATA-04 | Enum là `TEXT` + `CHECK`, không dùng PostgreSQL ENUM | Review migration |
| NFR-DATA-05 | Xoá mềm bằng `deleted_at`, trừ hàng đợi xoá dữ liệu cá nhân | Review repository |
| NFR-DATA-06 | Số dư, `spent_cents`, tóm tắt ngày là giá trị tính sẵn, cập nhật cùng giao dịch, không tính lại khi đọc | Test: sau N giao dịch, số dư = đầu kỳ ± tổng |
| NFR-DATA-07 | Chuyển khoản bị loại khỏi mọi phép cộng dồn thu/chi | Test báo cáo và ngân sách có transfer |
| NFR-DATA-08 | Idempotency ngưỡng CSDL: `UNIQUE (user_id, client_request_id) WHERE client_request_id IS NOT NULL`; thông báo chống trùng theo `contentHash` | Test gửi lại song song |
| NFR-DATA-09 | Không có tác dụng phụ ra ngoài trước khi lưu: push và feedback AI chỉ gửi sau `SaveChangesAsync` | Review handler |

### 11.2 Độ tin cậy và khả dụng

| Mã | Yêu cầu | Cách kiểm chứng |
| --- | --- | --- |
| NFR-REL-01 | AI Service chết không làm sập backend: thông báo ghi `Failed` và thử lại; nhập tay vẫn chạy | Tắt container AI rồi gọi API |
| NFR-REL-02 | AI Service luôn có đường dự phòng bằng luật khi chưa có mô hình được promote | Xóa `model_registry` active rồi gọi `/analyze` |
| NFR-REL-03 | FCM, Brevo lỗi không làm hỏng thao tác gốc | Test với client giả ném lỗi |
| NFR-REL-04 | Server chịu được đồng bộ hàng loạt từ hàng đợi offline của client mà không sinh bản ghi trùng | Replay 50 thông báo hai lần |
| NFR-REL-05 | Job nền bền qua khởi động lại (Hangfire lưu trên PostgreSQL) | Restart container giữa lúc job chạy |
| NFR-REL-06 | Hai health check tách biệt: `/health/live` (tiến trình sống) và `/health/ready` (PostgreSQL, Redis sẵn sàng) | Tắt Postgres → ready lỗi, live vẫn OK |
| NFR-REL-07 | Khả dụng mục tiêu 99,5%/tháng cho API công khai (đề xuất) | Uptime monitor |

### 11.3 Hiệu năng (đề xuất, chưa đo)

| Mã | Yêu cầu | Cách kiểm chứng |
| --- | --- | --- |
| NFR-PERF-01 | API đọc/ghi thông thường: p95 ≤ 300 ms | Load test k6, 100 user đồng thời |
| NFR-PERF-02 | `POST /notifications/analyze` gồm gọi AI: p95 ≤ 1 s | Đo `processing_ms` + độ trễ backend |
| NFR-PERF-03 | OCR hoá đơn: p95 ≤ 5 s với ảnh 5 MB | Benchmark trong container |
| NFR-PERF-04 | Cảnh báo ngân sách tới thiết bị ≤ 10 s sau khi giao dịch lưu (không chờ job) | Đo từ log lưu tới FCM ack |
| NFR-PERF-05 | Cache Redis cho dữ liệu đọc nhiều: hồ sơ 15 phút, tóm tắt ngân sách 5 phút, gamification 10 phút, nhiệm vụ 30 phút, danh mục hệ thống và provider 60 phút | Review `CacheKeys` |
| NFR-PERF-06 | Danh sách luôn phân trang; báo cáo đọc bảng tổng hợp, không quét toàn bộ giao dịch | Review query |
| NFR-PERF-07 | Backend gọi AI Service có timeout tường minh (đề xuất 5 s) | Hiện dùng mặc định 100 s của `HttpClient` — xem mục 13 |

## 12. NFR — Triển khai, vận hành, bảo trì

Toàn bộ hệ thống dựng được bằng một lệnh `docker compose up`, cấu hình hoàn toàn qua biến môi trường, và mỗi thay đổi được kiểm bằng test tự động.

### 12.1 Triển khai và vận hành

| Mã | Yêu cầu | Cách kiểm chứng |
| --- | --- | --- |
| NFR-OPS-01 | Image backend multi-stage (`publish -c Release` vào `aspnet`), chạy user không phải root (`$APP_UID`), ≤ 400 MB | `docker image ls` (hiện 359 MB) |
| NFR-OPS-02 | Stack gồm PostgreSQL 16 × 2 (backend, AI), Redis 7, backend, AI Service; mỗi service có healthcheck | `docker compose ps` |
| NFR-OPS-03 | Mọi cấu hình và bí mật qua biến môi trường, không hardcode | Grep repo tìm khoá |
| NFR-OPS-04 | Log có cấu trúc (Serilog) ra stdout; chỉ ghi file ở Development | Chạy container, xem `docker logs` |
| NFR-OPS-05 | Log khởi động nêu rõ chế độ thật/dự phòng của push và email | Xem log lúc boot |
| NFR-OPS-06 | Hangfire dashboard được bảo vệ bằng mật khẩu riêng (`HANGFIRE_DASHBOARD_PASS`) | Truy cập không mật khẩu → 401 |
| NFR-OPS-07 | Migration: EF Core cho backend, Alembic cho AI; không sửa migration đã phát hành | Review PR |
| NFR-OPS-08 | OCR chỉ chạy trong container có `tesseract-ocr` và `tesseract-ocr-vie` | Build image AI |

### 12.2 Bảo trì và kiểm thử

| Mã | Yêu cầu | Cách kiểm chứng |
| --- | --- | --- |
| NFR-MNT-01 | Kiến trúc phân tầng API → Application → Domain ← Infrastructure; Controller không chứa logic nghiệp vụ | Review theo `ARCHITECTURE.md` §2.3 |
| NFR-MNT-02 | CQRS tự viết (Command/Query + Handler), không dùng MediatR | Review |
| NFR-MNT-03 | Backend và AI Service không chia sẻ CSDL; giao tiếp duy nhất qua contract HTTP đã ghi trong `ARCHITECTURE.md` §3.3 | Review |
| NFR-MNT-04 | Unit test + integration test với Testcontainers (PostgreSQL thật) cho backend | `dotnet test` xanh |
| NFR-MNT-05 | AI Service: pytest; test tích hợp bỏ qua khi không có Postgres; OCR dùng engine giả | `pytest` xanh |
| NFR-MNT-06 | Python đạt `black`, `ruff`, `isort` | Chạy ba công cụ |
| NFR-MNT-07 | Những định danh ngoại hệ thống dựa vào (`provider_key`, `slug`, mission `code`) là bất biến | Test API sửa bị từ chối |
| NFR-MNT-08 | Ngưỡng nghiệp vụ (0,85 xác nhận một chạm; 70/90/100% ngân sách; 0,70 promote) nằm trong một hằng số duy nhất mỗi loại | Grep |
| NFR-MNT-09 | API có phiên bản trong URL (`/api/v1`) | Review route |

### 12.3 Khả năng sử dụng phía server

| Mã | Yêu cầu | Cách kiểm chứng |
| --- | --- | --- |
| NFR-USE-01 | Thông điệp lỗi validation bằng tiếng Việt, kèm mã lỗi ổn định (vd. `HasTransactions`) cho client rẽ nhánh | Test API |
| NFR-USE-02 | Hiểu cách viết tiền phổ biến của người Việt: "75k", "1.5tr", "2tr5", số bằng chữ có dấu | Unit test parser |
| NFR-USE-03 | Kết quả OCR phân biệt ba trường hợp thất bại để client đưa lời nhắc phù hợp | Test `/ocr` |

## 13. Ràng buộc, giả định, vấn đề mở

Flow 3 và Flow 4 của docx chưa từng được đối chiếu, nên các yêu cầu ngân sách, mục tiêu, báo cáo, gamification ở trên mô tả hệ thống hiện có chứ chưa chứng minh là đủ so với đặc tả nghiệp vụ.

**Ràng buộc**

- Backend: ASP.NET Core 9, EF Core, PostgreSQL 16, Redis 7, Hangfire. AI Service: Python 3.11, FastAPI, SQLAlchemy, scikit-learn, Tesseract.
- Đơn vị tiền duy nhất là VND; múi giờ nghiệp vụ duy nhất là UTC+7.
- Ứng dụng Android do nhóm khác phát triển; server không kiểm soát việc đọc thông báo, speech-to-text hay hàng đợi Room.

**Giả định**

- Thông báo ngân hàng là mẫu cố định theo từng provider, nên regex đủ chính xác cho số tiền.
- Client gửi `ReceivedAt` đúng thời điểm nhận thông báo, vì nó nằm trong khoá chống trùng.
- Mỗi user có một ngân sách tháng trên mỗi danh mục; không có kỳ tuần hay năm.

**Vấn đề mở**

- [ ] Đối chiếu Core Flow 3 và Core Flow 4 trong `FinMate_Core_User_Flows.docx` với mục 6–7, bổ sung yêu cầu còn thiếu.
- [ ] Ngưỡng xác nhận một chạm: docx tự mâu thuẫn (85% ở luồng từng bước, 80% ở bảng edge case); hệ thống đang dùng 85%.
- [ ] Chốt chỉ tiêu hiệu năng và khả dụng (NFR-PERF, NFR-REL-07) và đo thật bằng load test.
- [ ] Đặt timeout tường minh cho client gọi AI Service (NFR-PERF-07).
- [ ] Kiểm `package_name` của Techcombank, VPBank, ShopeePay trên thông báo thật rồi bật lên.

**Truy vết yêu cầu → nguồn**

| Nhóm | Nguồn nghiệp vụ | Phase triển khai (`TASKS.md`) |
| --- | --- | --- |
| FR-AUTH | Docx bước 1.1 (đã thay SMS bằng email OTP) | 1, 12, 13, 16, 17, 18 |
| FR-ACC | Docx Flow 1, Flow 2 | 2, 11 |
| FR-CAT | Docx Flow 1, Flow 2 | 3 |
| FR-TXN | Docx Flow 1, Flow 2 (a–c) | 4, 10, 11, 14, 15 |
| FR-BUD, FR-GOAL | Docx Flow 2 mục 2a; Flow 3/4 chưa đối chiếu | 5, 11 |
| FR-RPT | Flow 3/4 chưa đối chiếu | 6 |
| FR-GAME | Docx Flow 1 (Mascot); Flow 3/4 chưa đối chiếu | 7 |
| FR-NOTI | Docx Flow 1 | 12, 15 |
| FR-AI | `ARCHITECTURE.md` §3 | 9, 10 |
| FR-ADM | Vận hành (docx không có luồng admin) | 8, 13 |
