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

Bảng dưới là quyền **mặc định** (seed lần đầu vào bảng `role_permissions`). Admin chỉnh được quyền của **Staff và OpsManager** qua `GET`/`PUT /api/v1/admin/role-permissions`, có hiệu lực ngay (quyền đọc từ DB mỗi request, không chờ token hết hạn). Khởi động lại không ghi đè giá trị đã chỉnh. Admin luôn đủ quyền, User không có quyền nội bộ, `ManageSystem` chỉ Admin.

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
| `ModerateCommunity` | ✅ | ✅ | ✅ |
| `MuteCommunityAuthors` | ❌ | ✅ | ✅ |
| `ManageSubscriptionPlans` | ❌ | ✅ | ✅ |

### Chỉnh quyền của vai trò (chỉ Admin)

| API | Việc |
|---|---|
| `GET /admin/role-permissions` | `{ catalog, adminOnly, roles: [{ role, editable, capabilities }] }`: mọi quyền hệ thống có, quyền chỉ Admin, và quyền đang áp dụng của User, Staff, OpsManager, Admin |
| `PUT /admin/role-permissions/{role}` | Đặt **trọn bộ** quyền của `Staff` hoặc `OpsManager`: `{ "capabilities": ["HandleTickets", ...], "reason": "…" }`. Gửi lại đúng bộ đang có thì không ghi gì |

Từ chối với 422: vai trò không sửa được (`ROLE_PERMISSIONS_ROLE_NOT_EDITABLE`), tên quyền lạ (`ROLE_PERMISSIONS_UNKNOWN_CAPABILITY`), gán quyền chỉ Admin (`ROLE_PERMISSIONS_ADMIN_ONLY`), hoặc thiếu quyền phụ thuộc (`ROLE_PERMISSIONS_BROKEN_DEPENDENCY`: `AccessCustomerData` cần `HandleTickets`, `CreateDraftTransactions` cần `AccessCustomerData`). Mỗi lần đổi ghi audit `Admin.RolePermissions.Updated` (role, added, removed, reason).

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
| `GET /?cursor=&limit=`, `GET /{id}` | Danh sách (mới nhất trước, phân trang con trỏ: `limit` mặc định 20, tối đa 100; trang sau dùng `meta.cursor`, hết thì `null`), chi tiết kèm tin nhắn |
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
| `GET /access-log?cursor=&limit=` | Lịch sử xem dữ liệu của mình: ca, phần dữ liệu, thời điểm. Phân trang con trỏ: `limit` mặc định 20, tối đa 100; trang sau dùng `meta.cursor`, hết thì `null` |

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

## Kiểm duyệt cộng đồng: `/api/v1/ops/community`

Chạy được cả khi `features.community` tắt. **Không response hay audit nào có người đăng hoặc người báo cáo**; tên chỉ hiện trong `publicAuthorName` khi tác giả đã chọn công khai. Dữ liệu tài chính của khách không liên quan tới các API này.

| API | Việc | Quyền |
|---|---|---|
| `GET /reports?offset&limit` | Báo cáo còn mở: `self_harm` trước, rồi cũ trước. Có nội dung bị báo cáo | `ModerateCommunity` |
| `POST /reports/{id}/resolve` | `{ "decision": "upheld\|dismissed" }`. `upheld` gỡ nội dung; `dismissed` hiện lại. Đóng mọi báo cáo còn mở của nội dung đó. Đã xử lý trả 409 `COMMUNITY_ALREADY_RESOLVED` | `ModerateCommunity` |
| `GET /images?offset&limit` | Ảnh chờ duyệt, cũ trước, kèm tiêu đề và nội dung bài | `ModerateCommunity` |
| `GET /images/{id}` | Xem ảnh để quyết định | `ModerateCommunity` |
| `POST /images/{id}/approve` | Duyệt ảnh đang chờ | `ModerateCommunity` |
| `POST /images/{id}/reject` | Từ chối ảnh đang chờ hoặc gỡ ảnh đã duyệt. Dữ liệu ảnh bị xóa ngay | `ModerateCommunity` |
| `POST /posts/{id}/hide`, `/restore` | Gỡ hoặc hiện lại bài, đóng báo cáo còn mở | `ModerateCommunity` |
| `POST /comments/{id}/hide`, `/restore` | Gỡ hoặc hiện lại bình luận, đóng báo cáo còn mở | `ModerateCommunity` |
| `POST /places/{id}/hide` | Ẩn quán và mọi bài về quán đó | `ModerateCommunity` |
| `POST /posts/{id}/mute-author`, `POST /comments/{id}/mute-author` | `{ "days": 1..365 }`. Cấm đăng mà không biết tác giả là ai | `MuteCommunityAuthors` |

