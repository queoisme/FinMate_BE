Controllers dưới `/api/v1/admin/*`. Hầu hết kế thừa `AdminControllerBase`, nơi duy nhất gắn policy `AdminOnly`: không gắn lại trên từng controller, và controller mới không kế thừa lớp này sẽ rơi về "mọi người đã đăng nhập" mà vẫn trả 200. Ngoại lệ: `AdminAssumptionsController` và `AdminKnowledgeController` kế thừa `AgentContentControllerBase` (Staff trở lên soạn nháp, duyệt cần `ManagerOrAbove`).

| Nhóm | Controller | Tài liệu |
|---|---|---|
| Người dùng và quyền | `AdminUsersController`, `AdminRolePermissionsController`, `AdminDataDeletionController`, `AdminAuditLogsController` | `docs/ops-api.md` |
| Cấu hình hệ thống | `AdminSystemConfigController`, `AdminSecretsController`, `AdminMonitoringController`, `AdminLegalController` | `docs/ops-api.md` |
| Ngân hàng và danh mục | `AdminProviderConfigsController`, `AdminProviderFormatsController`, `AdminCategoriesController` | Swagger; đồng bộ danh mục sang AI ở `docs/ai-categories-sync-api.md` |
| AI | `AdminAiRulesController`, `AdminAiConfigController`, `AdminAiLearningController`, `AdminAIStatsController`; `ai-rules` và `ai-config` chuyển tiếp nguyên văn tới API quản trị của ai-service | `docs/ai-rules-admin-api.md`, `docs/ai-config-admin-api.md`, `docs/learning-loop.md` |
| Phương pháp tài chính | `AdminFinanceMethodsController`, `AdminFinanceMethodPromptsController`, `AdminSurveyQuestionsController`, `AdminGoalTemplatesController` | `docs/ai-finance-method-recommend-api.md` |
| Trợ lý và kinh tế | `AdminAssumptionsController`, `AdminKnowledgeController`, `AdminEconomicFeedsController` | `docs/agent-api.md`, `docs/agent-content-checklist.md` |
| Thông tin thị trường | `AdminMarketDataController` | `docs/market-data-api.md` |

Không xoá cứng: tắt bằng `is_active`. Mọi thao tác ghi đều vào `audit_logs` qua hằng số `AuditEvents`.
