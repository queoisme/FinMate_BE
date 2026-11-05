# API vận hành: Staff, OpsManager, Admin

Tài liệu cho người làm web vận hành. Toàn bộ API nằm dưới `/api/v1/ops/*`, trả về dạng `ApiResponse` như phần còn lại của hệ thống.

## Vai trò

| Vai trò (`UserRole`) | Giá trị trong DB | Việc chính |
|---|---|---|
| `Staff` | `staff` | Xử lý ticket, nói chuyện với khách |
| `OpsManager` | `opsmanager` | Điều phối ticket, duyệt việc nhạy cảm, theo dõi SLA |
| `Admin` | `admin` | Làm được việc của hai vai trên, cộng `/admin/*` |

`OpsManager` khác vai `manager` trong một nhóm tài chính. Chỉ Admin cấp được vai trò, qua `PATCH /api/v1/admin/users/{id}/role`, và không tự đổi được vai trò của mình.

## Quyền theo vai trò

| Quyền (`capabilities`) | Staff | OpsManager | Admin |
|---|---|---|---|
| `HandleTickets` | ✅ | ✅ | ✅ |
| `AccessCustomerData` (vẫn cần khách đồng ý theo từng ca) | ✅ | ✅ | ✅ |
| `CreateDraftTransactions` | ✅ | ✅ | ✅ |
| `EmergencyLock` | ✅ | ✅ | ✅ |
| `ProposeApprovals` | ✅ | ✅ | ✅ |
| `ManageTickets` | ❌ | ✅ | ✅ |
| `DecideApprovals` (không duyệt đề xuất của mình) | ❌ | ✅ | ✅ |
| `ViewDashboard` | ❌ | ✅ | ✅ |
| `ManageCannedReplies` | ❌ | ✅ | ✅ |
| `ViewOpsAudit` | ❌ | ✅ | ✅ |
| `ViewAiStats` | ❌ | ✅ | ✅ |
| `ViewMonitoring` | ❌ | ✅ | ✅ |
| `ManageIncidents` | ❌ | ✅ | ✅ |
| `ManageSystem` | ❌ | ❌ | ✅ |

## Cách kiểm tra quyền

- Người ngoài đội vận hành nhận 403, chưa đăng nhập nhận 401.
- Thao tác nhạy cảm đọc lại vai trò từ DB nên người vừa bị hạ quyền hoặc khóa mất quyền ngay, dù token còn hạn.
- Giảm quyền thu hồi refresh token, nên phiên cũ không gia hạn được. Tăng quyền giữ phiên; vai trò mới có từ lần refresh kế tiếp.
- Khi bảo trì, Staff và OpsManager chỉ gọi được `/api/v1/ops/*`; Admin gọi được mọi API.

## Cấu hình

`OPS_WEB_ORIGINS`: các domain của web được gọi API từ trình duyệt, cách nhau bằng dấu phẩy, ví dụ `https://ops.finmate.vn`. Để trống thì không trình duyệt nào gọi được. Không nhận `*`; sai định dạng thì backend không khởi động.

## API

### `GET /ops/me`

Vai trò và quyền hiện tại. Web dựng menu từ đây, không tự đoán từ token.

```json
{ "success": true, "data": {
  "id": "…", "email": "staff@finmate.vn", "displayName": "An",
  "role": "Staff", "capabilities": ["HandleTickets", "AccessCustomerData"] } }
```

## Ticket hỗ trợ

Tính năng nằm sau cờ `features.support`, mặc định tắt. Khi tắt, khách không tạo hay xem được ticket và không đồng ý cấp quyền mới (422 `SUPPORT_DISABLED`), nhưng vẫn xem được yêu cầu xem dữ liệu, từ chối, thu hồi và xem lịch sử, để luôn rút lại được quyền đã cho. API vận hành vẫn xử lý nốt các ca đang mở, trừ gửi yêu cầu xem dữ liệu mới.

### Vòng đời

`New → InProgress → WaitingCustomer → Resolved → Closed`, cộng `Escalated` khi chuyển cấp.