Đủ 3 người khác nhau báo cáo thì nội dung tự ẩn (`autoHideReportCount`), trừ lý do `self_harm`: lý do này xếp đầu hàng đợi và không tính vào việc tự ẩn.

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
| Nhà cung cấp | Vision, Gemini, nhà cung cấp dự phòng, Brevo, Firebase | Lỗi `providerFailureThreshold` lần liên tiếp (mặc định 3) | Có một lần gọi thành công |
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
| `Normal` | nhà cung cấp dự phòng lỗi, job quá giờ | Chỉ Admin, gom một email lúc 08:00 giờ VN |

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

## Cập nhật số liệu kinh tế tự động (CPI)

Job Hangfire `economic-assumption-refresh` đọc thông cáo CPI mới nhất của Cục Thống kê và ghi giả định `general_price_growth` (đơn vị `pct_per_year`, giá trị là CPI bình quân từ đầu năm so với cùng kỳ). **Lịch nằm trong cấu hình, không nằm trong code**: lưu hoặc hoàn tác cấu hình là job được đăng ký lại ngay (không cần deploy hay khởi động lại). Cấu hình ở system config nhóm `economicFeeds` (có lịch sử thay đổi):

| Trường | Ý nghĩa |
|---|---|
| `enabled` | Mặc định `true`. `false` = gỡ job khỏi lịch |
| `scheduleDayOfMonth`, `scheduleHour` | Ngày trong tháng (1–28) và giờ (0–23, **giờ Việt Nam**). Mặc định ngày 6, 9 giờ |
| `cpiIndexUrl` | Địa chỉ trang liệt kê bài CPI (https). **Để trống = job chạy nhưng không gọi mạng**. Gợi ý: trang tìm kiếm của site, xem `docs/economic-feeds-probe.md` |
| `autoApproveMaxDeltaBps` | Chênh tối đa so với bản đang dùng để job tự duyệt (mặc định 100 = 1 điểm phần trăm) |
| `minRateBps`, `maxRateBps` | Khoảng hợp lý; ngoài khoảng thì không bao giờ tự duyệt |

`POST /api/v1/admin/economic-feeds/refresh` (chỉ Admin, có audit `Admin.EconomicFeed.Refreshed`) chạy ngay một lượt và trả `{ "outcome": "Disabled | AlreadyCurrent | AutoApproved | NeedsReview | Failed" }`, dùng để thử sau khi đổi địa chỉ nguồn. Không phụ thuộc `enabled`.

Kết quả mỗi lượt:
- **Tự duyệt:** tạo bản mới và duyệt ngay, bản cũ chuyển Retired. Người thực hiện là tài khoản hệ thống (có trong audit log).
- **Cần xem:** số lệch nhiều hoặc ngoài khoảng thì để `Draft` và mở sự cố dịch vụ `economic_feed` (mức Normal) cho Admin và OpsManager; duyệt hoặc bỏ ở trang Giả định kinh tế.
- **Không đọc được** (trang đổi giao diện, không tải được, không thấy bài): giữ nguyên bản cũ và mở cùng sự cố. Sự cố tự đóng khi lượt sau thành công.
- Cùng một bài chạy lại không tạo thêm bản nào.

## Gói đăng ký: `/api/v1/ops/subscriptions`

Cần `ManageSubscriptionPlans`, tức OpsManager hoặc Admin. Chạy cả khi cờ `features.subscriptions` tắt, để chuẩn bị gói trước khi bật. Mọi thao tác ghi đều vào `audit_logs`. Tài liệu cho web và app: `docs/subscription-api.md`.

| API | Việc |
|---|---|
| `GET features` | Danh mục chức năng bán được. Danh mục nằm trong code vì mỗi chức năng phải có chỗ chặn thật |
| `GET plans` | Mọi gói, kể cả gói đã xóa |
| `POST plans` `{ code, name, description?, monthlyPriceCents?, yearlyPriceCents?, sortOrder? }` | Tạo gói ở trạng thái chưa bán. `code` không đổi được và không dùng lại được; phải có ít nhất một giá |
| `PATCH plans/{id}` `{ name?, description?, monthlyPriceCents?, yearlyPriceCents?, removeMonthlyPrice?, removeYearlyPrice?, isActive?, sortOrder? }` | Sửa gói, trường không gửi thì giữ nguyên |
| `PATCH plans/{id}` `{ isActive: true }` | Mở bán, cần có giá và ít nhất một chức năng đang bật. Chức năng không bật sẽ bị khóa với người mua, kể cả khi gói miễn phí có |
| `DELETE plans/{id}` | Ngừng bán, không xóa vật lý. Người đang dùng giữ đến hết kỳ |
| `PUT plans/{id}/features` `[{ key, enabled, monthlyLimit }]` | Thay toàn bộ chức năng của gói. `monthlyLimit` null là không giới hạn, 0 là hết lượt ngay |
| `GET orders?status=&offset=&limit=` | Đơn mua, chỉ đọc. Không có id người dùng |

