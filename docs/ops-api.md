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

Tính năng nằm sau cờ `features.support`, mặc định tắt. Khi tắt, API của khách trả 422 `SUPPORT_DISABLED`; API vận hành vẫn dùng được để xử lý nốt các ca đang mở.

### Vòng đời

`New → InProgress → WaitingCustomer → Resolved → Closed`, cộng `Escalated` khi chuyển cấp.

- Nhân viên trả lời khách: sang `WaitingCustomer`, ghi nhận lần phản hồi đầu tiên. Ghi chú nội bộ không đổi gì.
- Khách trả lời khi đang `WaitingCustomer` hoặc `Resolved`: quay về `InProgress`, hoặc `New` nếu chưa ai nhận.
- `Closed` là trạng thái cuối, không trả lời được nữa; khách phải tạo ca mới.
- Ca `Resolved` mà khách không phản hồi sau `support.autoCloseResolvedDays` (mặc định 7 ngày) thì tự đóng.

### Mức ưu tiên và SLA

Khách không tự chọn mức ưu tiên; hệ thống gán theo loại, Manager đổi lại được. Đổi mức thì hạn được tính lại từ lúc tạo ca. Chỉ tính và hiển thị trễ hạn, chưa có cảnh báo tự động.

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

Việc lưu ảnh trong ticket và cho nhân viên xem dữ liệu khi khách đồng ý phải có trong Điều khoản. Bản 4 chưa phát hành nên nội dung này gộp vào bản 4, để người dùng chỉ đồng ý lại một lần. Nếu bản 4 đã lên production trước khi tính năng này phát hành thì phải tăng `ConsentVersions.Terms` lên 5.

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

Đề xuất hết hạn sau 7 ngày. Hai người duyệt cùng lúc thì chỉ một người thành công, người còn lại nhận 409.

`/admin/users/{id}/lock` và `/admin/data-deletion-requests/{id}/cancel` vẫn còn làm đường khẩn cấp cho Admin, có ghi audit nhưng không qua bước duyệt.

## Khách hàng và nhóm

| API | Việc | Ai |
|---|---|---|
| `GET /ops/users/{id}` | Tên, email đã che, vai trò, trạng thái khóa, có yêu cầu xóa không. Không có dữ liệu tài chính | Manager trở lên, hoặc người đang phụ trách ca của khách đó |
| `POST /ops/users/{id}/emergency-lock` | Khóa ngay: `ticketId`, `reason`. Chỉ khóa được khách là người tạo ca đó, không khóa được tài khoản nội bộ. Ca chuyển sang `Escalated` để Manager rà soát | Người phụ trách ca, Manager trở lên |
| `GET /ops/groups/{id}?ticketId=` | Tên nhóm, thành viên, vai trò, loại hoạt động. Không có giao dịch hay số tiền. Người tạo ca phải là thành viên hoặc từng là thành viên nhóm | Người phụ trách ca, Manager trở lên |

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
3. `POST /ops/auth/otp/send`: gửi mã 6 số qua email, trả 204. Đang trong thời gian chờ thì không gửi thêm; mã đã gửi nếu chưa dùng thì vẫn nhập được.
4. `POST /ops/auth/otp/verify` với `{ code, refreshToken }`: trả `accessToken` mới; `refreshToken` giữ nguyên.

Gia hạn bằng `POST /auth/refresh` không phải nhập lại mã trong 12 giờ kể từ lần nhập gần nhất. Quá 12 giờ, token mới không còn trạng thái đã xác thực nên web phải quay lại bước 3.

`OPS_REQUIRE_2FA`: để trống là bật ngoài Development. Cơ chế này cần gửi được email, nên nếu Brevo hỏng thì không ai vào được trang vận hành và admin. Khi đó đặt `false` rồi khởi động lại để vào sửa, sau đó bật lại ngay.

**Thay đổi phá vỡ:** web admin hiện có phải thêm luồng trên trước khi deploy backend có bật cờ này.