- Nhân viên trả lời khách: sang `WaitingCustomer`, ghi nhận lần phản hồi đầu tiên. Ghi chú nội bộ không đổi gì.
- Ca `Escalated` giữ nguyên trạng thái khi trả lời khách, và chỉ Manager trở lên đổi được, để ca không rời hàng đợi rà soát.
- Khách trả lời khi đang `WaitingCustomer` hoặc `Resolved`: quay về `InProgress`, hoặc `New` nếu chưa ai nhận.
- `Closed` là trạng thái cuối, không trả lời được nữa; khách phải tạo ca mới.
- Ca `Resolved` mà khách không phản hồi sau `support.autoCloseResolvedDays` (mặc định 7 ngày) thì tự đóng.

### Mức ưu tiên và SLA

Khách không tự chọn mức ưu tiên; hệ thống gán theo loại, Manager đổi lại được. Đổi mức thì hạn được tính lại từ lúc tạo ca. Chỉ tính và hiển thị trễ hạn, không gửi cảnh báo trễ SLA.

| Mức | Loại ticket | Phản hồi đầu | Giải quyết |
|---|---|---|---|
| `P1` | `Security` | 15 phút | 4 giờ |
| `P2` | `Account`, `Transaction`, `Receipt`, `Notification`, `Group` | 1 giờ | 1 ngày |
| `P3` | `Question` | 4 giờ | 3 ngày |

Sửa được trong nhóm cấu hình `support` ở `/admin/system-config`.

### API cho khách: `/api/v1/support/tickets`

Chỉ thấy ca của mình; ca của người khác trả 404. Response không có người phụ trách, hạn SLA hay ghi chú nội bộ.

| API | Việc |
|---|---|
| `POST /` | Tạo ca: `category`, `subject` (tối đa 200), `body` (tối đa 4000). Tối đa 10 ca chưa đóng |
| `GET /`, `GET /{id}` | Danh sách, chi tiết kèm tin nhắn |
| `POST /{id}/messages` | Trả lời |
| `POST /{id}/messages/{messageId}/attachments` | Gửi ảnh (multipart `file`) vào tin nhắn của mình |
| `GET /{id}/attachments/{attachmentId}` | Tải ảnh |
| `POST /{id}/close` | Đóng ca |
| `POST /{id}/rating` | Chấm 1 đến 5, khi ca đã `Resolved` hoặc `Closed` |

Khi nhân viên trả lời, khách nhận push với `data = { action: "support_ticket", ticketId }`. Nội dung push không có nội dung trả lời.

### Ảnh đính kèm

Tối đa 5 MB, 3 ảnh mỗi tin nhắn. Định dạng JPEG, PNG, WebP, HEIC, HEIF, nhận dạng theo nội dung file chứ không theo `Content-Type`. Ảnh được mã hóa bằng `SECRETS_MASTER_KEY`; thiếu khóa thì trả 422 `SUPPORT_ATTACHMENTS_UNAVAILABLE`, không lưu bản rõ. Ảnh bị xóa sau khi ca đóng `support.attachmentRetentionDays` ngày (mặc định 30).

### API vận hành: `/api/v1/ops/tickets`

| API | Việc | Ai |
|---|---|---|
| `GET /` | Hàng đợi, hạn gần nhất trước. Lọc `status`, `priority`, `category`, `assignee` (`me`, `none`, id), `overdue`; phân trang `offset`, `limit` (tối đa 100). Mặc định bỏ ca đã đóng | Mọi vai trò nội bộ |
| `GET /{id}` | Chi tiết kèm ghi chú nội bộ | Ca chưa ai nhận, người phụ trách, Manager trở lên |
| `POST /{id}/claim` | Nhận ca. Người đến sau nhận 409 | Mọi vai trò nội bộ |
| `POST /{id}/messages` | Trả lời; `internal: true` là ghi chú nội bộ | Người phụ trách, Manager trở lên |
| `PATCH /{id}/status` | Sang `InProgress`, `WaitingCustomer`, `Resolved`, `Closed` | Người phụ trách, Manager trở lên |
| `POST /{id}/escalate` | Chuyển cấp, bắt buộc có `note` nêu đã thử gì | Người phụ trách, Manager trở lên |
| `PATCH /{id}/assignee` | Phân công | Manager trở lên |
| `PATCH /{id}/priority` | Đổi mức ưu tiên | Manager trở lên |
| `GET /{id}/attachments/{attachmentId}` | Tải ảnh, có ghi audit. Phải nhận ca trước | Người phụ trách, Manager trở lên |

