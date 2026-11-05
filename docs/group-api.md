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

## Mã lỗi

`GROUP_TOO_MANY_GROUPS`, `GROUP_TOO_MANY_MEMBERS`, `GROUP_TOO_MANY_INVITATIONS`, `GROUP_ALREADY_MEMBER` (409), `GROUP_MEMBERSHIP_CONFLICT` (409, gửi lại), `GROUP_OWNER_MUST_TRANSFER`, `GROUP_CANNOT_CHANGE_SELF`, `GROUP_INVALID_OWNER_ASSIGNMENT`, `GROUP_INVITATION_INVALID`, `CONCURRENT_UPDATE` (409, gửi lại).
