# Phase 95 — Staff soạn, OpsManager duyệt cấu hình nghiệp vụ và tham số dự báo

> Trạng thái: **Phần 2 đã duyệt 2026-11-27: D1–D17 theo đề xuất, kèm S1–S6 (S thắng D và phần thân khi mâu thuẫn).** Đang làm. Nhánh `feat/staff-draft-manager-approve` từ `main` @ `bacfb39` (đã có Phase 94).

Đăng ký đề tài:
- **Staff:** "Maintain system categories, keyword rules, and AI classification rules, and draft economic assumptions and knowledge content for the financial assistant for approval."
- **Operations Manager:** "Approve or reject content, economic assumptions, and forecast parameters drafted by Staff, with every change versioned and recorded in the audit log."

Đọc câu chữ: với **danh mục, từ khoá, luật AI**, Staff được "maintain" (không nói phải qua duyệt). Với **giả định kinh tế, tri thức, tham số dự báo**, Staff soạn và OpsManager duyệt hoặc từ chối, có phiên bản và audit. "Content" ở câu của OpsManager có thể hiểu rộng là cả danh mục, từ khoá, luật — đây là chỗ cần chủ sản phẩm chốt (xem §5).

## 1. Kiểm kê cấu hình và nội dung nghiệp vụ sửa được qua `/admin/*` và `/ops/*`

Ký hiệu: ✅ có, ❌ không, ◐ một phần. "Ai sửa" là thứ đang được kiểm thật: `AdminOnly` = vai trò Admin trong JWT + 2FA; `ManagerOrAbove` = OpsManager/Admin trong JWT + 2FA (không đọc quyền từ DB); `Cap:X` = `IOpsAccessService.RequireAsync(X)` (đọc DB).

| # | Cấu hình / nội dung | Route | Ai sửa hiện nay | Bản nháp | Xem trước | Phiên bản / hoàn tác | Audit | Hiệu lực | Đăng ký giao | Lệch |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Danh mục hệ thống | `POST /admin/categories`, `PATCH /{id}`, `PATCH /{id}/activation` | Admin | ❌ | ❌ | ❌ (không lưu bản cũ; tắt thay xoá) | ✅ `Admin.Category.*` | Ngay + đồng bộ AI cố gắng tối đa, job mỗi giờ bù | Staff "maintain" | **Lệch:** Staff không vào được |
| 2 | Thương hiệu, từ khoá hoá đơn | `PUT /admin/ai-rules/brands/{key}`, `PATCH .../active`, `POST /keywords`, `PATCH /keywords/{id}/active` | Admin | ❌ | ✅ `POST /preview` | ✅ lịch sử trước/sau + revert (ở AI Service) | ✅ `Admin.AiRule.Changed` + lịch sử AI | Ngay | Staff "maintain" (keyword rules) | **Lệch:** Staff không vào được |
| 3 | Bộ nhớ tên món dùng chung | `PUT/DELETE /admin/ai-rules/item-cache` | Admin | ❌ | ✅ (`item.cache_slug` trong preview) | ✅ (AI Service) | ✅ | Ngay, cho mọi người dùng | Staff (AI classification rules) | **Lệch** |
| 4 | Chỉ dẫn phân loại món cho Gemini | `POST /admin/ai-rules/ai-instructions`, `/{id}/activate` | Admin | ◐ (mỗi lần lưu là bản mới, **bật ngay**) | ❌ | ✅ bật lại bản cũ | ✅ | Ngay | Staff (AI classification rules) | **Lệch** |
| 5 | Tài liệu cấu hình AI/OCR (`intent_cues`, `chat_dictionary`, `merchant_lexicon`, `parsing_vocab`, `thresholds`, `app_screens`) | `/admin/ai-config/documents/{name}/drafts`, `/versions/{v}/preview`, `/activate` | Admin | ✅ | ✅ 3/6 tài liệu (bộ câu có đáp án), OCR thì không | ✅ kích hoạt lại bản cũ, 409 `stale_active` | ✅ `Admin.AiConfig.Changed` | Khi kích hoạt (OCR ~30 giây) | Staff (AI classification rules) | **Lệch:** Staff không vào được; người soạn và người kích hoạt có thể là một |
| 6 | Giả định kinh tế | `POST /admin/assumptions`, `/{id}/approve`, `/{id}/retire` | Soạn: Staff+ ; duyệt/gỡ: `ManagerOrAbove` | ✅ | ❌ | ✅ mỗi giá trị một phiên bản, Draft → Approved → Retired | ✅ `Admin.Assumption.*` | Khi duyệt | Staff soạn, OpsManager duyệt | **Gần đúng.** Thiếu: không chặn tự duyệt (người duyệt = người soạn vẫn qua); quyền duyệt theo vai trò JWT, không đọc DB; không có "từ chối kèm ghi chú" (chỉ có gỡ); job CPI tự duyệt khi lệch nhỏ (thiết kế có chủ ý, Phase 73) |
| 7 | Kho tri thức cho trợ lý | `POST /admin/knowledge/documents`, `/{id}/approve`, `/{id}/retire`, `candidates/{id}/resolve` | Soạn: Staff+ ; duyệt/gỡ: `ManagerOrAbove` | ✅ | ❌ | ◐ (AI Service giữ bản, không có hoàn tác) | ✅ `Admin.Knowledge.*` | Khi duyệt | Staff soạn, OpsManager duyệt | **Gần đúng.** Cùng thiếu sót như #6; AI Service không nhận id người soạn nên không thể chặn tự duyệt ở phía đó |
| 8 | **Tham số dự báo** (nhóm `forecast`) | `PUT /admin/system-config`, `POST /rollback/{v}` | Admin | ❌ | ◐ (chỉ kiểm hợp lệ khi lưu) | ✅ phiên bản tuyến tính + hoàn tác, 409 khi lệch phiên bản | ✅ `Admin.SystemConfig.Changed` (tên phần đã đổi) | Ngay (cache 10 giây) | **Staff soạn, OpsManager duyệt** | **Lệch nặng nhất** |
| 9 | Ngưỡng T1/T2 mặc định (`limits`) và các nhóm ảnh hưởng cảnh báo (`refunds`, `duplicates`, `transfers`, `listenerHealth`, `loans`, `notificationPipeline`) | như #8 | Admin | ❌ | ◐ | ✅ | ✅ | Ngay | Không nói rõ; một phần là "forecast parameters" theo nghĩa rộng | Xem §2 |
| 10 | Các nhóm kỹ thuật/vận hành khác của system-config (`maintenance`, `appVersion`, `features`, `sync`, `monitoring`, `economicFeeds`, `marketData`, `community`, `agent`, `snapshots`, `dataExport`, `kpi`…) | như #8 | Admin | ❌ | ◐ | ✅ | ✅ | Ngay | Không giao (Admin) | Không lệch |
| 11 | Phương pháp tài chính, mẫu mục tiêu, gắn mẫu vào phương pháp, câu hỏi khảo sát | `/admin/finance-methods`, `/admin/goal-templates`, `/admin/survey-questions` | Admin | ❌ | ❌ | ◐ (`finance_methods.version` tăng khi sửa, không lưu bản cũ) | ✅ | Ngay | Không nói ("content"?) | Có thể lệch nếu "content" gồm cả phần này |
| 12 | Prompt gợi ý phương pháp | `/admin/finance-method-prompts`, `/{v}/activate` | Admin | ✅ | ❌ | ✅ | ✅ | Khi kích hoạt | Không nói | — |
| 13 | Cấu hình và mẫu thông báo ngân hàng | `/admin/provider-configs`, `/admin/provider-formats` (draft/try/activate/archive) | Admin | ✅ (formats) | ✅ `try` | ✅ (formats) | ✅ | Khi kích hoạt | Không nói (kỹ thuật) | — |
| 14 | Nguồn thị trường, giá xăng, sự kiện thị trường | `/admin/market-sources`, `/fuel-prices`, `/market-events/{id}/approve|reject` | Admin | ❌ | ✅ `test` (nguồn) | ❌ | ✅ | Ngay | Không nói | — |
| 15 | Văn bản pháp lý | `PUT /ops/legal/draft` (`Cap:ManageLegalDocuments`, OpsManager+), `POST /admin/legal/draft/publish` (Admin) | OpsManager soạn, Admin xuất bản | ✅ | ✅ (xem bản nháp) | ✅ | ✅ | Khi xuất bản | Không nói | — |
| 16 | Câu trả lời mẫu | `/ops/canned-replies` | `Cap:ManageCannedReplies` (OpsManager) | ❌ | ❌ | ❌ | ✅ | Ngay | Không nói | — |
| 17 | Gói đăng ký | `/ops/subscriptions/plans` | `Cap:ManageSubscriptionPlans` (OpsManager) | ❌ | ❌ | ❌ (đơn chụp giá lúc tạo) | ✅ `Ops.SubscriptionPlan.*` | Ngay | Không nói | — |
| 18 | Quyền theo vai trò, khoá bí mật, người dùng, xoá dữ liệu | `/admin/role-permissions`, `/admin/secrets`, `/admin/users`, `/admin/data-deletion-requests` | Admin | — | — | — | ✅ | Ngay | Quản trị (Admin) | Ngoài phạm vi |