Không ai xử lý được ca do chính mình tạo: ca đó không hiện trong hàng đợi của họ và các API trên trả 403.

## Xem dữ liệu khách theo ca

Không ai trong đội vận hành xem được dữ liệu tài chính của khách nếu thiếu một trong các điều kiện sau, **kể cả Admin**:

1. Đang là người phụ trách ca. Manager và Admin cũng phải nhận ca trước.
2. Chính khách đã đồng ý trong app, cho đúng phần dữ liệu đó.
3. Quyền còn hạn: tối đa 24 giờ, không cấu hình được.
4. Ca chưa đóng và chưa đổi người phụ trách. Đổi người phụ trách là kết thúc quyền, kể cả khi ca được giao lại cho người cũ.
5. Khách còn hoạt động. Khách bị khóa hoặc yêu cầu xóa tài khoản thì mọi quyền đã cấp chấm dứt và không tự có lại, kể cả khi sau đó được mở khóa hoặc hủy yêu cầu xóa.

| Phạm vi (`scopes`) | Cho phép |
|---|---|
| `Accounts` | Xem ví và số dư |
| `Transactions` | Xem giao dịch; không có số dư sau giao dịch, `notificationLogId`, `clientRequestId` |
| `Budgets` | Xem ngân sách tháng hiện tại |
| `Draft` | Tạo giao dịch nháp vào ví khách chọn lúc đồng ý |

### Phía vận hành: `/api/v1/ops/tickets/{ticketId}`

Khách luôn được xác định qua ticket; không API nào nhận user id của khách.

| API | Việc |
|---|---|
| `POST /access-requests` | Xin quyền: `scopes`, `reason` (khách đọc được). Mỗi ca chỉ một yêu cầu đang chờ, thừa thì 409. Khách nhận push |
| `GET /access-requests` | Trạng thái các yêu cầu |
| `GET /customer-data/accounts`, `/transactions`, `/budgets` | Đọc dữ liệu. Thiếu điều kiện thì 403. Mỗi lần đọc đều ghi lại |
| `POST /draft-transactions` | Tạo nháp: `amountCents`, `transactionType` (`Debit`, `Credit`), `transactedAt`, `categoryId?` (chỉ danh mục hệ thống), `merchantName?`, `description?`, `clientRequestId?`. Gửi lại cùng `clientRequestId` thì nhận 200 với giao dịch cũ |

Nháp có `source = Support`. Số dư và ngân sách chỉ đổi khi khách xác nhận qua `POST /transactions/{id}/confirm`; nhân viên gọi API này nhận 404.

### Phía khách: `/api/v1/support`

| API | Việc |
|---|---|
| `GET /tickets/{id}/access-requests` | Các yêu cầu của ca |
| `POST /tickets/{id}/access-requests/{requestId}/approve` | Đồng ý. Có scope `Draft` thì bắt buộc `financialAccountId` là ví của mình |
| `POST /tickets/{id}/access-requests/{requestId}/deny` | Từ chối |
| `POST /tickets/{id}/access-requests/{requestId}/revoke` | Thu hồi bất cứ lúc nào |
| `GET /access-log` | Lịch sử xem dữ liệu của mình: ca, phần dữ liệu, thời điểm |

Khi có yêu cầu mới, khách nhận push với `data = { action: "support_access_request", ticketId, requestId }`. Khi có nháp, khách nhận push như nháp từ thông báo ngân hàng.

### Điều khoản

Việc lưu ảnh trong ticket và cho nhân viên xem dữ liệu khi khách đồng ý phải có trong Điều khoản. Bản 4 chưa phát hành nên nội dung này gộp vào bản 4, cùng với việc ảnh hóa đơn được gửi tới Google Cloud Vision, để người dùng chỉ đồng ý lại một lần.

