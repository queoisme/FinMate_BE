# Tài chính nhóm: API và phân quyền

Dành cho nhóm Flutter. Mọi route nằm dưới `/api/v1`, cần JWT, trả về envelope chung. Enum trong JSON là chuỗi PascalCase (`"Owner"`, `"ManageBudgets"`).

Công tắc: `config.features.groups` ở `GET /app-config`, mặc định `false`. Server không chặn theo công tắc này, giống các công tắc khác; app dùng nó để ẩn tính năng.

## Vai trò và quyền

| Quyền | Owner | Manager | Member | Viewer |
|---|---|---|---|---|
| `ViewOverview` | ✅ | ✅ | ✅ | ✅ |
| `ViewMemberTransactions` | ✅ | ✅ | Khi `transactionVisibility = Full` | Khi `transactionVisibility = Full` |
| `ViewActivity` | ✅ | ✅ | ✅ | ❌ |
| `ShareTransactions` | ✅ | ✅ | ✅ | ❌ |
| `ContributeGoals` | ✅ | ✅ | ✅ | ❌ |
| `ManageSharedTransactions` | ✅ | ✅ | ❌ | ❌ |
| `ManageBudgets`, `ManageGoals`, `ManageCategories` | ✅ | ✅ | ❌ | ❌ |
| `InviteMembers`, `RemoveMembers` | ✅ | ✅, chỉ với vai trò thấp hơn | ❌ | ❌ |

- **Chỉ Owner, không cấp được cho ai:** đổi vai trò, chỉnh quyền từng người, đổi cài đặt nhóm, chuyển chủ, xóa nhóm.
- **Không ai có:** sửa số tiền hoặc xóa giao dịch của người khác; xem ví, số dư, nội dung thông báo gốc hoặc giao dịch chưa chia sẻ của người khác.
- **Quyền từng người:** `(mặc định theo vai trò + grantedPermissions) − revokedPermissions`. Dùng `myPermissions` trong `GroupDto` để ẩn hoặc hiện nút, đừng suy từ vai trò.
- **Lỗi:** không phải thành viên → 404; là thành viên nhưng thiếu quyền → 403.

## Nhóm

| Route | Quyền | Ghi chú |
|---|---|---|
| `POST /groups` `{name, type?}` | Đăng nhập | `type`: `Family` (mặc định) hoặc `Other`. Trả 201 |
| `GET /groups` | Đăng nhập | Các nhóm đang tham gia |
| `GET /groups/{id}` | Thành viên | |
| `PATCH /groups/{id}` `{name?, transactionVisibility?}` | Owner | `transactionVisibility`: `Full` hoặc `Summary` |
| `DELETE /groups/{id}` | Owner | Xóa mềm, 204 |
| `GET /groups/{id}/activities?limit=` | `ViewActivity` | Mới nhất trước, tối đa 100 |

## Thành viên

| Route | Quyền | Ghi chú |
|---|---|---|
| `GET /groups/{id}/members` | `ViewOverview` | `permissions`, `grantedPermissions`, `revokedPermissions` là `null` trừ khi xem chính mình hoặc người xem là Owner. `isActive = false`: tài khoản đã yêu cầu xóa, tên được thay bằng "Thành viên không còn hoạt động" |
| `PATCH /groups/{id}/members/{userId}` `{role?, grantedPermissions?, revokedPermissions?}` | Owner | Không đổi được chính mình; không gán `Owner` ở đây. Đổi vai trò thì xóa quyền ghi đè cũ, trừ khi gửi kèm quyền mới |
| `DELETE /groups/{id}/members/{userId}` | `RemoveMembers` | Chỉ vai trò thấp hơn mình |
| `POST /groups/{id}/leave` | Thành viên | Owner nhận 422 `GROUP_OWNER_MUST_TRANSFER` |
| `POST /groups/{id}/transfer-ownership` `{newOwnerUserId}` | Owner | Chủ cũ thành Manager |

## Lời mời

| Route | Quyền | Ghi chú |
|---|---|---|
| `POST /groups/{id}/invitations` `{email?, role?}` | `InviteMembers` | Không có `email`: trả `token` **một lần duy nhất** để app tự tạo link chia sẻ. Có `email`: gửi mã qua email, trả `emailSent`. `role`: `Manager`, `Member` (mặc định), `Viewer`, và phải thấp hơn vai trò người mời |
| `GET /groups/{id}/invitations` | `InviteMembers` | Lời mời đang chờ, không có mã |
| `DELETE /groups/{id}/invitations/{invitationId}` | `InviteMembers` | Thu hồi |
| `POST /group-invitations/preview` `{token}` | Đăng nhập | Tên nhóm, vai trò, người mời |
| `POST /group-invitations/accept` `{token}` | Đăng nhập | Trả `{groupId}` |

