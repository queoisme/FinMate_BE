Controllers dưới `/api/v1/ops/*` cho `Staff`, `OpsManager`, `Admin` (web vận hành). Tất cả kế thừa `OpsControllerBase`, nơi gắn policy `StaffOrAbove`; quyền chi tiết theo capability đọc lại từ DB mỗi request (`IOpsAccessService`), không tin vai trò trong token. Policy nội bộ mới phải kèm `OpsVerifiedRequirement` (xác thực 2 lớp), nếu không chỉ cần mật khẩu là vào.

Dữ liệu tài chính của khách chỉ đi qua `IOpsCustomerDataService` (khách hàng lấy từ ticket, người gọi phải là người được giao và có grant còn hiệu lực, kể cả Admin). Thao tác nhạy cảm cần hai người qua `IOpsApprovalService`.

API, ma trận quyền và quy trình: `docs/ops-api.md`, `docs/ops-runbook.md`.