## Duyệt việc nhạy cảm

Việc nhạy cảm cần hai người: một người đề xuất, người khác duyệt. Admin cũng không tự duyệt được đề xuất của mình, nên cần tối thiểu 2 người trong đội. Không ai đề xuất hay duyệt việc liên quan đến chính mình.

| Loại (`type`) | `targetId` | Khi duyệt |
|---|---|---|
| `UnlockUser` | Tài khoản khách đang bị khóa | Mở khóa. Tài khoản nội bộ phải dùng `/admin/users/{id}/lock` |
| `CancelDeletion` | Tài khoản có yêu cầu xóa đang chờ | Hủy yêu cầu và khôi phục tài khoản |
| `TransferGroupOwner` | Nhóm, kèm `newOwnerUserId` là thành viên đang hoạt động | Chuyển chủ và báo cho mọi thành viên. Lịch sử nhóm không ghi tên nhân viên |

| API | Việc | Ai |
|---|---|---|
| `POST /ops/approvals` | Đề xuất: `type`, `targetId`, `newOwnerUserId?`, `ticketId?`, `reason`. Trùng đối tượng đang chờ thì 409 | Mọi vai trò nội bộ |
| `GET /ops/approvals?status=` | Staff chỉ thấy đề xuất của mình; Manager trở lên thấy tất cả | Mọi vai trò nội bộ |
| `POST /ops/approvals/{id}/approve` | Duyệt và thực hiện ngay, `note?`. Kiểm tra lại đối tượng lúc duyệt; không còn phù hợp thì trạng thái thành `Failed` kèm `failureCode`, trả 422 `OPS_APPROVAL_FAILED` | Manager trở lên |
| `POST /ops/approvals/{id}/reject` | Từ chối, `note?` | Manager trở lên |

Đề xuất hết hạn sau 7 ngày; đề xuất mới cho cùng đối tượng sẽ tự giải phóng đề xuất quá hạn, chuyển nó sang `Failed` với `failureCode = OPS_APPROVAL_EXPIRED`. Lỗi hệ thống lúc thực hiện cũng chuyển sang `Failed` với `OPS_APPROVAL_EXECUTION_ERROR`; khi đó phải kiểm tra lại đối tượng rồi đề xuất lại nếu cần. Nếu có `ticketId` với `UnlockUser` hoặc `CancelDeletion`, ca đó phải là của đúng tài khoản được đề xuất. `TransferGroupOwner` cần `features.groups` đang bật. Hai người duyệt cùng lúc thì chỉ một người thành công, người còn lại nhận 409.

`/admin/users/{id}/lock` và `/admin/data-deletion-requests/{id}/cancel` vẫn còn làm đường khẩn cấp cho Admin, có ghi audit nhưng không qua bước duyệt.

## Khách hàng và nhóm

| API | Việc | Ai |
|---|---|---|
| `GET /ops/users/{id}` | Tên, email đã che, vai trò, trạng thái khóa, có yêu cầu xóa không. Không có dữ liệu tài chính | Manager trở lên, hoặc người đang phụ trách ca của khách đó |
| `POST /ops/users/{id}/emergency-lock` | Khóa ngay: `ticketId`, `reason`. Ca phải chưa đóng. Chỉ khóa được khách là người tạo ca đó, không khóa được tài khoản nội bộ. Ca chuyển sang `Escalated` để Manager rà soát | Người phụ trách ca, Manager trở lên |
| `GET /ops/groups/{id}?ticketId=` | Tên nhóm, thành viên, vai trò, loại hoạt động. Không có giao dịch hay số tiền. Ca phải chưa đóng; người tạo ca phải là thành viên hoặc từng là thành viên nhóm. Cần `features.groups` đang bật | Người phụ trách ca, Manager trở lên |

## Chức năng của Manager

