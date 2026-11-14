# Phase 84 — Sức khoẻ dịch vụ đọc thông báo và độ phủ dữ liệu

> Trạng thái: **đã duyệt** (2026-11-14). D1–D20 theo đề xuất, kèm S1–S7 bên dưới (S thắng D khi mâu thuẫn). Nhánh `feat/listener-health` tạo từ `main` @ `d589097` (đã có Phase 81–83, PR #30–#32).

## Context

Flow 1 dựa hoàn toàn vào `NotificationListenerService` trên điện thoại. Service này chết lặng lẽ: hãng máy diệt app chạy nền, user tắt quyền Notification Access hoặc tắt thông báo của app ngân hàng, hay ngân hàng đổi mẫu tin khiến thông báo vẫn tới nhưng không đọc được. User vẫn tưởng app ghi đủ, nên báo cáo thấp hơn thực tế, ngân sách trông "còn nhiều" và dự báo sai mà không ai biết.

Phía server hiện chưa có gì cho chuyện này. `REQUIREMENTS.md` §2.3 giao heartbeat cho app với ghi chú "Hệ quả với server: Không có". `device_tokens.LastSeenAt` chỉ là lần đăng ký push gần nhất.

**Dùng lại, không viết lại:**
- **Nhắc user:** `ForecastAlertDispatcher` (outbox `forecast_alerts`: chống lặp theo `(user, kind, dedup_key)`, `HonorQuietHours`, `Wants()` theo công tắc của user), rồi tới `FcmPushNotificationService`, nơi chặn `PushEnabled`.
- **Giám sát:** `IIncidentService.RaiseAsync/ResolveAsync` (một sự cố đang mở cho mỗi fingerprint), `OpsServiceStatus` để đếm lần liên tiếp, `IIncidentNotifier`. Sự cố mức Normal đi vào email tổng hợp lúc 08:00.
- **Cấu hình:** `SystemConfigDocument` thêm một nhóm mới, có validator. Tài liệu cấu hình cũ đọc lên tự nhận giá trị mặc định.

## Đã duyệt (2026-11-14): điều chỉnh S1–S7

- **S1 (bổ sung D13).** Thêm tín hiệu thứ hai cho việc ngân hàng đổi mẫu tin.
  - Tỉ lệ `Financial` / (tổng trừ `Error`) của provider trong 24 giờ tụt dưới `formatFinancialShareDropRatio` (0,33) lần so với 7 ngày trước đó, với cùng điều kiện đủ mẫu.
  - Cùng loại sự cố, **cùng fingerprint** `notificationformat:{provider_key}`. `TechnicalDetail` nêu tín hiệu nào đang vượt (`error_rate`, `financial_share`, hoặc cả hai).
  - "Bình thường" (để đếm 24 lần liên tiếp rồi đóng) nghĩa là **cả hai** tín hiệu đều ổn. Thiếu mẫu thì `Unknown`.
  - Nền `Financial` bằng 0 thì tín hiệu này không xét.
  - Test: 60% Financial → 5% Financial, tỉ lệ lỗi ~0%, mở sự cố.
- **S2 (sửa D5).** Ví có `lastPostedAt` gần mà server im quá mức cho phép thì **không** báo `silent`, mà mang trạng thái `upload_gap` trong `/me/data-coverage`.
  - Đếm số ca `upload_gap` theo `app_version` của thiết bị báo `lastPostedAt` đó (không theo user). Số này thêm vào response `GET /admin/monitoring/provider-formats` dưới khoá `uploadGapsByAppVersion`.
  - Số đếm là ảnh chụp do job 09:00 ghi: bảng `listener_upload_gap_daily (day, app_version, count)`, không có `user_id`, dọn theo `historyRetentionDays`.
  - Push tuỳ chọn `reason = account_silent`, `hint = "sync_pending"`, nội dung "Mở app để đồng bộ", cùng chống lặp D17 (khoá `listener_health:upload:{accountId}:{mốc server}:{n}`).
  - Không nhắc `permission`. D9 vẫn áp dụng: thiết bị mất quyền thì chỉ gửi `permission_lost`.
- **S3 (thay D3).** Ngày có `permission_ok = false` khi có **bất kỳ** lần báo `permissionGranted = false`, **hoặc** lần báo **cuối** của ngày có `listenerConnected = false`. Mất kết nối giữa ngày rồi có lại thì ngày vẫn ok.
  - Bảng ngày lưu `any_permission_denied` và `last_listener_connected`. `permission_ok` tính khi đọc (hoặc cột sinh ra).
  - Test: `connected=false` rồi `connected=true` cùng ngày → ok.
- **S4 (sửa D4).** Kế thừa dùng khoá riêng `listenerHealth.coverageInheritHours` (36, khoảng 12–72). `staleReportHours` (72) chỉ dùng cho trạng thái `unknown`.
- **S5 (sửa D12).** Câu cố định phải qua được bộ kiểm số của Agent.
  - Evidence `data_coverage` mang đúng giá trị phần trăm như trong câu, hoặc câu không có số.
  - Test chạy qua verifier thật, không chỉ composer giả. Kiểm lại ở task 13: verifier hiện nằm phía ai-service (`ComposeAsync(skipVerifier)`). Nếu câu được ghép **sau** compose thì test phải chứng minh câu đó qua được đúng luật kiểm số; khi cần thì thêm test phía ai-service.
- **S6 (bổ sung D10).** `PUT /devices/listener-status` xoá cache độ phủ của user đó.
- **S7 (bổ sung D10/D11).** Khi gắn vào dự báo (`ForecastCoverage.Apply`, purchase-check, Agent), cửa sổ độ phủ = `forecast.historyWindowDays` kẹp về 7–90 (mặc định 90 → 90). `/me/data-coverage` vẫn mặc định 30. Mọi response có độ phủ ghi `windowDays`. Cache theo `(user, windowDays)`.

Hệ quả cho cấu hình: `listenerHealth` thêm `coverageInheritHours` (36; 12–72) và `formatFinancialShareDropRatio` (0,33; 0,01–1). `dataCoverage = { percent, level, windowDays, warning }`.

## Kết quả kiểm code (những điểm quyết định thiết kế)

1. **Thiết bị đang được xác định chỉ bằng token FCM.** `DevicesController` nhận `Token` trong body. `device_tokens` unique theo token, và token đổi bất cứ lúc nào FCM cấp lại. Nếu lấy token làm khoá thiết bị thì mỗi lần token đổi, thiết bị thành máy mới: mất lịch sử theo ngày, mất cả đợt nhắc. Đề xuất ở D1.
2. **Các ví được theo dõi khớp theo package.** `FinancialAccountRepository.GetByUserAndMonitoredPackageAsync` tìm ví theo `PackageName` + `IsMonitored`. Log của ví đang tắt theo dõi có `FinancialAccountId = null` và trạng thái `Ignored`. Vì vậy chỉ cần đếm `notification_logs.financial_account_id` là đúng "lịch sử của ví". `notification_logs` hiện chưa có index `(financial_account_id, received_at)`.
3. **`FinancialAccount` không có cột "bật theo dõi từ lúc nào".** Chỉ có `CreatedAt` và `UpdatedAt`. Xem D7.
4. **`CashflowForecastDto` không có danh sách cảnh báo**, chỉ có `Confidence` (`"low"`/`"normal"`). **`SafeToSpendDto` không có cả `Confidence`.** `PurchaseCheckDto` có `Warnings` và `Confidence`; `PurchaseVerdict.VerdictOf` đã trả `insufficient_data` thay cho `fits_plan` khi trạng thái là `partial`.
5. **`ForecastAccuracyService` (dòng 67) lưu snapshot `GetCashflowAsync` và chia độ chính xác theo `Confidence` low/normal** (`ForecastAccuracy.cs:111`). `ForecastAlertService.CheckLowBalanceAsync` cũng gọi `GetCashflowAsync`. Nếu sửa `Confidence` ngay trong `ForecastService.GetCashflowAsync` thì số liệu độ chính xác bị pha trộn và mỗi lần kiểm cảnh báo tốn thêm một truy vấn. Xem D10.
6. **Agent:** công cụ trả `Evidence`, còn `AgentOrchestrator` ghép câu trả lời qua LLM hoặc `LocalTemplateComposer`. Không có chỗ nào bắt LLM phải nhắc "dữ liệu có thể thiếu". Xem D12.
7. **Monitoring:** `IncidentSource` = Health/AppError/Job/Provider/Payment. Fingerprint của `ServiceStatusTracker` là `"{source}:{service}"`, và tiêu đề của nó cố định "{Name} đang lỗi", không khớp câu mà BR-LH-10 yêu cầu. `PipelineResult` gồm Financial/NonFinancial/Uncertain/ExtractionFailed/Error.
8. **Cron của Hangfire chạy theo UTC** (`monitoring-digest` dùng `"0 1 * * *"` cho 08:00 VN). Vậy 09:00 VN là `"0 2 * * *"`.

---

## Quyết định cần duyệt (mỗi mục kèm đề xuất)

**D1. Cách xác định thiết bị.**
Đề xuất: app tự sinh một `installationId` (UUID, lưu cục bộ, sinh lại khi cài lại app) và gửi trong body của `PUT /devices/listener-status`. Khoá là `(user_id, installation_id)`. Không gắn với `device_tokens`, vì token FCM xoay vòng và là capability, không nên làm khoá ở bảng thứ hai.
- Đổi tài khoản trên cùng máy: mỗi user có dòng riêng. Dòng của user cũ thôi được báo, quá 72 giờ thì thành `unknown`. Không xoá, không chuyển chủ.
- Mỗi user tối đa `listenerHealth.maxDevicesPerUser` (mặc định 10) thiết bị. Thiết bị thứ 11 → 422.

**D2. Gói ngoài danh sách đã chọn.**
Đề xuất: server chỉ lưu các mục `selectedPackages` có `packageName` trùng package của **một ví của chính user** (chưa xoá), hoặc trùng `provider_configs.package_name`. Mục khác bị bỏ lặng lẽ, không lưu, không log, không báo lỗi. Tối đa 50 mục, mỗi tên ≤ 255 ký tự.

**D3. Một ngày có nhiều lần báo.**
Đề xuất: theo hướng thận trọng, `permission_ok` của ngày = AND của mọi lần báo trong ngày. Một lần thấy mất quyền là ngày đó không được tính đủ. Ngày tính theo **giờ server nhận**, đổi sang UTC+7, không theo đồng hồ máy.

**D4. Ngày không có lần báo nào** (WorkManager lỡ một ngày).
Đề xuất: ngày đó lấy trạng thái của lần báo gần nhất trước nó, nếu lần báo đó còn trong `staleReportHours` (72 giờ). Quá hạn thì ngày đó không `permission_ok`. Việc kế thừa tính lúc đọc, không ghi dòng giả vào bảng theo ngày. Không có quy tắc này thì một lần WorkManager trễ cũng kéo độ phủ xuống.

**D5. `lastPostedAt` mâu thuẫn với sự im lặng ở server.**
Ví dụ: listener vẫn thấy thông báo VCB hôm qua (bộ lọc trên máy bỏ đi vì là OTP hoặc quảng cáo), nhưng server không nhận gì đã 4 ngày.
Đề xuất: mốc im lặng = max(log cuối ở server, `lastPostedAt` mới nhất của package đó trên mọi thiết bị của user). Listener thấy được thông báo thì nguyên nhân không nằm ở phía user, nhắc họ bật quyền là sai. Lỗi đọc mẫu tin đã có BR-LH-10 bắt. `lastPostedAt` ở tương lai quá 10 phút so với giờ server thì kẹp về giờ server.

**D6. `notificationsEnabled = false` cho package của ví.**
Đề xuất: không thêm `reason` mới, chỉ dùng `permission_lost` và `account_silent` như spec. Khi ví `silent` mà thiết bị báo package đó đang tắt thông báo, push `account_silent` có thêm `data.hint = "bank_notifications_off"` và câu nội dung riêng ("Thông báo của {ví} đang bị tắt trên máy"), để app mở đúng màn hướng dẫn. Cờ này cũng hiện trong `/me/data-coverage`.

**D7. "Ngày trước khi user tạo ví theo dõi đầu tiên không tính".**
Đề xuất:
- Mốc đầu = max(đầu cửa sổ, `CreatedAt` nhỏ nhất của các ví đang bật theo dõi và chưa xoá, **ngày báo trạng thái đầu tiên** của user).
- Điều kiện thứ ba đến từ ràng buộc "app chưa báo thì `unknown`": ngày trước khi app mới được cài không bị coi là ngày mất dữ liệu.
- Sau mốc đầu không còn ngày nào, hoặc user chưa từng báo → `percent = null`, `level = unknown`.
- Không thêm cột `monitoring_enabled_at`: ví bật theo dõi lại thì mốc vẫn là `CreatedAt`. Chấp nhận sai số này. Ghi §13.4 nếu cần chính xác hơn.

**D8. Trạng thái `unknown` (quá 72 giờ không báo) có nhắc không?**
Đề xuất: **không** push (BR-LH-05 chỉ liệt kê `silent` và `permission_lost`). Máy không báo thường là app cũ, đã gỡ hoặc đã bị diệt hẳn, và push tới đó gần như vô ích. `unknown` chỉ hiện trong `/me/data-coverage`.

**D9. Ví `silent` cùng lúc thiết bị `permission_lost`.**
Đề xuất: trong cùng một lượt chỉ gửi `permission_lost`, vì đó là nguyên nhân gốc. Bỏ `account_silent` của các ví có package nằm trong `selectedPackages` của thiết bị mất quyền. Đợt im lặng của ví vẫn tiếp tục: quyền được cấp lại mà ví vẫn im thì lượt sau nhắc `account_silent` như bình thường.

**D10. Gắn độ phủ vào dự báo ở đâu.**
Đề xuất:
- `ForecastService` và `CashflowEngine` giữ nguyên. Thêm `IDataCoverageService.GetSummaryAsync(userId)`, cache theo `(user, ngày VN)` 10 phút qua `ICacheService`, cùng một hàm thuần `ForecastCoverage.Apply(dto, summary)`.
- Hàm này được gọi ở `ForecastController` (cashflow, scenario, spendable), trong `PurchaseCheckService` và ở các công cụ Agent.
- `ForecastAccuracyService` và `CheckLowBalanceAsync` không đụng tới, để thống kê độ chính xác vẫn đo đúng độ tin cậy của engine.
- `DataCoverageDto` là property tuỳ chọn có `init` (mặc định `null`) trên DTO, nên các chỗ đang tạo DTO không phải sửa.

**D11. "Câu cảnh báo" đặt ở đâu, và `spendable` không có `confidence`.**
Đề xuất:
- `dataCoverage = { percent, level, warning }`. `warning` là câu mẫu của backend, chỉ có khi `poor`: "Khoảng {n}% số ngày gần đây app không đọc được thông báo ngân hàng, nên dự báo có thể thiếu giao dịch." Cách này không cần thêm danh sách cảnh báo vào `CashflowForecastDto`.
- `purchase-check` thêm cảnh báo `data_coverage_poor` vào `Warnings` sẵn có.
- `spendable` chỉ thêm `dataCoverage`, **không** thêm trường `confidence` mới, để khỏi đặt ra một khái niệm mà công thức của nó không có.

**D12. Agent bắt buộc nêu dữ liệu có thể thiếu.**
Đề xuất: không dựa vào LLM. Khi có ít nhất một công cụ dữ liệu trả `Ok` và độ phủ là `poor`, `AgentOrchestrator` làm hai việc:
- thêm một `Evidence` `data_coverage` (Text là câu mẫu, `EvidenceSource.Estimated`) để LLM thấy;
- **ghép cố định** câu mẫu đó vào cuối câu trả lời: một `AgentClaimDto` cuối cùng, hoặc trường `caveat` mới trên `AgentAnswerDto`. Chọn cách nào sẽ quyết khi đọc DTO ở task 13; mặc định là claim cuối nếu claim không bắt buộc kèm số.

Như vậy dù LLM lờ đi thì câu trả lời vẫn có câu đó. Công cụ tri thức (`search_knowledge`) không kích hoạt điều này.

**D13. Mẫu số của BR-LH-10.**
Đề xuất:
- Tỉ lệ lỗi = (ExtractionFailed + Uncertain) / (mọi `ai_results` của provider trong cửa sổ, **trừ `Error`**). `Error` là AI service hỏng, nguồn Health đã giám sát; tính vào thì một lần sập AI thành "VCB đổi mẫu".
- Có `NonFinancial` trong mẫu số vì đó là thông báo thật của ngân hàng.
- Provider xác định bằng `notification_logs.package_name` = `provider_configs.package_name`. Package không có provider thì không xét.
- Thời điểm lấy theo `ai_results.created_at`.

**D14. Tỉ lệ nền bằng 0, và một lỗi lẻ.**
Nếu 7 ngày trước đó không lỗi lần nào thì tỉ số là vô cực, và chỉ một lỗi trong 20 mẫu (5%) cũng mở sự cố.
Đề xuất thêm hai ngưỡng có cấu hình:
- `formatDriftBaselineFloorPercent` (1): tỉ lệ nền dùng để so = max(nền thật, 1%);
- `formatDriftMinRatePercent` (10): tỉ lệ 24 giờ phải ít nhất 10%.

Kịch bản (e) (2% → 30%, 40 mẫu) vẫn mở sự cố. Nền có dưới `formatDriftMinSamples` mẫu (provider mới) thì dùng sàn 1%.

**D15. "Tự đóng khi tỉ lệ về bình thường 24 giờ".**
Đề xuất: dùng `OpsServiceStatus` với khoá `format:{provider_key}`, cập nhật mỗi giờ.
- Vượt ngưỡng → `Unhealthy` → mở sự cố ngay (ngưỡng 1 lần).
- Bình thường → `Healthy`. Đủ 24 lần `Healthy` liên tiếp (≈ 24 giờ) → `ResolveAsync`.
- 24 giờ qua chưa đủ mẫu → `Unknown`: giữ nguyên bộ đếm, không mở và không đóng, giống quy tắc của monitoring.

Không dùng `ServiceStatusTracker`, vì tiêu đề của nó cố định. Logic đếm viết lại khoảng 20 dòng trong `ProviderFormatMonitor`.
- Thêm `IncidentSource.NotificationFormat`, fingerprint `notificationformat:{provider_key}`.
- Tiêu đề: "Có thể {DisplayName} đã đổi mẫu thông báo".
- `TechnicalDetail` chỉ có số đếm: "24h: 12/40 (30%), 7d: 3/150 (2%)".

**D16. Chạy job định dạng mỗi giờ thế nào.**
Đề xuất: job Hangfire riêng `ProviderFormatMonitorJob`, cron `"5 * * * *"`. Không làm `IMonitorSource`, vì nguồn đó chạy mỗi phút và truy vấn 8 ngày `ai_results` mỗi phút là phí. Sự cố mở ra vẫn đi qua `IIncidentNotifier` như mọi nguồn khác (`SystemMonitor.SendDueAsync` gửi ở lượt phút kế tiếp).

**D17. Chống nhắc lặp mà không thêm bảng "đợt".**
Đề xuất: danh tính của đợt nằm trong `dedup_key`.
- Ví: `listener_health:account:{accountId}:{mốc im lặng, unix giây}:{n}`.
- Thiết bị: `listener_health:device:{statusId}:{permission_lost_since, unix giây}:{n}`.

`n` chạy từ 1 tới `maxRemindersPerEpisode`. Trước khi gửi, đếm các cảnh báo cùng tiền tố đợt (thêm `IForecastAlertRepository.ListByDedupPrefixAsync`): đủ `n` thì dừng, lần cuối chưa quá `cooldownDays` thì bỏ qua.

Có thông báo mới thì mốc im lặng đổi, nên tự sang đợt mới. Quyền được cấp lại thì `permission_lost_since` về null. Lần mất quyền sau có mốc mới, nghĩa là đợt mới. Không cần job dọn dẹp, và chạy lại job hai lần trong ngày vẫn an toàn nhờ unique index sẵn có.

Cooldown so theo **ngày VN** (`cooldownDays` = 3 nghĩa là cách ít nhất 3 ngày lịch), để job 09:00 không lệch vài phút mà bị bỏ một ngày.

**D18. Công tắc.**
`NotificationPreferences.ListenerHealthAlertsEnabled`, cột `users.listener_health_alerts_enabled` mặc định `true`. Trong `ForecastAlertDispatcher.Wants()`, kind `ListenerHealth` chỉ theo công tắc này. **Không** phụ thuộc `ForecastAlertsEnabled`, đúng spec: "công tắc push chung và công tắc mới". `PushEnabled` vẫn do `FcmPushNotificationService` chặn. `PATCH /users/me/notification-prefs` nhận thêm trường tuỳ chọn này.

**D19. Phân vị 90.**
Dùng nearest-rank trên các khoảng cách giữa hai log liên tiếp **trong cửa sổ 30 ngày**, tính bằng giờ (double). Khoảng từ log cuối tới hiện tại **không** được tính vào phân bố. `received_at` ở tương lai thì kẹp về giờ server. Hai log cùng `received_at` cho khoảng cách 0 và vẫn được tính.

**D20. Dọn dòng theo ngày.**
Bước cuối của job 09:00 xoá các dòng cũ hơn `historyRetentionDays`, theo lô 5.000 dòng. Lỗi ở bước này không chặn việc nhắc, vì nhắc chạy trước và hai bước tách try/catch. Xoá cứng user: FK `ON DELETE CASCADE`, thêm lời gọi tường minh trong `UserHardDeleter` theo cách các bảng khác đang làm.

---

## Thiết kế

### Dữ liệu (migration `AddListenerHealth`)

**`device_listener_statuses`** (trạng thái mới nhất)

| Cột | Kiểu | Ghi chú |
| --- | --- | --- |
| `id` | uuid PK | |
| `user_id` | uuid FK users, cascade | |
| `installation_id` | uuid | unique `(user_id, installation_id)` |
| `permission_granted` | bool | |
| `listener_connected` | bool | |
| `selected_packages` | jsonb | `[{packageName, notificationsEnabled?, lastPostedAt?}]`, đã lọc theo D2 |
| `app_version` | text null | ≤ 32 |
| `manufacturer` | text null | ≤ 64, chuẩn hoá chữ thường |
| `permission_lost_since` | timestamptz null | đặt khi chuyển sang mất quyền, về null khi có lại |
| `first_reported_at` | timestamptz | |
| `last_reported_at` | timestamptz | |

**`device_listener_daily`**: `(status_id FK cascade, day date)` PK, cùng `user_id` (để xoá và truy vấn), `permission_ok bool`, `report_count int`. Index `(user_id, day)`.

**Các thay đổi khác:**
- `users.listener_health_alerts_enabled bool not null default true`.
- `chk_forecast_alerts_kind` thêm `listener_health`, kèm `ForecastAlertKindColumn` và `ForecastAlertKinds.Text`.
- Index `ix_notification_logs_account_received (financial_account_id, received_at) WHERE financial_account_id IS NOT NULL`.
- Index `ai_results (created_at)` nếu chưa có (kiểm khi viết migration).
- Nếu `ops_incidents.source` có CHECK thì thêm `notification_format`.

### Cấu hình

`listenerHealth`:

| Khoá | Mặc định | Validator |
| --- | --- | --- |
| `historyRetentionDays` | 90 | 7–365 |
| `historyDays` | 30 | 7–90 |
| `minNotificationsForBaseline` | 8 | 2–1000 |
| `minSilenceHours` | 48 | 1–720 |
| `silenceMultiplier` | 3 | 1–20 (double) |
| `staleReportHours` | 72 | 12–720 |
| `coverageInheritHours` | 36 | 12–72 (S4) |
| `cooldownDays` | 3 | 1–30 |
| `maxRemindersPerEpisode` | 3 | 1–10 |
| `maxDevicesPerUser` | 10 | 1–50 |
| `formatDriftRatio` | 3 | 1.1–100 (double) |
| `formatDriftMinSamples` | 20 | 1–10000 |
| `formatDriftMinRatePercent` | 10 | 0–100 |
| `formatDriftBaselineFloorPercent` | 1 | 0–100 |
| `formatDriftRecoveryRuns` | 24 | 1–168 |
| `formatFinancialShareDropRatio` | 0,33 | 0,01–1 (S1) |

`forecast`:
- `goodCoveragePercent` = 85;
- `minCoveragePercent` = 60;
- validator: `0 ≤ min ≤ good ≤ 100`.

### Code (Application là hàm thuần, Infrastructure là truy vấn)

- `Application/ListenerHealth/`:
  - `SilenceDetector`: hàm thuần cho BR-LH-03 và D5/D19. Vào: danh sách `received_at`, `lastPostedAt?`, `now`, settings. Ra: `{status: ok|silent|not_enough_data, silentSince?, allowedHours, silentDays}`.
  - `DeviceHealthEvaluator`: hàm thuần cho BR-LH-04. Ra `ok | permission_lost | unknown`.
  - `CoverageCalculator`: hàm thuần cho BR-LH-07, D3, D4, D7, gom nhiều thiết bị bằng OR theo ngày.
  - `CoverageLevel`: `good | partial | poor | unknown`.
  - `ReportListenerStatusCommand` + validator + handler.
  - `DataCoverageService` (`/me/data-coverage` và `GetSummaryAsync` có cache).
  - `ListenerHealthReminderService`: chạy theo từng user, BR-LH-05/06, D6, D8, D9, D17.
  - `ProviderFormatMonitor`: BR-LH-10, D13–D15.
  - `ProviderFormatStatsQuery`: BR-LH-11.
  - `ForecastCoverage.Apply` (D10/D11).
- `Infrastructure`:
  - `ListenerHealthRepository`: trạng thái, ngày, timestamps log theo ví, danh sách user cần xét;
  - `ProviderFormatRepository`: đếm theo provider, cửa sổ 24h/7d trong **một** truy vấn GROUP BY;
  - jobs `ListenerHealthDailyJob` (`"0 2 * * *"`, theo lô user) và `ProviderFormatMonitorJob` (`"5 * * * *"`).
- `API`:
  - `DevicesController.PutListenerStatus` → 204;
  - `MyFinanceController` (`api/v1/me`) `GET data-coverage?days=30` (ngoài 7–90 → 400);
  - `AdminMonitoringController` `GET provider-formats`.

### Nội dung push (không số tiền, không tên package)
- `permission_lost`: tiêu đề "FinMate không còn đọc được thông báo ngân hàng", nội dung "Quyền đọc thông báo trên máy đã bị tắt nên giao dịch mới sẽ không được ghi. Mở app để bật lại nhé." Data: `reason`, `installationId`, `manufacturer`.
- `account_silent`: tiêu đề "Chưa thấy giao dịch nào từ {tên ví}", nội dung "Đã {n} ngày FinMate không nhận được thông báo từ {tên ví}. Có thể app bị tắt chạy nền hoặc thông báo ngân hàng đang tắt." Data: `reason`, `accountId`, `accountName`, `silentDays`, `hint?`.
- Tên ví do user tự đặt. Payload `data` vốn không bao giờ được log (quy tắc của dispatcher), còn log của job chỉ ghi số đếm.

---

## Task (TDD; mỗi task một commit nhỏ; không đụng `docs/diagrams/*.drawio`)

1. **Cấu hình**: `ListenerHealthSettings` và hai khoá `forecast`, validator cùng test (biên, `min ≤ good`).
2. **Domain + migration**: entity, cấu hình EF, `ForecastAlertKind.ListenerHealth`, `IncidentSource.NotificationFormat`, cột prefs, index. Test: CHECK constraint nhận `listener_health` (Testcontainers).
3. **`SilenceDetector`**: unit cho lịch sử đều (2/ngày), thưa (3/30 ngày), rỗng, đúng 8 log (biên), P90 lớn hơn sàn 48h, `lastPostedAt` mới hơn log (D5), `received_at` ở tương lai (D19).
4. **`DeviceHealthEvaluator`**: mất quyền, listener mất kết nối mà quyền còn (vẫn là `ok` cho push, nhưng `permission_ok = false` cho độ phủ, theo BR-LH-02), quá 72 giờ, chưa từng báo.
5. **`CoverageCalculator`**: một thiết bị; hai thiết bị bù ngày cho nhau (OR); D3 (AND trong ngày); D4 (kế thừa ≤ 72h rồi hết); D7 (trước ví đầu tiên, trước lần báo đầu tiên, chưa báo → `unknown`); ngưỡng `good/partial/poor` ở đúng biên 85/60.
6. **`PUT /devices/listener-status`**: validator (D1/D2, giới hạn chiều dài, tối đa 50 gói, 11 thiết bị → 422); handler upsert, dòng theo ngày AND, `permission_lost_since` chuyển trạng thái; integration test controller (401, 204, lọc gói lạ, cùng `installationId` ở hai user là hai dòng).
7. **`GET /me/data-coverage`**: service + integration (400 khi `days` ngoài 7–90, `unknown` khi chưa báo, ví tắt theo dõi không xuất hiện (BR-LH-06), không lộ ví của user khác).
8. **Dispatcher**: `Wants(ListenerHealth)` theo công tắc mới, `ListByDedupPrefixAsync`, `PATCH notification-prefs` nhận trường mới.
9. **`ListenerHealthReminderService`**: unit cho cooldown theo ngày VN, tối đa 3 lần mỗi đợt, đợt mới khi có log mới hoặc quyền có lại, D6 hint, D8 không nhắc `unknown`, D9 chỉ gửi `permission_lost`, công tắc tắt, giờ yên lặng (gọi với `HonorQuietHours: true`).
10. **`ListenerHealthDailyJob`**: đăng ký cron, chạy theo lô, một user lỗi không chặn user khác, bước dọn D20; test idempotent (chạy hai lần cùng ngày → một push).
11. **`ForecastCoverage` + forecast endpoints**: cashflow/scenario/spendable có `dataCoverage`; `poor` → `confidence = low` và có `warning`; con số dự báo và dải **không đổi** (so sánh trước/sau); `unknown` không đổi confidence; `ForecastAccuracyService` vẫn nhận confidence gốc.
12. **`purchase-check`**: `PurchaseQuality.PoorCoverage` → `partial` + `data_coverage_poor`; không bao giờ `fits_plan`; thiếu tiền vẫn báo `shortfall_risk`.
13. **Agent**: các công cụ dữ liệu (`SpendingSummary`, `SimulatePurchase`, `PlanBudget`, `Runway`, `ForecastGoal`, `StressTest`, `Sensitivity`) chạy kèm độ phủ; câu cố định của D12 xuất hiện khi `poor`, kể cả khi composer giả bỏ qua evidence.
14. **`ProviderFormatMonitor` + job**: unit cho tỉ số, sàn nền, `minRate`, thiếu mẫu → `Unknown`, `Error` bị loại; 24 lần bình thường → đóng; chạy lại không mở sự cố thứ hai; `TechnicalDetail` chỉ có số.
15. **`GET /admin/monitoring/provider-formats`**: integration (AdminOnly, 403 cho user thường, chỉ có số).
16. **Xoá cứng**: `UserHardDeleter` + test hai bảng mới trống sau khi xoá.
17. **Kịch bản đầu-cuối (a)–(e)** (Testcontainers, đồng hồ giả):
    - (a) VCB ~2/ngày × 30 ngày, im 3 ngày → 1 push `account_silent`; job ngày +1 → 0 push; log mới → đợt kết thúc (log mới rồi im tiếp vượt mức → đợt mới, `n` lại bằng 1);
    - (b) MoMo 3 log/30 ngày, im 10 ngày → 0 push, `/me/data-coverage` trả `not_enough_data`;
    - (c) báo `permissionGranted=false` → 1 push `permission_lost`; báo `true` → `permission_lost_since = null`, job kế tiếp 0 push;
    - (d) 30 ngày, 15 ngày không `permission_ok` → 50%, `cashflow.dataCoverage.level = poor`, `confidence = low`; `purchase-check` không trả `fits_plan`;
    - (e) VCB nền 2%, 24h 30% với 40 mẫu → 1 sự cố đang mở; chạy lại → vẫn 1.
18. **Tài liệu**:
    - `docs/REQUIREMENTS.md`: FR-LH-01…11, sửa §2.3, §13.4 (nới dải theo độ phủ; cột `monitoring_enabled_at`);
    - `.context/TASKS.md`: Phase 84 + mục **"Báo nhóm Flutter"**;
    - `docs/ERD.md`;
    - tài liệu API mới `docs/listener-health-api.md`;
    - `docs/ops-api.md` (mục Giám sát: nguồn `notification_format`, endpoint provider-formats);
    - `.context/ARCHITECTURE.md` (một đoạn);
    - đoạn Phase 84 trong `CLAUDE.md`.

## Verification
- Mốc trước khi làm: backend 3012/3013 (chỉ FR-FIN-07 đỏ), ai-service 2003 qua / 453 bỏ qua khi không có Postgres. Chạy lại mốc trên nhánh này trước task 1.
- Khi viết từng task: chạy lớp test của tính năng. Trước mỗi commit: unit + test liên quan. Trước khi xin merge: toàn bộ `dotnet test` (container SDK 9, podman) và `pytest` của ai-service. Không có thêm test đỏ nào so với mốc.
- Grep cuối: không có `NotificationBody`, `AmountCents` hay `packageName` ngoài danh sách đã lọc trong log của code mới.
- **Không push, không mở PR** cho tới khi được đồng ý.

## Ngoài phạm vi
- Nới dải dự báo theo độ phủ (ghi §13.4).
- Phía app (Flutter): ghi ở mục "Báo nhóm Flutter" của TASKS.md.
- ai-service không đổi (BR-LH-10 chỉ đọc `ai_results` của backend).

## Bảng BR → test (sau khi làm)

| BR / quyết định | Test |
|---|---|
| BR-LH-01, D1, D2, D5, S6 | `ReportListenerStatusCommandHandlerTests`, `ListenerStatusControllerTests` |
| BR-LH-02, S3 | `ListenerStatusControllerTests` (ngày AND, lần báo cuối), `CoverageCalculatorTests` |
| BR-LH-03, D19, S2 | `SilenceDetectorTests` (đều, thưa, rỗng, biên 8, P90, `upload_gap`, tương lai, im lặng dài) |
| BR-LH-04, D8 | `DeviceHealthEvaluatorTests` |
| BR-LH-05, D6, D9, D17, D18 | `ListenerHealthReminderServiceTests`, `ForecastAlertDispatcherTests`, `ListenerHealthPrefsTests`, `ListenerHealthJobTests` (chạy hai lần → một nhắc) |
| BR-LH-06 | `DataCoverageControllerTests.AccountStatuses_OnlyForWatchedAccountsOfThisUser` |
| BR-LH-07, S4, D7 | `CoverageCalculatorTests` (nhiều thiết bị, kế thừa, mốc đầu, `unknown`, biên mức), `DataCoverageControllerTests` |
| BR-LH-08, D10, D11, S7 | `ForecastCoverageTests`, `PurchaseVerdictTests.PoorDataCoverage_*`, `DataCoverageControllerTests.ForecastResponses_*` |
| BR-LH-09, S5 | `AgentOrchestratorTests.*coverage*`, ai-service `test_agent_verifier.py::test_the_fixed_data_coverage_sentence_passes_the_verifier` |
| BR-LH-10, D13–D16, S1 | `ProviderFormatMonitorTests` |
| BR-LH-11 | `ProviderFormatsControllerTests` |
| D20 | `ListenerHealthDailyJobTests`, `ListenerHealthJobTests.OldDailyRows_*`, `ListenerHealthSchemaTests.HardDeletingTheUser_*` |
| Kịch bản (a)–(e) | `ListenerHealthScenarioTests` |

Ghi chú khi làm:
- **Cửa sổ của BR-LH-03** kết thúc ở thông báo cuối, không ở hiện tại. Nếu kết thúc ở hiện tại thì sau khoảng ba tuần im lặng, ví sẽ tụt từ `silent` xuống `not_enough_data`.
- **D9 khi thiết bị mất quyền không báo `selectedPackages`:** coi như thiết bị đó giải thích sự im lặng của mọi ví.
- **D20 không cần sửa code:** `UserHardDeleter` chỉ xoá dòng `users`, còn hai bảng mới tự xoá theo khoá ngoại cascade.
- **BR-LH-09 thêm độ phủ ở `AgentOrchestrator`, không ở từng công cụ.** Mỗi lượt hỏi chỉ đọc độ phủ một lần, và chỉ đọc khi có ít nhất một công cụ dữ liệu trả `Ok`.