Ghi chú phát hiện thêm:
- **Lệch thật so với đăng ký:** #1–#5 (Staff không vào được), #8 (không có bước duyệt, Staff không vào được). #6, #7 đúng khuôn nhưng có lỗ: tự duyệt được, quyền duyệt chỉ dựa vào vai trò trong JWT (một OpsManager vừa bị hạ quyền vẫn duyệt được tới 15 phút), và không có "từ chối kèm ghi chú".
- **Audit của system-config bỏ sót phần:** `SystemConfigService.ChangedSections` không so `notificationPipeline`, `categories`, `dataExport`, `kpi`, nên đổi các phần đó thì audit ghi `changedSections: []`. Lỗi nhỏ, sửa được trong phase này.

## 2. Tham số ảnh hưởng tới dự báo và cảnh báo

Phân loại: **N** = nghiệp vụ (đổi trải nghiệm hay độ "nhạy" của dự báo/cảnh báo với người dùng; OpsManager nên duyệt). **K** = kỹ thuật (giới hạn tài nguyên, lưu trữ, hiệu năng, hay chi tiết thuật toán đã đo; để Admin). Khoảng là của `SystemConfigDocumentValidator`.

### 2.1 Nhóm `forecast` (45 tham số)

| Tham số | Mặc định | Khoảng | Dùng ở | Loại |
|---|---|---|---|---|
| `historyWindowDays` | 90 | 30–365 | ForecastInputs, ForecastService, BudgetForecast/BudgetRisk, IncomeForecast, ForecastAlert, ForecastAccuracy, DataCoverageCache, RecurringDetection | **N** |
| `minHistoryDays` | 30 | 1–`historyWindowDays` | ForecastInputs, SpendingBaseline (cờ tin cậy thấp) | **N** |
| `reservePercentOfIncome` | 5 | 0–50 | ForecastService.Reserve, ForecastSettingsService | **N** |
| `maxHorizonDays` | 90 | 7–365 | ForecastService, PurchaseCheckService | **N** |
| `amountTolerancePercent` | 10 | 0–50 | ForecastInputs (khớp khoản định kỳ) | **N** |
| `monthlyMinIntervalDays` / `monthlyMaxIntervalDays` | 25 / 35 | 20 ≤ min ≤ max ≤ 40 | RecurringDetector, RecurringTracker | **N** |
| `weeklyMinIntervalDays` / `weeklyMaxIntervalDays` | 6 / 8 | 5 ≤ min ≤ max ≤ 9 | RecurringDetector | **N** |
| `suggestMinOccurrences` / `autoActivateOccurrences` | 2 / 3 | 2 ≤ suggest ≤ auto ≤ 12 | RecurringDetector, RecurringTracker | **N** |
| `overdueGraceDays` | 7 | 0–31 | CashflowEngine, BillReminderRule, RecurringMatcher, RecurringDetector | **N** |
| `maxScenarioItems` | 20 | 1–50 | ForecastService (`/scenario`) | K |
| `purchaseDefaultHorizonDays` | 14 | 1–90 | PurchaseCheckService | **N** |
| `purchaseStaleBalanceDays` | 7 | 1–60 | PurchaseCheckService | **N** |
| `holidays[]` (`name`, `start`, `end`, `multiplierPercent`) | [] | ≤ 30 khoảng, ≤ 32 ngày, hệ số 50–300% | CashflowEngine, ForecastService | **N** |
| `lowBalanceHorizonDays` | 14 | 3–60 | ForecastAlertService | **N** |
| `lowBalanceCooldownDays` | 3 | 1–30 | ForecastAlertDispatcher | **N** |
| `inaccurateSuppressDays` | 7 | 1–60 | ForecastAlertDispatcher | **N** |
| `budgetRiskMinElapsedDays` | 7 | 1–28 | BudgetForecastService | **N** |
| `budgetRiskMinDaysRemaining` | 3 | 1–28 | ForecastAlertService | **N** |
| `billReminderDaysBefore` | 3 | 0–14 | ForecastAlertService | **N** |
| `incomeLateDays` | 3 | 1–15 | ForecastAlertService, IncomeForecastService | **N** |
| `irregularIncomeMonths` | 6 | 2–12 | ForecastService, IncomeForecastService | **N** |
| `quietHoursStartMinute` / `quietHoursEndMinute` | 1320 / 420 | 0–1439 | ForecastAlertDispatcher, ForecastSettingsService (mặc định khi user chưa đặt) | **N** |
| `anomalyLookbackDays` | 90 | 30–365 | AnomalyService | **N** |
| `anomalyMinCategorySamples` | 5 | 3–50 | AnomalyDetector | **N** |
| `anomalyMinMerchantSamples` | 3 | 2–50 | AnomalyDetector | **N** |
| `anomalyRatioPercent` / `anomalyHighRatioPercent` | 300 / 500 | 150 ≤ ratio ≤ high ≤ 5000 | AnomalyDetector | **N** |
| `anomalyMinAmountCents` | 200 000 | 0–100 000 000 | AnomalyDetector | **N** |
| `duplicateWindowMinutes` | 10 | 1–120 | AnomalyDetector (nghi trùng trong bất thường) | **N** |
| `normalFeedbackTolerancePercent` | 125 | 100–300 | AnomalyDetector | **N** |
| `anomalyScanDays` | 3 | 1–14 | AnomalyService | K |
| `categoryForecastMonths` / `categoryForecastMinMonths` | 3 / 2 | 1 ≤ min ≤ months ≤ 12 | CategoryForecastCalculator | **N** |
| `categoryChangePercent` | 20 | 1–100 | CategoryForecastCalculator | **N** |
| `budgetSuggestionRoundingCents` | 10 000 | 1–1 000 000 | CategoryForecastCalculator | **N** |
| `goalRateMonths` | 6 | 1–24 | GoalForecastService | **N** |
| `goodCoveragePercent` / `minCoveragePercent` | 85 / 60 | 0 ≤ min ≤ good ≤ 100 | DataCoverageService | **N** |

→ 43/45 nghiệp vụ, 2 kỹ thuật (`maxScenarioItems`, `anomalyScanDays`). Thực tế gần như cả nhóm là nghiệp vụ, nên đề xuất duyệt **cả nhóm `forecast`** như một đơn vị, không tách từng trường.

### 2.2 Nhóm khác có ảnh hưởng tới dự báo hoặc cảnh báo