| API | Việc |
|---|---|
| `GET /ops/dashboard` | Ca đang mở theo trạng thái và mức ưu tiên, chưa phân công, trễ hạn, tải theo từng người; 30 ngày gần nhất: số ca, theo loại, tỉ lệ đúng hạn, điểm hài lòng; số đề xuất chờ duyệt |
| `POST /ops/tickets/{id}/children` | Gom ca cùng lỗi: `ticketIds`. Chỉ một tầng, không gom ca đã đóng |
| `DELETE /ops/tickets/{id}/parent` | Gỡ ca khỏi ca cha |
| `POST /ops/tickets/{id}/broadcast` | Trả lời ca cha và mọi ca con chưa đóng: `body`, `resolve` |
| `GET /ops/canned-replies` (mọi vai trò), `POST`, `PATCH /{id}` | Câu trả lời mẫu; tắt bằng `isActive = false`, không xóa |
| `GET /ops/audit-logs` | Chỉ sự kiện `Ops.*`; lọc sự kiện khác thì 403 |
| `GET /ops/ai-stats` | Như `/admin/ai-stats`, chỉ đọc |

## Xác thực 2 lớp

Áp dụng cho `Staff`, `OpsManager`, `Admin`. Khi bật, token chưa qua OTP nhận 403 ở mọi API `/ops/*` và `/admin/*`, trừ các API dưới đây. API dành cho khách không đổi, kể cả khi tài khoản nội bộ dùng app cá nhân.

Luồng trên web:

1. `POST /auth/login` như bình thường, nhận `accessToken` và `refreshToken`.
2. `GET /ops/auth/status`: `{ required, verified }`.
3. `POST /ops/auth/otp/send`: gửi mã 6 số qua email, trả 204. Email không gửi được thì trả 422 `OPS_OTP_EMAIL_FAILED` và có thể thử lại ngay. Đang trong thời gian chờ thì không gửi thêm; mã đã gửi nếu chưa dùng thì vẫn nhập được.
4. `POST /ops/auth/otp/verify` với `{ code, refreshToken }`: trả `accessToken` mới; `refreshToken` giữ nguyên.

Gia hạn bằng `POST /auth/refresh` không phải nhập lại mã trong 12 giờ kể từ lần nhập gần nhất. Quá 12 giờ, token mới không còn trạng thái đã xác thực nên web phải quay lại bước 3.

`OPS_REQUIRE_2FA`: để trống là bật ngoài Development. Cơ chế này cần gửi được email, nên nếu Brevo hỏng thì không ai vào được trang vận hành và admin. Khi đó đặt `false` rồi khởi động lại để vào sửa, sau đó bật lại ngay.

**Thay đổi phá vỡ:** web admin hiện có phải thêm luồng trên trước khi deploy backend có bật cờ này.

## Giám sát hệ thống

Mỗi phút hệ thống kiểm tra các dịch vụ và nhà cung cấp, gom lỗi giống nhau thành một sự cố và báo qua email. Quy trình cho con người ở `docs/ops-runbook.md`.

### Quyền

| Chức năng | Staff | OpsManager | Admin |
|---|---|---|---|
| Xem trạng thái dịch vụ, sự cố, chỉ số OCR | ❌ | ✅ | ✅ |
| Nhận xử lý, đóng sự cố | ❌ | ✅ | ✅ |
| Xem `technicalDetail` của sự cố | ❌ | ❌ | ✅ |
| Xem danh sách lỗi | ❌ | ❌ | ✅ |

### Các nguồn lỗi

| Nguồn | Theo dõi | Mở sự cố khi | Tự đóng khi |
|---|---|---|---|
| Dịch vụ | Redis, AI, OCR, STT | Lỗi `probeFailureThreshold` lần liên tiếp (mặc định 3) | Tốt `probeRecoveryThreshold` lần liên tiếp (mặc định 3) |
| Nhà cung cấp | Vision, Gemini, nhà cung cấp dự phòng, Rasa, Brevo, Firebase | Lỗi `providerFailureThreshold` lần liên tiếp (mặc định 3); Rasa không phản hồi | Có một lần gọi thành công |
| Lỗi ứng dụng | Log từ mức Error | Cùng một lỗi `errorCountThreshold` lần trong `errorWindowMinutes` (mặc định 10 lần trong 5 phút) | Không lặp lại trong `errorQuietMinutes` (mặc định 30 phút) |
| Job | Hangfire | Thất bại sau mọi lần thử lại, hoặc quá giờ 10 phút chưa chạy | Lần chạy sau thành công, hoặc đã bắt kịp |