Mã sai, hết hạn, đã dùng, đã thu hồi hoặc dành cho email khác đều trả 422 `GROUP_INVITATION_INVALID`. Lời mời theo email đòi tài khoản đã xác minh đúng email đó. Hai route `group-invitations` có giới hạn tần suất chặt (429).

## Chia sẻ giao dịch

Ví và số dư không đổi; khoản đã chia sẻ vẫn tính vào ngân sách cá nhân của chủ giao dịch. Không chia sẻ được: nháp, chuyển khoản nội bộ, cho vay/mượn (422 `GROUP_TRANSACTION_NOT_SHAREABLE`).

| Route | Quyền | Ghi chú |
|---|---|---|
| `POST /transactions`, `POST /transactions/{id}/confirm` thêm `groupId?`, `groupCategoryId?` | `ShareTransactions` | Không gửi `groupId` thì áp quy tắc tự chia sẻ |
| `PUT /transactions/{id}/group` `{groupId, groupCategoryId?}` | Chủ giao dịch + `ShareTransactions` | Cũng dùng để chuyển nhóm hoặc đổi danh mục nhóm |
| `DELETE /transactions/{id}/group` | Chủ giao dịch, còn là thành viên | Người đã rời nhóm chỉ xóa được hẳn giao dịch |
| `GET /groups/{id}/transactions?year=&month=&limit=` | `ViewOverview` | Mặc định tháng này, tối đa 200. Thiếu `ViewMemberTransactions` thì chỉ trả khoản của chính mình |
| `PATCH /groups/{id}/transactions/{transactionId}` `{groupCategoryId}` | `ManageSharedTransactions` | Không đổi dữ liệu cá nhân |
| `DELETE /groups/{id}/transactions/{transactionId}` | `ManageSharedTransactions` | Gỡ khỏi nhóm; giao dịch vẫn còn ở phía chủ |
| `GET/POST /groups/{id}/share-rules` `{financialAccountId?, categoryId?}`, `DELETE .../{ruleId}` | Của chính mình; tạo cần `ShareTransactions` | Bỏ trống cả hai là chia sẻ mọi khoản. Rời nhóm thì quy tắc tắt |

`TransactionDto` có thêm `groupId`. `GET /groups/{id}/transactions` cố ý không có ví và số dư.

## Danh mục, ngân sách, mục tiêu

| Route | Quyền | Ghi chú |
|---|---|---|
| `GET /groups/{id}/categories` | `ViewOverview` | |
| `POST /groups/{id}/categories` `{name, iconName?}`, `DELETE .../{categoryId}` | `ManageCategories` | Danh mục đang có ngân sách thì không xóa được |
| `GET /groups/{id}/budgets` | `ViewOverview` | Số liệu tháng này |
| `POST /groups/{id}/budgets` `{categoryId?, groupCategoryId?, limitCents}` | `ManageBudgets` | Bỏ trống cả hai là ngân sách tổng. `categoryId` chỉ nhận danh mục hệ thống |
| `PATCH /groups/{id}/budgets/{budgetId}` `{limitCents}`, `DELETE ...` | `ManageBudgets` | |
| `GET /groups/{id}/goals`, `GET .../{goalId}/contributions` | `ViewOverview` | |
| `POST /groups/{id}/goals` `{name, targetCents, deadline?}`, `PATCH .../{goalId}`, `POST .../{goalId}/cancel` | `ManageGoals` | |
| `POST /groups/{id}/goals/{goalId}/contribute` `{amountCents, note?}` | `ContributeGoals` | Chỉ ghi sổ, không trừ ví |
| `PUT /groups/{id}/alerts` `{enabled}` | Thành viên | Tự bật hoặc tắt cảnh báo của nhóm này cho mình |

Push mới: `data.kind` là `group_budget` (`groupId`, `budgetId`, `percent`) hoặc `group_goal` (`groupId`, `goalId`). Cả hai cũng hiện trong `GET /forecast/alerts`.

## Mã lỗi

`GROUP_TOO_MANY_GROUPS`, `GROUP_TOO_MANY_MEMBERS`, `GROUP_TOO_MANY_INVITATIONS`, `GROUP_ALREADY_MEMBER` (409), `GROUP_MEMBERSHIP_CONFLICT` (409, gửi lại), `GROUP_OWNER_MUST_TRANSFER`, `GROUP_CANNOT_CHANGE_SELF`, `GROUP_INVALID_OWNER_ASSIGNMENT`, `GROUP_INVITATION_INVALID`, `CONCURRENT_UPDATE` (409, gửi lại)., `GROUP_TRANSACTION_NOT_SHAREABLE`, `GROUP_BUDGET_ALREADY_EXISTS` (409), `GROUP_BUDGET_INVALID_CATEGORY`, `GROUP_CATEGORY_DUPLICATE` (409), `GROUP_CATEGORY_IN_USE` (409), `GROUP_GOAL_NOT_ACTIVE`.
