# Ma trận quyền của nhân sự vận hành (RBAC)

**Trạng thái:** đã triển khai ở backend (Task 19), kiểm bằng `FinMate.Tests/Integration/Controllers/RbacTests.cs`.

## Vai trò

| Vai trò | Giá trị trong JWT (`ClaimTypes.Role`) | Mục đích |
|---|---|---|
| Người dùng | `User` | Người dùng ứng dụng. Không có quyền quản trị nào. |
| Admin | `Admin` | Quản trị hệ thống: người dùng, cấu hình, bí mật, nhật ký. Làm được mọi việc bên dưới. |
| Manager | `Manager` | Duyệt giả định kinh tế và tài liệu tri thức; xử lý khiếu nại đã leo thang. Không quản lý người dùng hay cấu hình hệ thống. |
| SupportStaff | `SupportStaff` | Xem và trả lời khiếu nại của người dùng. |
| ContentStaff | `ContentStaff` | Soạn giả định kinh tế và tài liệu tri thức (bản nháp), xem câu hỏi chưa có tri thức. Không duyệt. |
| VoucherStaff | — | **Chỉ mô tả, chưa có tính năng.** Khi có tính năng voucher mới thêm vai trò, policy và dòng trong ma trận. |

Policy (khai báo ở `AuthorizationPolicies`, danh sách vai trò TƯỜNG MINH nên một giá trị lạ trong JWT không khớp policy nào và bị từ chối):

| Policy | Vai trò được qua |
|---|---|
| `AdminOnly` | Admin |
| `ManagerOrAbove` | Admin, Manager |
| `SupportStaffOrAbove` | Admin, Manager, SupportStaff |
| `ContentStaffOrAbove` | Admin, Manager, ContentStaff |

## Ma trận

✔ = được, — = bị từ chối (403).

| Khu vực / hành động | Admin | Manager | SupportStaff | ContentStaff |
|---|---|---|---|---|
| Người dùng, vai trò, yêu cầu xóa dữ liệu (`/admin/users`, `/admin/data-deletion-requests`) | ✔ | — | — | — |
| Cấu hình hệ thống, bí mật, nhà cung cấp, danh mục, nhật ký kiểm tra, AI/Rasa (mọi `AdminControllerBase` khác) | ✔ | — | — | — |
| Giả định kinh tế: xem, soạn bản nháp (`/admin/assumptions`) | ✔ | ✔ | — | ✔ |
| Giả định kinh tế: **duyệt, gỡ** | ✔ | ✔ | — | — |
| Tri thức của agent: xem tài liệu, gửi bản nháp, xem/xử lý câu hỏi chưa có tri thức (`/admin/knowledge`) | ✔ | ✔ | — | ✔ |
| Tri thức của agent: **duyệt, gỡ** tài liệu | ✔ | ✔ | — | — |
| Khiếu nại: xem, trả lời (xem Task 20) | ✔ | ✔ | ✔ | — |
| Khiếu nại đã leo thang: quyết định cuối | ✔ | ✔ | — | — |

## Quy tắc đi kèm (giữ từ Phase 13, mở rộng cho vai trò mới)

1. **Không tự đổi vai trò của chính mình**, kể cả đổi lên (một người tự nâng quyền làm mất ý nghĩa "ai trao quyền cho ai" trong nhật ký).
2. **Không hạ admin hoạt động cuối cùng** xuống bất kỳ vai trò nào khác (kể cả Manager hay nhân sự).
3. **Đổi sang một vai trò không bao hàm vai trò cũ thì thu hồi refresh token** (`UserRole.Covers`): hạ quyền, hoặc chuyển ngang giữa hai nhóm nhân sự. Thăng quyền không thu hồi. Lý do: vai trò nằm trong access token và không được tra lại DB mỗi request, nên token cũ còn hiệu lực tới hết TTL (mặc định 15 phút); thu hồi refresh token chặn việc gia hạn.
4. **Thêm hành động cần quyền cao hơn controller của nó** (ví dụ duyệt trong controller cho ContentStaff) thì gắn `[Authorize(Policy = ManagerOrAbove)]` ngay hành động đó; hai policy cùng phải qua. Hành động đó phải có test 403 riêng, vì thiếu attribute không gây lỗi nào.
5. Controller mới dành cho nhân sự kế thừa đúng một lớp cơ sở (`AdminControllerBase`, `ManagerControllerBase`, `SupportStaffControllerBase`, `ContentStaffControllerBase`), không gõ lại `[Authorize]`.

## Ghi chú vận hành

- Ràng buộc DB `chk_users_role` cho phép: `user`, `admin`, `manager`, `supportstaff`, `contentstaff` (migration `ExtendUserRoleCheckForStaff`). **Bước lùi migration này sẽ lỗi nếu đã có người dùng mang vai trò nhân sự**: đổi họ về `user` trước.
- Vai trò chỉ gán được qua `PATCH /api/v1/admin/users/{id}/role` (Admin). Seed `ADMIN_SEED_*` vẫn là cách duy nhất tạo admin đầu tiên.
- Ma trận này là hợp đồng: thêm vai trò hay quyền mới thì sửa tệp này cùng lúc với `AuthorizationPolicies` và `RbacTests`.