| Nhóm.tham số | Mặc định | Ảnh hưởng | Loại |
|---|---|---|---|
| `limits.alertT1Percent` / `alertT2Percent` | 70 / 90 | Ngưỡng cảnh báo ngân sách mặc định (khi phương pháp và người dùng chưa đặt) | **N** |
| `refunds.matchWindowDays`, `reversalMatchWindowDays`, `maxSuggestions` | 60, 3, 3 | Gợi ý gắn khoản hoàn/đảo → ảnh hưởng số chi tiêu ròng của dự báo | **N** (cửa sổ), K (`maxSuggestions`) |
| `duplicates.notificationWindowMinutes`, `manualWindowHours`, `receiptWindowHours` | 10, 48, 12 | Đánh dấu nghi trùng → chi tiêu bị đếm hai lần nếu bỏ sót | **N** |
| `transfers.pairWindowMinutes`, `clockSkewMinutes`, `maxFeeCents`, `maxFeePercent`, `providerAliases` | 30, 2, 20 000, 2, … | Gợi ý chuyển ví → chi tiêu/thu nhập ảo nếu bỏ sót | **N** (phí, cửa sổ), K (`clockSkewMinutes`, `providerAliases` là dữ liệu kỹ thuật) |
| `notificationPipeline.businessDuplicateWindowMinutes`, `selfTransferMergeWindowMinutes` | 5, 10 | Pipeline thông báo | K |
| `listenerHealth.*` (17 tham số) | — | Nhắc dịch vụ đọc thông báo, độ phủ, sự cố đổi mẫu tin | **N**: `minSilenceHours`, `silenceMultiplier`, `cooldownDays`, `maxRemindersPerEpisode`, `historyDays`, `minNotificationsForBaseline`, `staleReportHours`, `coverageInheritHours`; K: `historyRetentionDays`, `maxDevicesPerUser`, `formatDrift*`, `formatFinancialShareDropRatio` |
| `loans.reminderDaysBefore`, `overdueRepeatDays`, `maxOverdueReminders`, `suggestWindowHours`, `maxSuggestions`, `interestToleranceCents`, `honorifics` | 3, 7, 3, 72, 3, 10 000, … | Nhắc khoản vay, gợi ý gắn, tách lãi → dự báo khoản vay | **N** (nhắc, `interestToleranceCents`), K (`maxSuggestions`, `honorifics`) |
| `financeMethods.supportMinSharePercent` | 50 | Chọn cách bổ trợ khi AI lỗi | **N**; `recommendationReuseMinutes` K |
| `snapshots.retentionDays` | 365 | Lưu trữ | K |

Nhận xét: tách từng trường giữa "Staff/OpsManager" và "Admin" trong cùng một nhóm sẽ làm form và luật kiểm phức tạp. Đề xuất chia theo **nhóm**: `forecast`, `limits` thuộc phạm vi duyệt; các nhóm còn lại là lựa chọn mở rộng (§5).

## 3. Ba khuôn soạn → duyệt / phiên bản đang có

