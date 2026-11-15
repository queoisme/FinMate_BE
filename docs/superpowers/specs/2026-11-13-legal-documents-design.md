# Văn bản pháp lý do Admin/Manager cập nhật — thiết kế

Ngày: 2026-11-13. Trạng thái: chờ duyệt. Yêu cầu gốc: `FinMate_FE/docs/be-api-requests.md`, P1 mục 1 (màn A7, H9, H10).

## Ý định
App cần đọc Điều khoản sử dụng và Chính sách quyền riêng tư từ server, và đội vận hành phải sửa được chữ mà không cần deploy. Hiện nội dung viết cứng trong FE (`legal_document_screen.dart`, 4 mục mỗi văn bản, ghi "phiên bản 2.1"), còn số phiên bản đồng ý là hằng `ConsentVersions.Terms = 4` trong code. Hai thứ này lệch nhau và không ai ngoài lập trình viên đổi được.

## Mục tiêu
- `GET legal/terms`, `GET legal/privacy` công khai (màn đăng ký cần đọc trước khi có tài khoản), trả `{ version, updatedAt, sections[{title, body}] }`.
- Manager (và Admin) soạn bản nháp; **chỉ Admin xuất bản**.
- Xuất bản = tạo phiên bản mới = mọi đồng ý cũ mất hiệu lực: `termsAccepted` về `false` cho mọi người (app hỏi lại) và đồng ý gửi phiên bản cũ bị 409. Server không dừng AI vì việc này: đồng ý AI là phiên bản riêng và không có chỗ nào ở server chặn theo `termsAccepted`.
- Mọi lần soạn và xuất bản có dấu vết trong `audit_logs`.

## Không làm (và vì sao)
- Phiên bản riêng cho từng văn bản: Điều khoản và Chính sách đang được đồng ý cùng lúc bằng một số; tách ra phải đổi cột User, API đồng ý và luồng FE mà chưa có nhu cầu.
- "Sửa nhỏ không hỏi lại": mỗi lần xuất bản luôn là phiên bản mới. Thêm cờ vào lúc xuất bản nếu sau này cần.
- Soạn nội dung bản 5 (Google Vision, nhóm, hỗ trợ). Đó là việc của người phụ trách pháp lý; thiết kế này chỉ làm chỗ để họ nhập.
- Gửi thông báo "điều khoản đã đổi". FE tự thấy `termsAccepted = false` và hiện lại màn đồng ý.
- Sửa FE. Cần FE đọc từ API và gửi đúng `version`; ngoài phạm vi repo này.

## Cách làm (đã chọn: một bảng, mỗi dòng một phiên bản chứa cả hai văn bản)
Đã cân nhắc: (B) hai số phiên bản riêng — tốn migration và đổi FE; (C) giữ hằng trong code, chỉ đưa chữ vào system config — Admin không xuất bản và buộc đồng ý lại được. Chọn A vì khớp cách đồng ý hiện có.

### Dữ liệu
Bảng `legal_versions`:

| Cột | Ghi chú |
|---|---|
| `id` uuid | |
| `version` int null | `null` khi còn là nháp; gán lúc xuất bản = max(đã xuất bản) + 1 |
| `status` | `draft` \| `published` |
| `terms_sections`, `privacy_sections` jsonb | mảng `{title, body}` |
| `created_by`, `updated_at` | người soạn gần nhất; `updated_at` dùng để chặn ghi đè |
| `published_by`, `published_at` | |

Ràng buộc: unique `version` khi khác null; tối đa **một** dòng `draft` (unique index một phần). Dòng `published` không bao giờ sửa; muốn đổi thì soạn nháp mới.

Migration tạo bảng và thêm sẵn **bản 4 đã xuất bản** với chữ hiện có ở FE, để người dùng hiện tại không bị hỏi lại. Bản 4 này là nội dung tạm; bản đầy đủ sẽ lên là bản 5.

### Thay hằng bằng dữ liệu
`ConsentVersions.Terms` bị xóa. Một `ILegalVersionProvider.CurrentTermsVersionAsync()` đọc `max(version)` của dòng `published` mỗi lần gọi (truy vấn theo chỉ mục, không cache, để vừa xuất bản là có hiệu lực ở mọi instance).

Ba chỗ cần đổi:
- `User.TermsAccepted` (`backend/FinMate.Domain/Entities/User.cs:111`) là thuộc tính tính từ hằng; Domain không đọc DB được nên đổi thành phương thức nhận số phiên bản hiện hành, và `UserProfileDto` nhận số đó từ nơi dựng DTO.
- `ConsentCommandHandler` (`backend/FinMate.Application/Auth/Commands/ConsentCommandHandler.cs:27`) lấy phiên bản từ provider; gửi sai phiên bản vẫn trả 409 như cũ.
- Các test đang dùng `ConsentVersions.Terms` chuyển sang đọc từ provider hoặc từ bản seed.

`ConsentVersions.Ai`, `Training`, `SurveyAi`, `Community` giữ nguyên là hằng.

