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