| | Giả định kinh tế (`economic_assumptions`) | `ai-config` (`config_documents` ở AI Service) | `system_config_versions` (backend) |
|---|---|---|---|
| Dữ liệu | Mỗi giá trị một dòng; `key`, `version`, `status`, `created_by`, `approved_by` | Mỗi phiên bản một dòng ở AI Service; `name`, `version`, `status`, `note`, `actor_hash` | Mỗi phiên bản một dòng; `name` (hiện chỉ `app`), `version`, `status`, `content` jsonb, `note`, `created_by` |
| Trạng thái | Draft → Approved → Retired | draft → active → archived | Active / Archived (không có nháp) |
| Ai làm gì | Staff soạn; OpsManager/Admin duyệt hoặc gỡ | Admin lưu nháp, xem trước, kích hoạt | Admin lưu (thành Active ngay) hoặc hoàn tác |
| Chống ghi đè | Không cần (mỗi khoá một bản duyệt) | `expected_active_version` → 409 | `expectedVersion` → 409 qua `TryActivateNextAsync` |
| Diff / xem trước | ❌ | ✅ `diff_vs_active` + cổng đo | ❌ (audit chỉ có tên phần đổi) |
| Tái dùng được cho | Nội dung dạng "một giá trị có nguồn" | Tài liệu cấu hình AI (#5), nhưng dữ liệu nằm ở AI Service | **Tham số dự báo (#8, #9)**: đã có phiên bản, hoàn tác, kiểm hợp lệ, chống ghi đè, cột `name` cho nhiều tài liệu |
| Không tái dùng được cho | Danh mục, luật AI (thao tác từng dòng, không phải "giá trị có nguồn") | Bất cứ thứ gì ở backend | Danh mục, luật AI (không phải một tài liệu JSON) |

Còn một khuôn thứ tư đã có mà đề bài không nhắc: **`ops_approval_requests`** (Phase 64). Nó lưu loại việc, đối tượng, lý do, trạng thái Pending/Approved/Rejected/Failed, người duyệt, hạn 7 ngày và khoá `xmin`. Luật "không tự duyệt" có cả ở service lẫn CHECK trong DB. Lúc duyệt nó kiểm lại đối tượng rồi gọi đúng command handler có sẵn. Đây là khuôn hợp nhất cho các thay đổi **từng dòng** (danh mục, thương hiệu, từ khoá): thêm loại mới và một cột payload jsonb. Như vậy không phải tạo khuôn mới.

Kết luận tái dùng:
- **Tham số dự báo:** mở rộng `system_config_versions` (thêm trạng thái `draft`/`pending`/`rejected`, người gửi, người duyệt, ghi chú duyệt, phiên bản gốc). Duyệt thì dùng lại đúng đường `CommitAsync` hiện có.
- **Danh mục, thương hiệu, từ khoá (nếu chọn duyệt):** mở rộng `ops_approval_requests` với loại mới và payload. Duyệt thì gọi command handler hoặc client AI có sẵn, giống cách `UnlockUser` gọi `SetUserLockCommandHandler`.
- **`ai-config`:** đã có nháp ở AI Service. Chỉ cần tách quyền "lưu nháp" (Staff) khỏi "kích hoạt" (OpsManager). Muốn chặn tự kích hoạt thì backend phải nhớ ai soạn bản nháp, vì AI Service chỉ giữ `actor_hash` và không biết vai trò.

## 4. Phụ thuộc đi kèm

- **Đồng bộ danh mục sang AI Service:** `IAiCategorySyncService.TrySyncAsync` chạy sau mỗi lần tạo, sửa hoặc tắt (cố gắng tối đa); `AiCategorySyncJob` chạy lúc khởi động và mỗi giờ để bù. Nếu danh mục chỉ có hiệu lực khi được duyệt, đồng bộ phải chạy **sau khi duyệt**, không chạy lúc soạn.
- **Slug bất biến:** `UpdateSystemCategoryCommandHandler` từ chối đổi slug (`ImmutableField`). Bản nháp đổi slug cũng phải bị từ chối ngay lúc soạn. AI Service và `MarketSpendProfile` nối theo slug.
- **Luật AI có hiệu lực ngay ở AI Service:** backend không giữ trạng thái luật. Muốn có bước duyệt, bản nháp phải nằm ở **backend** (payload), duyệt xong mới gọi client AI. `POST /preview` của AI Service dùng được cho bước xem trước (nó nhận `proposed_brands`/`proposed_keywords`). Lịch sử và revert ở AI Service vẫn còn nguyên.
- **`ai-config`:** AI Service nạp nóng sau khi kích hoạt (ai-service ngay, ocr-service ~30 giây). `preview_regression` có thể chặn kích hoạt (cần `force`). Không đổi phía AI Service nếu chỉ tách quyền ở backend.
- **Cache system-config:** `SystemConfigProvider` cache 10 giây mỗi máy, `Invalidate()` sau khi lưu. `EconomicFeedScheduler.Apply` chạy lại sau mỗi lần lưu. Duyệt phải đi qua cùng `CommitAsync` để giữ hai việc này.
- **Hai đường cùng ghi một tài liệu:** Admin sửa trực tiếp system-config trong lúc có bản nháp đang chờ duyệt → bản nháp dựa trên phiên bản cũ. Cần quyết khi duyệt: từ chối (409, soạn lại) hay chỉ áp **phần của nhóm đang duyệt** lên bản mới nhất. Đề xuất ở bước 2: chỉ áp phần `forecast`/`limits` và giữ nguyên các nhóm khác.
- **Bảo vệ tài liệu khỏi Staff:** Staff chỉ được đổi phần thuộc phạm vi. Một bản nháp không được mang theo thay đổi ở `maintenance`, `features`… Luật kiểm phải so với bản đang chạy và từ chối nếu phần ngoài phạm vi khác đi.
- **Test đang giả định Admin:** `AdminControllersTests` (thiếu `AdminOnly` → 403), `AdminAiRulesControllerTests`, `AdminAiConfigControllerTests`, `SystemConfigControllerTests` đều kiểm "không phải Admin → 403". Nếu mở route cho Staff/OpsManager thì các test này phải đổi chủ đích. Đề xuất: **giữ nguyên route `/admin/*` là AdminOnly** và thêm route mới dưới `/ops/*` cho Staff/OpsManager, nên các test 403 cũ vẫn đúng.
- **Quyền mới:** `OpsCapability` còn bit 19 trở đi. Thêm quyền mới phải có migration cập nhật các dòng `role_permissions` đã có (như Phase 94). `PUT /admin/role-permissions` cần biết quyền nào phụ thuộc quyền nào (ví dụ "duyệt" không cần "soạn").
- **Agent và web:** web admin gọi `/admin/*` với giả định "lưu là có hiệu lực". Route mới dưới `/ops/*` không làm vỡ màn hình cũ.

## 5. Đề xuất phạm vi

Mọi phương án đều gồm phần **sửa lỗ của khuôn đang có** (#6, #7): chặn tự duyệt (lưu người soạn, người duyệt phải khác), kiểm quyền duyệt bằng `IOpsAccessService` (DB), thêm "từ chối kèm ghi chú" cho giả định kinh tế, và sửa `ChangedSections` bỏ sót phần. Kho tri thức: backend phải tự ghi người soạn, vì AI Service không nhận id.

### Phương án A — Chỉ tham số dự báo (khớp chặt câu chữ đăng ký)
- Bản nháp cho nhóm `forecast` + `limits` (T1/T2) trên `system_config_versions`: Staff soạn, xem trước (kiểm hợp lệ + diff theo trường), gửi duyệt; OpsManager duyệt hoặc từ chối; người duyệt khác người soạn; duyệt xong mới tạo phiên bản Active. Hoàn tác = bản nháp mới. Admin giữ `PUT /admin/system-config`.
- Xem trước: diff + kiểm khoảng hợp lệ; tuỳ chọn chạy thử dự báo trên 2–3 hồ sơ mẫu tổng hợp với tham số cũ/mới. Các bộ tính (`CashflowEngine`, `AnomalyDetector`, `CategoryForecastCalculator`, `RecurringDetector`) đều nhận rules qua tham số nên làm được; tốn thêm khoảng 2 task.
- Quyền mới: `DraftForecastParameters` (Staff), `ApproveForecastParameters` (OpsManager).
- **Ước lượng:** 8–10 task (+2 nếu có chạy thử hồ sơ mẫu). **Rủi ro: thấp.** Không đụng AI Service. Rủi ro chính là xung đột với Admin sửa trực tiếp, đã có cách xử lý ở §4.
- Còn lệch: Staff vẫn không vào được danh mục, từ khoá, luật AI (#1–#5).

### Phương án B — A + danh mục + thương hiệu/từ khoá + tách quyền `ai-config`
- Như A.
- Danh mục, thương hiệu, từ khoá, bộ nhớ tên món: Staff soạn **yêu cầu thay đổi** (mở rộng `ops_approval_requests` với loại mới và payload), xem trước bằng `/preview` của AI Service (với luật) hoặc diff (với danh mục). OpsManager duyệt → gọi command handler / client AI có sẵn → đồng bộ AI sau khi duyệt.
- `ai-config`: route `/ops` cho Staff lưu nháp và xem trước; OpsManager kích hoạt. Backend ghi người soạn để chặn tự kích hoạt.
- Chỉ dẫn Gemini (#4): đổi thành nháp → duyệt (cần sửa AI Service để lưu bản nháp không bật ngay), hoặc để Admin-only. Đề xuất để Admin-only ở phương án này.
- Quyền mới: `DraftBusinessConfig`, `ApproveBusinessConfig` (gộp), hoặc tách theo loại.
- **Ước lượng:** 16–20 task. **Rủi ro: trung bình.** Luật AI có hiệu lực ở dịch vụ khác. Yêu cầu chờ duyệt có thể lỗi thời khi Admin sửa trực tiếp cùng dòng, nên phải kiểm lại lúc duyệt như `ops_approval_requests` đang làm. Phải đổi chủ đích một số test.
- Lưu ý câu chữ: đăng ký nói Staff "maintain" danh mục, từ khoá, luật. Bắt buộc duyệt chặt hơn đăng ký. Biến thể **B′**: Staff sửa trực tiếp các mục này (có audit, có lịch sử AI Service), chỉ tham số dự báo đi qua duyệt. Nhẹ hơn B khoảng 6 task, nhưng mất bước hai người với luật phân loại.

### Phương án C — Toàn bộ nội dung nghiệp vụ
- Như B, cộng: phương pháp tài chính, mẫu mục tiêu, câu hỏi khảo sát (#11), chỉ dẫn Gemini (#4, cần sửa AI Service), các nhóm cấu hình nghiệp vụ khác ở §2.2 (`refunds`, `duplicates`, `transfers`, `listenerHealth`, `loans`, `financeMethods`).
- **Ước lượng:** 26–32 task, đụng cả ai-service. **Rủi ro: cao.** Nhiều màn hình web phải đổi; system-config phải chia quyền theo nhiều nhóm; dễ trượt thời hạn trước buổi bảo vệ.

### Đề xuất
**Phương án A**, cộng phần sửa lỗ của giả định kinh tế và tri thức. Lý do:
- Khớp câu chữ "forecast parameters drafted by Staff … versioned and recorded in the audit log".
- Tái dùng gần trọn `system_config_versions`, không đụng AI Service.
- Đóng được lỗ tự duyệt ở hai khuôn đang có.

Nếu muốn Staff "maintain" danh mục và luật đúng đăng ký mà không tốn nhiều, thêm **B′** (Staff sửa trực tiếp danh mục, từ khoá, thương hiệu qua route `/ops`, có audit). Chủ sản phẩm chọn A, A + B′, B hoặc C.

---

# Phần 2 — Thiết kế cho phạm vi đã chốt: A + B′ (thu hẹp)

## Đã duyệt (2026-11-27): điều chỉnh S1–S6

- **S1 (thay một phần D8/D9): bản chờ duyệt đã cũ.** Bản `pending_approval` bị 409 vì cũ vẫn giữ chỗ chờ. Vì vậy `POST /drafts { fromDraftId }` từ một bản `pending_approval` đã cũ **của chính người soạn** sẽ chuyển bản nguồn sang `cancelled` trong cùng giao dịch, rồi tạo bản mới. Người duyệt cũng được `reject` một bản cũ (từ chối không cần bản mới nhất). Kịch bản (b): M duyệt → 409; S "làm lại" từ bản đang chờ → bản cũ `cancelled`, bản mới gửi duyệt được → M duyệt.
- **S2: xung đột khi làm lại.** Khi áp phần đã đổi lên bản Active mới (D9), tính `conflicts: [{ path, baseValue, activeValue, draftValue }]` cho các trường mà **cả** bản Active lẫn bản nguồn đều đã đổi so với bản gốc. Giá trị của bản nháp thắng (người soạn có chủ ý), không chặn. `conflicts` được lưu trên dòng nháp (cột `conflicts jsonb`), trả khi tạo và trong `GET /drafts/{id}`. Unit test.
- **S3 (thay D16): tài liệu tri thức cũ.** Chỉ coi là "cũ" (ai duyệt cũng được) khi tài liệu được tạo ở AI Service **trước** mốc triển khai. Mốc lấy bằng giờ chạy migration: bảng một dòng `knowledge_review_cutover (id = 1, tracked_since)`. Tài liệu tạo sau mốc mà không có dòng `knowledge_reviews` → 409 `KNOWLEDGE_REVIEW_MISSING`, không cho duyệt.
  - **Thay đổi hợp đồng với AI Service (chỉ thêm trường):** `GET /api/v1/admin/knowledge/documents/{id}` và danh sách trả thêm `created_at`. Cột đã có sẵn trong `agent_kb_documents`.
  - **Bù khi ghi dòng thất bại:** nếu AI Service đã nhận tài liệu mà ghi dòng `knowledge_reviews` thất bại, backend gọi `retire` (Draft → Retired) cho tài liệu đó theo kiểu cố gắng tối đa, ghi log cảnh báo (chỉ id tài liệu), rồi trả lỗi 503 `KNOWLEDGE_SUBMIT_FAILED` để người soạn gửi lại. Nếu `retire` cũng hỏng, tài liệu ở lại Draft nhưng không duyệt được (409), và gỡ tay được bằng `retire`.
  - Test.
- **S4: danh mục được bảo vệ.** Kiểm trong code: `CategoryRepository.GetSystemBySlugAsync` chỉ trả danh mục **đang bật**. Tắt một danh mục mà code gọi theo slug sẽ làm các luồng đó hỏng hoặc rơi về null. Tắt chưa được chặn ở đâu cả (`SetSystemCategoryActivationCommandHandler` không kiểm). Danh sách slug được bảo vệ:
  - `income`: `RefundPolicy`, `RelatedTransactionCommands`, lãi khi cho vay (`LoanTransactionLink.InterestCategoryForLend`), luật quỹ nhóm, `AiCategorySyncService` (kind), `TrainingSampleService`, `ReceiptMemory`;
  - `loan` (`Category.LoanSlug`): `LoanExclusion` (dòng tiền `cashFlow`, báo cáo), `MergeTransactionsCommand`, `ItemMemoryService`, chia hoá đơn nhóm;
  - `other`: phí chuyển ví (`TransferFee.CategorySlug`), lãi khi đi vay (`InterestCategoryForBorrow`), danh mục dự phòng khi người dùng ẩn danh mục (`SetCategoryHiddenCommandHandler.FallbackSlug`).
  
  Chuyển khoản, quỹ nhóm và hoàn tiền không dùng slug riêng: chuyển khoản và quỹ là `TransactionType.Transfer`, hoàn tiền là `relation`. Kênh `/ops` không được **tắt** ba danh mục này → 422 `PROTECTED_CATEGORY` (vẫn đổi tên được). Danh sách nằm ở một chỗ (`ProtectedCategories.Slugs`, có test gắn với các hằng ở trên). Đường `/admin` hiện cũng không chặn; giữ nguyên theo phạm vi và ghi ở REQUIREMENTS §13.4.
- **S5: quyền của Admin.** Kiểm trong code: Admin luôn nhận `RolePermissionRules.All` (tính từ enum), không đọc dòng `role_permissions`. Vì vậy migration **không** cần thêm bit cho Admin; 4 quyền mới có tự động. Có test: Admin duyệt được bản của người khác (tham số dự báo, giả định kinh tế, tri thức).
- **S6: `/meta` khớp validator.** Test phản chiếu: mọi thuộc tính của `ForecastSettings` và `LimitSettings` có trong `/meta`. Với mỗi trường số có `min`/`max`: đặt đúng biên thì qua `SystemConfigDocumentValidator`; vượt biên một đơn vị thì bị từ chối (với cặp có ràng buộc chéo như T1 < T2, các trường còn lại giữ mặc định).
- **Kịch bản (a):** user mẫu không tự đặt ngưỡng T1/T2 và không có phương pháp tài chính đặt ngưỡng, để 75% của hệ thống có hiệu lực.
- **Tài liệu:** sau khi duyệt, tham số có hiệu lực trong vòng 10 giây (cache theo từng máy).
- **Mốc test:** `bacfb39` = cây của `f8332fd` (merge không đổi gì), mốc 4093/4095. Hai test đỏ có sẵn (`MethodSelectionOnSeedDataTests…CashFlowBudget`, `NotificationsControllerTests.Analyze_HighConfidence…`) không tính là hồi quy.

Phạm vi chủ sản phẩm chốt ngày 2026-11-27:
- **A:** tham số dự báo (`forecast` + `limits`, tức ngưỡng T1/T2 mặc định) đi qua soạn → duyệt.
- **B′ thu hẹp:** Staff duy trì trực tiếp danh mục hệ thống, thương hiệu, từ khoá, không qua duyệt.
- **Sửa ba lỗ** của giả định kinh tế và kho tri thức.
- **Sửa audit** của system-config.

Ngoài phạm vi (ghi REQUIREMENTS §13.4 là hướng mở rộng):
- các nhóm `refunds`, `duplicates`, `transfers`, `listenerHealth`, `loans`;
- bộ nhớ tên món, chỉ dẫn Gemini, `ai-config`;
- chạy thử dự báo trên hồ sơ mẫu (để phase backtest).

`LimitSettings` hiện chỉ có `alertT1Percent` và `alertT2Percent`, nên "T1/T2 trong `limits`" chính là **cả nhóm `limits`**. Phạm vi tham số dự báo vì vậy là hai nhóm trọn vẹn: `forecast` và `limits`.

## 6. Tham số dự báo: bản nháp trên `system_config_versions`

### 6.1 Dữ liệu (migration `AddForecastConfigDrafts`)

Thêm cột vào `system_config_versions` (không tạo bảng mới):

| Cột | Kiểu | Ý nghĩa |
|---|---|---|
| `version` | int **NULL** (trước NOT NULL) | Chỉ dòng `active`/`archived` có số. Bản nháp chưa có số, nên không chiếm số phiên bản và không lọt vào lịch sử. Unique `(name, version)` vẫn đúng vì Postgres cho nhiều NULL |
| `scope` | varchar(24) NULL | `forecast` cho bản nháp của phase này; NULL cho dòng của đường Admin. Chừa chỗ cho phạm vi khác sau này |
| `base_version` | int NULL | Phiên bản Active lúc tạo bản nháp |
| `submitted_at` | timestamptz NULL | Lúc gửi duyệt |
| `decided_by` | uuid NULL | Người duyệt hoặc từ chối |
| `decided_at` | timestamptz NULL | |
| `decision_note` | text NULL | Ghi chú duyệt / lý do từ chối |
| `updated_at` | timestamptz NULL | Lần sửa bản nháp gần nhất; dùng làm khoá đồng thời của bản nháp (D6) |

- `status`: thêm `draft`, `pending_approval`, `rejected`, `cancelled` (D3).
- CHECK mới: `(status IN ('active','archived')) = (version IS NOT NULL)`; `(scope IS NULL) OR status <> 'active' OR decided_by IS NOT NULL`; `decided_by IS NULL OR decided_by <> created_by` (chặn tự duyệt ở DB, giống `ops_approval_requests`).
- Unique một phần `(name, scope) WHERE status = 'pending_approval'` để mỗi lúc chỉ có một yêu cầu chờ duyệt (yêu cầu 4).
- Dòng cũ: `scope`, `base_version`, `decided_*` là NULL, nên không đổi nghĩa.

`Approved` = dòng bản nháp **chuyển thành** `active`, không tạo dòng khác (D2):
- nội dung được ghi lại thành toàn bộ tài liệu (bản Active hiện tại, thay hai nhóm `forecast` và `limits`);
- `version` = Active + 1;
- `created_by` vẫn là người soạn, `decided_by` là người duyệt.

Lịch sử phiên bản (`GET /admin/system-config/versions`) vì vậy cho thấy cả người soạn lẫn người duyệt.

### 6.2 Nội dung bản nháp

- Bản nháp lưu **trọn hai nhóm** `{ "forecast": {…}, "limits": {…} }`, không lưu cả tài liệu (D4). Nhờ vậy:
  - "chỉ chứa `forecast` và T1/T2" được bảo đảm ngay ở dạng lưu;
  - bản nháp không thể mang thay đổi ở `maintenance`, `features`… lên runtime.
- Khi tạo hoặc sửa, Staff gửi `changes`, là **phần thay đổi** theo kiểu JSON merge patch (RFC 7396) áp lên hai nhóm của bản gốc (D5):
  - gửi `{ "forecast": { "lowBalanceHorizonDays": 21 } }` là đủ;
  - trường không gửi giữ giá trị gốc;
  - mảng (`holidays`) thay nguyên mảng.
  
  Backend lưu kết quả đã gộp (trọn hai nhóm), nên bản nháp tự đầy đủ.
- **Kiểm "chỉ hai nhóm" (yêu cầu 3):** đọc thân bằng `JsonElement`. Khoá cấp một của `changes` ngoài `forecast`, `limits` → **400** `FORECAST_CONFIG_OUT_OF_SCOPE`. Trường lạ bên trong hai nhóm (gõ sai tên) cũng 400, vì System.Text.Json mặc định bỏ qua trường lạ, nên sai chính tả sẽ âm thầm không đổi gì.
- **Kiểm hợp lệ:** dựng tài liệu đầy đủ = bản Active + hai nhóm của bản nháp, chạy `SystemConfigDocumentValidator`, để giữ các luật chéo trường (`minHistoryDays ≤ historyWindowDays`, T1 < T2…). Lỗi → 400 như `PUT /admin/system-config`. Chạy khi tạo, khi sửa, khi gửi duyệt và lúc duyệt.

### 6.3 Trạng thái và luồng

```
draft ──submit──▶ pending_approval ──approve──▶ active (rồi archived khi có bản mới)
  │  ▲                 │
  │  └───withdraw──────┤
  │                    └──reject──▶ rejected
  └──cancel──▶ cancelled
```

| Thao tác | Ai | Điều kiện | Kết quả |
|---|---|---|---|
| Tạo nháp | `DraftForecastConfig` | `reason` ≥ 3 ký tự; `changes` hợp lệ | `draft`, `base_version` = Active hiện tại |
| Sửa nháp | Người soạn | `draft` | Gộp `changes` lên nội dung **đang có của bản nháp**; `base_version` không đổi |
| Gửi duyệt | Người soạn | `draft`, không cũ (D7), hợp lệ, không có yêu cầu chờ duyệt khác | `pending_approval`, `submitted_at`. Đã có yêu cầu chờ → **409** `FORECAST_CONFIG_PENDING_EXISTS`, kèm `pendingId` (D8) |
| Rút lại | Người soạn | `pending_approval` | Về `draft` |
| Huỷ | Người soạn | `draft` | `cancelled` (không xoá vật lý) |
| Duyệt | `ApproveForecastConfig`, khác người soạn | `pending_approval`; `base_version` = Active hiện tại (yêu cầu 1); hợp lệ | Thành `active` (§6.1) qua cùng đường với `CommitAsync`: `Invalidate()` cache, ghi audit. Bản cũ → **409** `FORECAST_CONFIG_DRAFT_STALE`, không gộp, bản nháp vẫn `pending_approval` |
| Từ chối | `ApproveForecastConfig`, khác người soạn | `pending_approval`; `note` ≥ 3 ký tự | `rejected`, kết thúc |
| Làm lại từ bản cũ | `DraftForecastConfig` | Nguồn ở `rejected`, `cancelled` hoặc nháp đã cũ | Bản nháp mới (D9) |

Quyền đọc từ DB mỗi lần qua `IOpsAccessService` (bị hạ quyền thì mất quyền ngay). Người duyệt khác người soạn được kiểm cả ở service (403 `FORECAST_CONFIG_OWN_DRAFT`) lẫn CHECK trong DB. Admin có mọi quyền nên soạn và duyệt được, nhưng không duyệt được bản của chính mình.

**Hoàn tác** = tạo một bản nháp mới có giá trị cũ. Màn hình có thể điền sẵn từ một phiên bản lịch sử: `POST drafts` với `fromVersion` lấy hai nhóm của phiên bản đó làm `changes` (D9).

### 6.4 API: `/api/v1/ops/forecast-config`

Không dùng `/ops/forecast`: route đó đã thuộc chất lượng dự báo (Phase 84/88).

| API | Việc | Quyền |
|---|---|---|
| `GET /` | Hai nhóm đang chạy + `version` + `pendingId` (nếu có) | Draft hoặc Approve |
| `GET /meta` | Mỗi trường: kiểu, mặc định, khoảng hợp lệ, mô tả tiếng Việt (sinh từ một bảng mô tả, không đọc validator lúc chạy) | Draft hoặc Approve |
| `GET /drafts?status=&offset=&limit=` | Danh sách bản nháp, mới trước | Draft hoặc Approve |
| `POST /drafts` | `{ changes?, fromDraftId?, fromVersion?, reason }` → **201** | Draft |
| `GET /drafts/{id}` | Nội dung, `diff` theo trường so với **bản đang chạy**, `validation`, `stale`, trạng thái, người soạn/duyệt, ghi chú | Draft hoặc Approve |
| `PATCH /drafts/{id}` | `{ changes, reason?, expectedUpdatedAt }` | Người soạn |
| `POST /drafts/{id}/submit` · `/withdraw` · `/cancel` | Như §6.3 | Người soạn |
| `POST /drafts/{id}/approve` | `{ note? }` → phiên bản Active mới | Approve, khác người soạn |
| `POST /drafts/{id}/reject` | `{ note }` | Approve, khác người soạn |

- `diff`: `[{ path: "forecast.lowBalanceHorizonDays", before: 14, after: 21 }]`. So sánh từng trường lá; mảng `holidays` so cả mảng, thành một mục.
- Mỗi thao tác ghi `audit_logs` với sự kiện `Ops.ForecastConfig.{DraftCreated, DraftUpdated, Submitted, Withdrawn, Cancelled, Approved, Rejected}`: id bản nháp, `baseVersion`, phiên bản mới (khi duyệt), danh sách `path` đã đổi.
  - **Không** ghi giá trị và ghi chú vào audit; chúng nằm ở dòng bản nháp, giống quy ước của `Admin.SystemConfig.Changed`.
  - Duyệt ghi thêm `Admin.SystemConfig.Changed` (action `approve`, `ChangedSections`) để lịch sử system-config đọc một chỗ là đủ.

### 6.5 Runtime chỉ đọc bản Active (yêu cầu 2)

`SystemConfigProvider` và `SystemConfigService.GetActiveAsync` đọc `status = 'active'`. `ListVersionsAsync` và `GetVersionAsync` đổi sang lọc `version IS NOT NULL`, để bản nháp không hiện trong lịch sử. Test:
- (a) có bản nháp và bản chờ duyệt với `lowBalanceHorizonDays` khác → `ISystemConfigProvider` và một dự báo thật vẫn dùng giá trị cũ;
- (b) duyệt xong thì giá trị mới có hiệu lực (sau `Invalidate()`);
- (c) lịch sử Admin không thấy bản nháp.

### 6.6 Đường của Admin

Giữ nguyên `PUT /admin/system-config` và `rollback`. Lưu trực tiếp của Admin tăng `version` Active, nên mọi bản nháp đang có thành **cũ** (đúng yêu cầu 1: chặn, không gộp). Màn hình duyệt thấy `stale: true` và người soạn bấm "làm lại" (D9).

## 7. B′ thu hẹp: Staff duy trì danh mục, thương hiệu, từ khoá

Route mới dưới `/api/v1/ops/catalog`, kiểm quyền `MaintainCatalog` (D11) bằng `IOpsAccessService`, rồi gọi **đúng** handler/service có sẵn:

| API | Gọi tới | Ghi chú |
|---|---|---|
| `GET /categories`, `GET /categories/{id}` | query danh mục hệ thống của admin | đọc |
| `POST /categories` | `CreateSystemCategoryCommandHandler` | slug đặt một lần lúc tạo |
| `PATCH /categories/{id}` | `UpdateSystemCategoryCommandHandler` | gửi `slug` khác → 422 `IMMUTABLE_FIELD` (đã có) |
| `PATCH /categories/{id}/activation` | `SetSystemCategoryActivationCommandHandler` | "ẩn" = tắt; không có xoá |
| `GET /ai-rules/meta`, `/brands`, `/keywords`, `POST /ai-rules/preview` | `IAdminAiRulesService.ReadAsync` | đọc, xem trước |
| `GET /ai-rules/history?resource=brand\|keyword` | `ReadAsync` | chỉ hai loại này (D12) |
| `PUT /ai-rules/brands/{key}`, `PATCH /ai-rules/brands/{key}/active` | `IAdminAiRulesService.WriteAsync` | |
| `POST /ai-rules/keywords`, `PATCH /ai-rules/keywords/{id}/active` | `WriteAsync` | |

- Đồng bộ AI (`TrySyncAsync` sau mỗi lần ghi, job mỗi giờ) và cache đi theo handler, nên không viết lại.
- Không có revert, item-cache, ai-instructions, `ai-config` dưới `/ops`.
- **Audit:** cùng handler nhưng sự kiện mang tiền tố theo kênh (D13). Từ `/ops` ghi `Ops.Catalog.CategoryCreated|CategoryUpdated|CategoryActivationChanged`, `Ops.Catalog.AiRuleChanged`, để Manager đọc được ở `/ops/audit-logs` (chỉ hiện `Ops.*`). Từ `/admin` giữ `Admin.*` như cũ. Dòng ở AI Service vẫn có `actor_hash` và `reason`.
- `reason` được khuyến khích; không bắt buộc, giống `/admin`.

## 8. Sửa ba lỗ của giả định kinh tế và kho tri thức

**Giả định kinh tế** (migration `AddAssumptionRejection`):
- Thêm trạng thái `rejected`, cột `decided_note`, `rejected_by`, `rejected_at`. CHECK `approved_by IS NULL OR approved_by <> created_by OR created_by = '<SystemActor>'` (D14).
- `ApproveAsync` / `RetireAsync` / `RejectAsync` mới gọi `IOpsAccessService.RequireAsync(actor, ApproveAgentContent)` (D15). Approve/Reject chặn `actor == CreatedBy` → 403 `ASSUMPTION_OWN_DRAFT`, **trừ** khi người tạo là tài khoản hệ thống của job CPI (tự duyệt có chủ ý, Phase 73).
- `POST /admin/assumptions/{id}/reject` `{ note }` (`ManagerOrAbove`): chỉ bản `draft`; `note` ≥ 3 ký tự; audit `Admin.Assumption.Rejected` (không ghi ghi chú).

**Kho tri thức** (bảng mới `knowledge_reviews`, vì AI Service không nhận id người soạn):
- `document_id` PK (không FK, tài liệu ở AI Service), `submitted_by`, `submitted_at`, `status` (`draft|approved|rejected|retired`), `decided_by`, `decided_at`, `decision_note`.
- `SubmitAsync` ghi một dòng sau khi AI Service nhận tài liệu.
- Approve: kiểm quyền DB, chặn người soạn → gọi AI Service → cập nhật dòng.
- Reject mới `POST /admin/knowledge/documents/{id}/reject` `{ note }`: AI Service chuyển `Draft → Retired` (đã có trong `TRANSITIONS`), backend ghi `rejected` kèm ghi chú.
- Tài liệu soạn trước phase này không có dòng: được duyệt bởi bất kỳ ai có quyền (không biết người soạn), audit ghi `submittedBy: null` (D16).
- `GET /admin/knowledge/documents` ghép thêm `submittedBy`, `reviewStatus`, `decisionNote` từ bảng này.

## 9. Sửa audit system-config

`SystemConfigService.ChangedSections` so thêm `notificationPipeline`, `categories`, `dataExport`, `kpi`. Thêm test phản chiếu (reflection) để mọi thuộc tính của `SystemConfigDocument` phải có trong danh sách, không bỏ sót nhóm mới về sau.

## 10. Quyền

| Quyền (bit) | Staff | OpsManager | Admin | Ghi chú |
|---|---|---|---|---|
| `MaintainCatalog` (19) | ✅ | ✅ | ✅ | B′ |
| `DraftForecastConfig` (20) | ✅ | ✅ | ✅ | A, soạn |
| `ApproveForecastConfig` (21) | ❌ | ✅ | ✅ | A, duyệt |
| `ApproveAgentContent` (22) | ❌ | ✅ | ✅ | Duyệt/từ chối/gỡ giả định kinh tế và tri thức (D15) |

- Migration cập nhật dòng `role_permissions` đã có: Staff (role 2) `| 2^19 | 2^20`; OpsManager (role 3) `| 2^19 | 2^20 | 2^21 | 2^22`. Down gỡ đúng các bit đó.
- Không có quyền phụ thuộc (duyệt không cần soạn).
- Admin chỉnh được qua `/admin/role-permissions` như mọi quyền.

## 11. Cần quyết (mỗi mục có đề xuất)

- **D1 — Dùng lại bảng hay bảng riêng cho bản nháp.** Đề xuất: dùng lại `system_config_versions` như chủ sản phẩm chọn, thêm cột ở §6.1. Phương án khác (bảng `system_config_drafts`) sạch hơn về ràng buộc nhưng là khuôn thứ năm.
- **D2 — "Duyệt" là chuyển chính dòng nháp thành Active hay tạo dòng mới.** Đề xuất: chuyển chính dòng đó (ghi lại `content` thành toàn bộ tài liệu, gán `version`). Lịch sử có một dòng cho mỗi thay đổi, đủ người soạn và người duyệt. Tạo dòng mới thì phải nối hai dòng bằng một cột nữa.
- **D3 — Thêm trạng thái `cancelled`.** Chủ sản phẩm liệt kê Draft / PendingApproval / Rejected. Đề xuất thêm `cancelled` để người soạn bỏ bản nháp của mình mà không xoá vật lý (AGENTS §3.3) và không lẫn với "bị từ chối".
- **D4 — Bản nháp lưu gì.** Đề xuất: chỉ hai nhóm `forecast` + `limits`, đã gộp đầy đủ. Lưu cả tài liệu thì bản nháp có thể mang theo thay đổi ngoài phạm vi.
- **D5 — Thân yêu cầu là phần thay đổi hay trọn nhóm.** Đề xuất: merge patch (chỉ gửi trường đổi), trường lạ → 400. Gửi trọn nhóm thì quên một trường là trường đó âm thầm về mặc định.
- **D6 — Hai người cùng sửa một bản nháp.** Chỉ người soạn sửa được, nên hiếm. Đề xuất: `PATCH` kèm `expectedUpdatedAt`, lệch → 409 `FORECAST_CONFIG_DRAFT_CHANGED`. Không thêm `xmin`.
- **D7 — "Cũ" nghĩa là gì.** Theo yêu cầu: `base_version ≠ version Active`, kể cả khi Admin chỉ đổi một nhóm khác (ví dụ bật bảo trì). Đề xuất làm đúng như vậy. Phương án nới: chỉ coi là cũ khi `forecast`/`limits` của bản Active khác bản gốc. Ít phiền hơn, nhưng là một dạng "tự gộp" nên không đề xuất. Gửi duyệt cũng chặn bản cũ (422 `FORECAST_CONFIG_DRAFT_STALE`), để người duyệt không nhận bản đã biết là cũ.
- **D8 — Đã có yêu cầu chờ duyệt.** Đề xuất: gửi duyệt bản thứ hai → 409 `FORECAST_CONFIG_PENDING_EXISTS` kèm `pendingId`; tạo và sửa nháp vẫn được (nhiều bản nháp song song). Phương án khác (bản mới tự thay bản chờ) dễ làm mất bản đang có người xem nên không đề xuất. Chặn ở cả service lẫn unique index một phần.
- **D9 — "Staff sửa sau khi bị từ chối" và "làm lại" bản cũ.** Đề xuất: `rejected` là trạng thái cuối. "Sửa" = `POST /drafts { fromDraftId }`: lấy **phần đã đổi** của bản nguồn (so với phiên bản gốc của nó) áp lên bản Active hiện tại, tạo bản nháp mới có `base_version` mới. Cùng cách cho bản nháp cũ. `fromVersion` lấy nguyên hai nhóm của một phiên bản lịch sử để hoàn tác. Phương án khác (mở lại `rejected` → `draft`) làm mất dấu lần từ chối trên dòng.
- **D10 — Báo cho người duyệt.** Đề xuất: không gửi email trong phase này. Màn hình `/ops/forecast-config/drafts?status=pending_approval` là hàng chờ. Có thể thêm số đếm vào `/ops/dashboard` sau.
- **D11 — Một quyền hay ba quyền cho B′.** Đề xuất: một quyền `MaintainCatalog` cho cả danh mục, thương hiệu, từ khoá (cùng một màn hình "phân loại"). Tách quyền khi có nhu cầu.
- **D12 — Lịch sử và hoàn tác luật dưới `/ops`.** Đề xuất: cho đọc lịch sử của thương hiệu và từ khoá; **không** cho `revert` (AI Service revert theo id mục lịch sử, mở cả mục của bộ nhớ tên món và chỉ dẫn Gemini). Staff hoàn tác bằng cách sửa hoặc tắt lại.
- **D13 — Sự kiện audit theo kênh.** Đề xuất: handler nhận thêm tham số tuỳ chọn `AuditChannel` (`Admin` mặc định, `Ops`) để chọn tên sự kiện. `/ops/audit-logs` chỉ hiện `Ops.*`, nên ghi `Admin.*` cho thao tác của Staff thì Manager không thấy. Phương án khác: ghi cả hai sự kiện, gây trùng nhật ký.
- **D14 — Tự duyệt của job CPI.** `EconomicAssumptionRefresher` tạo và tự duyệt bằng `SystemActorId` khi lệch nhỏ (Phase 73, có chủ ý). Đề xuất: luật "người duyệt ≠ người soạn" miễn trừ đúng tài khoản hệ thống này, ở cả service và CHECK.
- **D15 — Quyền duyệt nội dung agent.** Hiện là policy `ManagerOrAbove` (vai trò trong JWT). Đề xuất: giữ policy (lọc sớm) **và** thêm `ApproveAgentContent` đọc từ DB. Phương án khác: dùng lại `DecideApprovals`, nhưng quyền đó là của duyệt việc nhạy cảm trên tài khoản khách; gộp lại thì không tách được.
- **D16 — Tài liệu tri thức soạn trước phase này.** Không biết người soạn. Đề xuất: cho duyệt và ghi `submittedBy: null`. Phương án khác (bắt soạn lại) làm mất bản nháp đang chờ.
- **D17 — Staff thêm danh mục hệ thống mới.** Danh mục mới hiện cho **mọi** người dùng và được đồng bộ sang AI Service. Đề xuất: cho phép như đăng ký ("maintain"), có audit `Ops.Catalog.*`. Phương án chặt hơn (Staff chỉ sửa và ẩn, không tạo) lệch đăng ký.

## 12. Các bước (TDD, một commit mỗi task)

0. Kế hoạch được duyệt; mốc test trên `bacfb39`.
1. Quyền mới (4 bit) + migration cập nhật `role_permissions` + test migration và test ma trận quyền.
2. Sửa `ChangedSections` + test phản chiếu (§9).
3. Migration `AddForecastConfigDrafts` (§6.1) + entity + repository: tạo / sửa / chuyển trạng thái nháp; `TryActivateDraftAsync` (khoá dòng Active, kiểm `base_version`, gán `version`, archive bản cũ trong một giao dịch); lọc lịch sử. Test DB: CHECK tự duyệt, unique chờ duyệt, nháp không có số phiên bản.
4. Phần thuần: merge patch hai nhóm + kiểm "chỉ hai nhóm" + trường lạ, diff theo trường, áp phần đổi lên bản mới (D9). Unit test.
5. `ForecastConfigDraftService` (§6.3) + audit + `Invalidate()`. Unit test cho mọi chuyển trạng thái, người duyệt ≠ người soạn, bản cũ, chờ duyệt trùng.
6. `OpsForecastConfigController` + `/meta`. Integration: quyền (Staff soạn được, không duyệt được; OpsManager duyệt được; user thường 403; chưa 2FA 403), 400 ngoài phạm vi, 409 bản cũ, 409 chờ duyệt trùng, runtime chỉ đọc Active (§6.5).
7. B′: `OpsCatalogController` + tham số `AuditChannel` cho ba handler danh mục và `AdminAiRulesService`. Integration: Staff thêm, sửa, ẩn danh mục và thương hiệu/từ khoá; đổi slug 422; tắt quyền qua `/admin/role-permissions` → 403; route `/admin/*` vẫn 403 với Staff (test cũ không đổi); audit `Ops.Catalog.*` hiện ở `/ops/audit-logs`.
8. Giả định kinh tế: `rejected`, chặn tự duyệt (miễn trừ job CPI), kiểm quyền DB, `POST reject`. Test, kể cả job CPI vẫn tự duyệt.
9. Kho tri thức: `knowledge_reviews`, chặn tự duyệt, kiểm quyền DB, `POST reject`, ghép trạng thái duyệt vào danh sách. Test (AI Service giả).
10. Kịch bản đầu-cuối (§13).
11. Tài liệu: REQUIREMENTS (FR-ADM/FR-OPS mới, FR-FCST tham số, §13.4 hướng mở rộng), `docs/ops-api.md` (bảng quyền, mục "Tham số dự báo", mục "Danh mục và luật phân loại"), `docs/ai-rules-admin-api.md` (lối `/ops`), ERD, `.context/TASKS.md` kèm "Báo nhóm web admin", CLAUDE.md.
12. Toàn bộ test, so với mốc.

## 13. Kịch bản đầu-cuối (bắt buộc pass)

- **(a) Soạn → từ chối → sửa → duyệt:**
  1. Staff S tạo nháp `forecast.lowBalanceHorizonDays = 21` kèm lý do, xem `diff` (14 → 21), gửi duyệt.
  2. OpsManager M từ chối kèm ghi chú. Dự báo vẫn dùng 14.
  3. S tạo bản mới `fromDraftId` và đổi thêm `limits.alertT1Percent = 75`, gửi duyệt.
  4. M duyệt → phiên bản Active mới, `created_by = S`, `decided_by = M`. `GET /forecast/cashflow` của một user mẫu dùng cửa sổ 21 ngày cho cảnh báo số dư thấp, và ngân sách mới báo T1 ở 75%. Audit có đủ `Ops.ForecastConfig.*` và `Admin.SystemConfig.Changed`.
- **(b) Bản nháp cũ bị chặn:** S gửi duyệt; Admin lưu trực tiếp `PUT /admin/system-config` (bật bảo trì); M duyệt → 409 `FORECAST_CONFIG_DRAFT_STALE`, bản Active không đổi, bản nháp vẫn chờ. S "làm lại" (`fromDraftId`) → M duyệt được, và cờ bảo trì Admin vừa bật vẫn còn.
- **(c) Không tự duyệt:** M soạn rồi tự duyệt → 403; Admin duyệt bản của M → được.
- **(d) Ngoài phạm vi:** `changes` có `maintenance` hoặc `refunds` → 400; trường gõ sai trong `forecast` → 400.
- **(e) Một yêu cầu chờ:** hai Staff cùng gửi duyệt → người sau 409 kèm `pendingId`.
- **(f) B′:** Staff thêm danh mục, sửa tên, ẩn → đồng bộ AI được gọi (client giả); đổi slug 422; thêm từ khoá và tắt thương hiệu; Admin tắt `MaintainCatalog` của Staff → 403.
- **(g) Ba lỗ:** OpsManager tự duyệt giả định của mình → 403; OpsManager bị Admin gỡ `ApproveAgentContent` → 403 ngay với cùng token; từ chối kèm ghi chú → `rejected`. Tương tự với tài liệu tri thức.

## 14. Báo nhóm web admin (bản nháp)

- Màn hình mới "Tham số dự báo" (`/ops/forecast-config`):
  - form dựng từ `/meta`, gửi `changes` chỉ gồm trường đổi;
  - xem `diff` và `validation` trước khi gửi duyệt;
  - hàng chờ duyệt cho OpsManager; hiện `stale` và nút "Làm lại";
  - từ chối bắt buộc ghi chú.
  
  Admin vẫn sửa trực tiếp ở trang cấu hình hệ thống như cũ, và mỗi lần lưu làm mọi bản nháp đang có thành cũ.
- Màn hình "Danh mục và luật phân loại" cho Staff dùng `/ops/catalog/*`: không có xoá, không đổi slug, không có hoàn tác, bộ nhớ tên món, chỉ dẫn Gemini. Admin dùng trang `/admin/*` như cũ.
- Giả định kinh tế và tri thức có thêm nút "Từ chối" (bắt buộc ghi chú) và trạng thái `rejected`. Người soạn không thấy nút duyệt bản của mình (server cũng chặn, 403).
- Quyền mới trong `/ops/me` (`capabilities`): `MaintainCatalog`, `DraftForecastConfig`, `ApproveForecastConfig`, `ApproveAgentContent`.