### API
| Endpoint | Ai | Việc |
|---|---|---|
| `GET api/v1/legal/terms`, `GET api/v1/legal/privacy` | ẩn danh | bản đã xuất bản mới nhất |
| `GET api/v1/ops/legal/versions` | `ManageLegalDocuments` | lịch sử (số phiên bản, ngày, người xuất bản) và bản nháp nếu có |
| `GET api/v1/ops/legal/draft` | `ManageLegalDocuments` | đọc nháp; nếu chưa có, trả bản hiện hành làm điểm xuất phát |
| `PUT api/v1/ops/legal/draft` | `ManageLegalDocuments` | tạo hoặc ghi đè nháp; body `{ termsSections, privacySections, baseUpdatedAt }` |
| `POST api/v1/admin/legal/draft/publish` | Admin (`ManageSystem`) | xuất bản nháp thành phiên bản mới |

- `PUT` trả 409 nếu `baseUpdatedAt` khác `updated_at` hiện tại (hai người sửa cùng lúc). Dòng nháp mới tạo thì bỏ qua kiểm tra này.
- Kiểm tra đầu vào: mỗi văn bản 1–50 mục; `title` ≤ 200 ký tự, `body` ≤ 20 000 ký tự, không rỗng. `body` là văn bản thuần, FE không được hiển thị như HTML.
- `publish` từ chối nếu không có nháp, hoặc nháp giống hệt bản hiện hành (xuất bản không đổi gì mà vẫn buộc cả hệ thống đồng ý lại là lỗi người dùng không thấy được). Việc đánh số và chuyển trạng thái nằm trong một giao dịch.
- Controller ops kế thừa `OpsControllerBase` (policy `ManagerOrAbove`) giống `OpsSubscriptionsController`; controller publish kế thừa `AdminControllerBase` (tự có `AdminOnly`, không lặp lại attribute).

### Phân quyền
- Thêm `OpsCapability.ManageLegalDocuments = 1 << 17`, thêm vào `ManagerCapabilities`. Admin có sẵn vì `ManagerCapabilities` nằm trong quyền Admin. Staff không có, nhưng Admin gán thêm được qua `admin/role-permissions` như các quyền khác.
- Xuất bản dùng `ManageSystem` (chỉ Admin, `RolePermissionRules.AdminOnly` đã loại nó khỏi mọi vai trò khác). Quyền đọc DB mỗi lần qua `IOpsAccessService`.
- Audit: hai sự kiện mới trong `AuditEvents`, `LegalDraftSaved` và `LegalVersionPublished` (kèm số phiên bản, không ghi nội dung vì dài).

## Ràng buộc đã biết
- Xuất bản buộc **mọi** người dùng đồng ý lại trước khi dùng app tiếp (việc chặn do app làm). Đó là lý do chỉ Admin được xuất bản và là lý do `publish` chặn bản nháp không có thay đổi. (Bản đầu của thiết kế này nói thêm là server dừng AI; sai, vì đồng ý AI là phiên bản riêng. Đã sửa khi thực thi.)
- Profile người dùng được cache 15 phút, nên `GetUserProfileQueryHandler` so lại `TermsVersion` đã cache với phiên bản hiện hành mỗi lần đọc; thiếu bước này thì xuất bản không có hiệu lực ngay.
- `RolePermissionSeeder` không cập nhật hàng đã có, nên migration tự cấp `ManageLegalDocuments` cho các hàng OpsManager sẵn có.
- App cũ gửi `version = 4` sau khi bản 5 lên sẽ nhận 409 (đúng như `REQUIREMENTS.md` đã mô tả cho bản 4). FE phải đọc `version` từ `GET legal/...` thay vì viết cứng.
- Nội dung bản tạm có thể bị coi là văn bản pháp lý đang hiệu lực. Việc xuất bản bản 5 cần người phụ trách pháp lý duyệt chữ (theo `REQUIREMENTS.md` FR-CONS-05).

## Để người xây quyết định
- Tên bảng và cột, tên file migration (`PascalCase`), dạng response lỗi theo quy ước hiện có.
- Cách tách `UserProfileDto` nhận số phiên bản (tham số của `From` hay bước gán sau).

## Kiểm tra cần có
- Đơn vị: xuất bản gán đúng `max + 1`; từ chối nháp trống, nháp trùng bản hiện hành, nháp quá giới hạn.
- Tích hợp: ẩn danh đọc được `legal/terms` và `legal/privacy`; Manager `PUT` được nhưng `publish` trả 403; Admin xuất bản được; sau xuất bản người dùng đã đồng ý bản cũ có `termsAccepted = false` và gửi đồng ý bản cũ trả 409; hai `PUT` lệch `baseUpdatedAt` trả 409; Staff không có quyền cho tới khi Admin gán.
- Cập nhật `docs/ops-api.md` (ma trận quyền, endpoint) và `docs/REQUIREMENTS.md` (FR-AUTH-00, FR-CONS-01).