Giá tính bằng đồng. Gói miễn phí có sẵn, không có giá, không tắt và không xóa được; hạn mức của nó áp dụng ngay cho mọi người chưa mua. Sửa gói trả phí không ảnh hưởng người đã mua, vì đơn giữ bản chụp giá và quyền lợi.

Đơn `NeedsReview` là tiền đã về nhưng chưa cấp được gói. Mỗi đơn mở một sự cố `subscription_payments`, xử lý theo `docs/ops-runbook.md`.

## Chất lượng dự báo: độ chính xác và phản hồi "dự báo sai"

Vòng khép kín: người dùng xem dự báo, báo sai, Staff gán nguyên nhân, OpsManager theo dõi sai số thật. Không có định danh người dùng ở bất kỳ phản hồi nào của nhân sự.

**Job `forecast-accuracy`** chạy 02:30 giờ VN (cron UTC `30 19 * * *`), sau nhận diện khoản định kỳ. Hai bước: (1) điền số dư **thực tế** cho các bản chụp đã tới ngày của mốc, (2) **chụp** dự báo hôm nay cho mọi người có hoạt động trong cửa sổ lịch sử: một dòng mỗi mốc nhìn trước 7, 14, 30 ngày, lưu trong `forecast_snapshots` (số dư dự kiến, hai đầu dải, mốc so sánh "chỉ khoản định kỳ", mức tin cậy, phiên bản cấu hình, tên phương pháp). Chạy lại trong ngày không tạo bản trùng.

Số dư thực tế = số dư lúc chụp + thu chi thật từ ngày gốc tới ngày của mốc, cùng định nghĩa với dự báo: chỉ ví được tính vào dự báo, không gồm chuyển khoản nội bộ và cho vay/mượn. Giao dịch user loại khỏi dự báo vẫn tính vì tiền đã đổi thật.

| API | Quyền | Việc |
|---|---|---|
| `POST /api/v1/forecast/feedback` `{reason, note?}` | Người dùng (JWT) | Báo dự báo sai. `reason`: `balance_wrong`, `missing_bill`, `wrong_bill`, `unusual_spending`, `other`; `note` tối đa 300 ký tự. Gắn với bản chụp gần nhất của chính họ; gửi lại khi phản hồi trước chưa xử lý thì trả lại phản hồi đó. 422 `FORECAST_FEEDBACK_INVALID` |
| `GET /api/v1/ops/forecast/feedback?status=&limit=&offset=` | Staff trở lên (xử lý ticket) | Hàng đợi, `status` là `new` hoặc `labeled`. Mỗi dòng có lý do, lời nhắn, số dự báo và số thực tế của bản chụp; **không có id người dùng** |
| `PATCH /api/v1/ops/forecast/feedback/{id}` `{cause}` | Staff trở lên | Gán nguyên nhân: `data_missing`, `recurring_misread`, `one_off`, `model_issue`, `other`. Gán lại được. Nhật ký `Ops.ForecastFeedback.Labeled` chỉ có id và nguyên nhân |
| `GET /api/v1/ops/forecast/accuracy?windowDays=30` | OpsManager trở lên (xem giám sát) | Chỉ số trong cửa sổ 7–180 ngày |

`accuracy` trả, theo mỗi mốc 7/14/30 ngày và theo nhóm `all`, `low` (ít dữ liệu), `normal`:
- `maeCents`: sai số tuyệt đối trung bình của số dư dự báo (đồng); `medianAbsErrorCents`.
- `maeRecurringOnlyCents` và `maeHoldCents`: cùng chỉ số nếu chỉ dùng khoản định kỳ (kiểu Simplifi/Monzo) và nếu giả định số dư không đổi. `ratioVsHold` và `ratioVsRecurringOnly` nhỏ hơn 1 nghĩa là dự báo hơn mốc đó.
- `coveragePercent`: tỉ lệ số dư thật nằm trong dải. Dải đúng nghĩa P25–P75 thì phải gần 50%; cao hơn nhiều là dải quá rộng. Dải hiện tại cộng dồn P25/P75 từng ngày nên **dự kiến cao hơn 50%**; đây là số đo để quyết định có thay bằng bootstrap hay không.
- `meanBandWidthCents`, số mẫu, số bản chụp đã điền và còn chờ, số phản hồi theo lý do và theo nguyên nhân.

Giới hạn đã biết: chưa có mốc so sánh trung bình 90 ngày (B1) và seasonal naive (B2); mốc "không đổi" là mốc thấp nhất. Số thực tế dùng giao dịch đã xác nhận, nên giao dịch ghi muộn làm số thực tế đổi theo.