Nhà cung cấp chỉ được đếm khi có lần gọi thật. Không có khách quét hóa đơn thì không có gì để đếm, nên sự cố Vision chỉ tự đóng sau lần quét thành công kế tiếp. Đổi key xong mà muốn đóng ngay thì đóng tay.

Dịch vụ chưa cấu hình có trạng thái `Disabled`, không phải lỗi. Nhà cung cấp có trạng thái `Unknown` khi chưa đủ căn cứ: dịch vụ Python bản cũ chưa có API trạng thái, dịch vụ vừa khởi động lại chưa có lần gọi nào, hoặc không đọc được key. `Unknown` không mở sự cố và không đóng sự cố đang mở, nên khởi động lại dịch vụ không làm một sự cố thật tự đóng.

Brevo chưa cấu hình thì có trạng thái `Disabled` và email chỉ được ghi vào log, nên hệ thống không báo được cho ai. Hãy kiểm tra trạng thái này sau khi deploy.

### Mức độ và người nhận

| Mức | Ví dụ | Email |
|---|---|---|
| `Critical` | Redis lỗi; lỗi ứng dụng gấp 5 lần ngưỡng | Admin và OpsManager ngay |
| `High` | AI, OCR, STT, Vision, Gemini, Brevo, Firebase lỗi; lỗi ứng dụng vượt ngưỡng; job thất bại | Admin và OpsManager ngay |
| `Normal` | Rasa tắt, nhà cung cấp dự phòng lỗi, job quá giờ | Chỉ Admin, gom một email lúc 08:00 giờ VN |

- Chưa có người nhận thì nhắc lại mỗi `reminderMinutes` (mặc định 15 phút) trong giờ đầu, sau đó mỗi giờ. Sự cố nặng lên thì báo lại kể cả khi đã có người nhận.
- Sự cố đã báo khi đóng sẽ có thêm một email báo đã khắc phục.
- Email chỉ có dịch vụ, mức độ, ảnh hưởng, thời điểm; không có chi tiết kỹ thuật.

Các ngưỡng sửa được trong nhóm `monitoring` ở `/admin/system-config`. Tắt cả tính năng bằng `MONITORING_ENABLED=false`.

### API: `/api/v1/ops/monitoring`, OpsManager trở lên

| API | Việc |
|---|---|
| `GET /status` | Trạng thái từng dịch vụ (`Healthy`, `Unhealthy`, `Disabled`, `Unknown`) và số sự cố đang mở |
| `GET /incidents` | Lọc `status`, `severity`; phân trang `offset`, `limit` (tối đa 100). Không truyền `status` thì chỉ trả sự cố chưa đóng |
| `GET /incidents/{id}` | Chi tiết, kèm `impact`. `technicalDetail` luôn null nếu không phải Admin |
| `POST /incidents/{id}/acknowledge` | Nhận xử lý, dừng nhắc. Người đầu tiên giữ tên |
| `POST /incidents/{id}/resolve` | Đóng, kèm `note?`. Lỗi còn tiếp diễn thì sẽ mở sự cố mới |
| `GET /ocr` | Chỉ số 24 giờ và 7 ngày: số lần quét theo kết quả và engine, `readableRate`, `localRate` |

`localRate` tăng là dấu hiệu Vision đang lỗi; bằng 1 cũng có thể là chưa cấu hình Vision.

### API: `GET /api/v1/admin/monitoring/error-events`, chỉ Admin

Lọc `service`, `exceptionType`, `from`, `to`; phân trang `offset`, `limit` (tối đa 200). Giữ 30 ngày (`errorRetentionDays`).

Mỗi lỗi chỉ có loại exception, vị trí trong code, message template chưa điền giá trị, đường dẫn đã thay định danh bằng `{id}`, và `traceId`. Không có message của exception, stack trace hay giá trị tham số, vì chúng có thể chứa số tiền, token hoặc nội dung của khách. Cần chi tiết thì dùng `traceId` tra `docker logs`.
