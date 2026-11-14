# Báo cáo Phase 84: sức khoẻ dịch vụ đọc thông báo và độ phủ dữ liệu

Phase 84 đã làm xong cả 18 task. Test không có ca đỏ mới, và mọi thứ còn nằm trên máy: chưa push, chưa mở PR.

- **Ngày:** 2026-11-14
- **Nhánh:** `feat/listener-health`, gồm 19 commit so với `main`.
- **Phạm vi thay đổi:** 79 file, +11.989 / −39 dòng. Khoảng 6.600 dòng trong số đó là file Designer do EF tự sinh cho migration.
- **Kế hoạch:** `docs/superpowers/plans/2026-11-14-listener-health.md`, gồm các quyết định D1–D20 và điều chỉnh S1–S7.

## 1. Danh sách commit

| Commit | Task | Nội dung |
|---|---|---|
| `5354702` | — | Kế hoạch, bản chờ duyệt |
| `74f3d16` | — | Kế hoạch đã duyệt, kèm S1–S7 |
| `fe02bb9` | 1 | Cấu hình `listenerHealth` và hai ngưỡng độ phủ trong `forecast` |
| `395d516` | 2 | Lược đồ: 3 bảng mới, 1 cột mới, các index |
| `f6b041e` | 3 | `SilenceDetector`: phát hiện ví im lặng |
| `1919967` | 4 | `DeviceHealthEvaluator`: trạng thái thiết bị |
| `6c1e5b4` | 5 | `CoverageCalculator`: tính độ phủ |
| `193d31d` | 6 | `PUT /devices/listener-status` |
| `ccc39cc` | 7 | `GET /me/data-coverage` |
| `40f63c9` | 8 | Công tắc nhắc riêng, tra đợt nhắc trong outbox |
| `0cb2468` | 9 | Service nhắc nhở: cách quãng và giới hạn số lần mỗi đợt |
| `ce591dd` | 10 | Job 09:00 hằng ngày, đếm `upload_gap`, dọn dữ liệu cũ |
| `7e8294a` | 11–12 | Độ phủ trên cashflow, scenario, spendable, purchase-check |
| `7194155` | 13 | Agent nói "có thể thiếu dữ liệu" khi độ phủ kém |
| `d1a9efe` | 14 | Job mỗi giờ theo dõi ngân hàng đổi mẫu thông báo |
| `622fece` | 15 | `GET /admin/monitoring/provider-formats` |
| `9491bfd` | 16 | Test: xoá user thì xoá theo dữ liệu thiết bị |
| `766e33f` | 17 | Kịch bản đầu-cuối (a)–(e) và S1 |
| `0d8244e` | 18 | Tài liệu |

## 2. Thay đổi theo lớp

### Database (migration `20261114112300_AddListenerHealth`)

**Bảng mới**
- **`device_listener_statuses`:** trạng thái mới nhất của mỗi thiết bị.
  - Unique theo `(user_id, installation_id)`.
  - `selected_packages` là jsonb, có CHECK bắt buộc phải là mảng.
  - Khoá ngoại tới `users` có cascade.
- **`device_listener_daily`:** mỗi thiết bị mỗi ngày (giờ VN) một dòng, gồm `any_permission_denied`, `last_permission_granted`, `last_listener_connected` và `report_count`. Cascade theo cả thiết bị lẫn user.
- **`listener_upload_gap_daily`:** số ví bị `upload_gap` mỗi ngày, theo phiên bản app. Bảng này không có user.

**Cột mới:** `users.listener_health_alerts_enabled`, mặc định `true`.

**Index**
- `ix_notification_logs_account_received`: lọc theo `financial_account_id IS NOT NULL`, thay cho index FK cũ.
- `ix_ai_results_created_at`.
- Hai index cho bảng ngày.

**CHECK được nới thêm giá trị**
- `forecast_alerts.kind` nhận thêm `listener_health`.
- `ops_incidents.source` và `ops_error_events.source` nhận thêm `notification_format`.

### Cấu hình (`SystemConfigDocument`)

**Nhóm `listenerHealth`**

| Khoá | Giá trị | Khoá | Giá trị |
|---|---|---|---|
| historyRetentionDays | 90 | cooldownDays | 3 |
| historyDays | 30 | maxRemindersPerEpisode | 3 |
| minNotificationsForBaseline | 8 | maxDevicesPerUser | 10 |
| minSilenceHours | 48 | formatDriftRatio | 3 |
| silenceMultiplier | 3 | formatDriftMinSamples | 20 |
| staleReportHours | 72 | formatDriftMinRatePercent | 10 |
| coverageInheritHours | 36 (12–72) | formatDriftBaselineFloorPercent | 1 |
| formatFinancialShareDropRatio | 0.33 | formatDriftRecoveryRuns | 24 |

**Nhóm `forecast`:** `goodCoveragePercent` = 85, `minCoveragePercent` = 60.

Mọi giá trị đều có validator. `ChangedSections` có thêm `listenerHealth`, nên sửa nhóm này sẽ được ghi vào audit.

### API

| Route | Thay đổi |
|---|---|
| `PUT /devices/listener-status` | Mới, trả 204. Trả 400 nếu thiếu cờ, 422 `LISTENER_TOO_MANY_DEVICES` từ thiết bị thứ 11 |
| `GET /me/data-coverage?days=7..90` | Mới, mặc định 30 ngày |
| `GET /forecast/cashflow`, `POST /forecast/cashflow/scenario`, `GET /forecast/spendable`, `POST /forecast/purchase-check` | Thêm trường tuỳ chọn `dataCoverage` |
| `PATCH /users/me/notification-prefs` | Thêm `listenerHealthAlertsEnabled?` (bỏ trống thì giữ nguyên) |
| `GET /users/me` | Thêm `listenerHealthAlertsEnabled` |
| `GET /admin/monitoring/provider-formats` | Mới, chỉ Admin, chỉ trả số đếm |

Chi tiết từng trường: `docs/listener-health-api.md`.

### Job Hangfire

| Job | Lịch (UTC) | Điều kiện |
|---|---|---|
| `listener-health-daily` | `0 2 * * *` (09:00 giờ VN) | Luôn chạy |
| `provider-format-monitor` | `5 * * * *` | Chỉ khi bật `MONITORING_ENABLED`; khi tắt thì gọi `RemoveIfExists` |

**Job hằng ngày**
- Duyệt user theo trang (keyset).
- Mỗi user chạy trong try/catch riêng, một user lỗi không làm dừng cả job.
- Đếm `upload_gap` theo phiên bản app; thiếu phiên bản thì ghi `"unknown"`.
- Dọn các dòng ngày cũ hơn 90 ngày theo từng lô, trong một try/catch riêng.

### Logic (`Application/ListenerHealth/`)

- **`SilenceDetector`**
  - Khoảng im lặng cho phép = max(48 giờ, 3 × P90 của khoảng cách giữa hai thông báo). P90 tính theo nearest-rank.
  - Cần ít nhất 8 thông báo; ít hơn thì trả `not_enough_data`.
  - Thời điểm ở tương lai bị kẹp về hiện tại.
  - Listener còn thấy app trong khoảng cho phép thì trả `upload_gap` (S2).
- **`DeviceHealthEvaluator`:** trả `ok`, `permission_lost`, hoặc `unknown` khi quá 72 giờ không báo.
- **`CoverageCalculator`**
  - Một ngày được tính là ok theo quy tắc S3.
  - Ngày app không báo được kế thừa lần báo trước nếu lần đó cách trưa ngày ấy không quá 36 giờ.
  - Không tính những ngày trước khi tạo ví theo dõi đầu tiên và trước lần báo đầu tiên.
  - Phần trăm làm tròn xuống.
- **`ListenerHealthReader`:** nơi duy nhất quyết định ví nào được theo dõi và trạng thái của từng ví. Job và `/me/data-coverage` đều đi qua đây.
- **`DataCoverageService` và `DataCoverageCache`:** cache 10 phút cho mỗi user và mỗi độ dài cửa sổ. Mỗi lần PUT trạng thái thì xoá cache (S6).
- **`ForecastCoverage.Apply`:** khi độ phủ `poor`, `confidence` thành `low` và có thêm câu cảnh báo cố định. Số liệu và dải dự báo không đổi.
- **`ListenerHealthReminderService`:** nhắc theo đợt, mã chống trùng có dạng `listener_health:{device|account|upload}:{id}:{anchor}:{n}`, giữ cách quãng 3 ngày và tối đa 3 lần mỗi đợt.
- **`ProviderFormatMonitor`:** hai tín hiệu nghi ngân hàng đổi mẫu:
  - tỉ lệ đọc lỗi tăng gấp 3 so với 7 ngày trước;
  - tỉ lệ thông báo được xếp là giao dịch tụt dưới 0,33 lần (S1).

  Mỗi provider có tối đa một sự cố `Normal`. Sau 24 lần chạy khoẻ liên tiếp thì sự cố tự đóng.

### Code cũ bị chạm (chỉ thêm, không đổi hành vi cũ)

- **`ForecastAlertDispatcher`:** kind mới đi theo công tắc riêng, không theo `forecastAlertsEnabled`.
- **`ForecastAlertRepository`:** thêm `ListCreatedWithKeyPrefixAsync`.
- **`CashflowEngine.cs` và `SpendableCalculator.cs`:** chỉ thêm một thuộc tính `DataCoverage` (init) vào DTO. Thuật toán không đổi.
- **`PurchaseVerdict` / `PurchaseCheckService`**
  - Thêm tham số tuỳ chọn `PoorCoverageWarning`.
  - Khi độ phủ `poor`: thêm cảnh báo `data_coverage_poor`, không bao giờ trả `fits_plan`, nhưng thiếu tiền vẫn trả `shortfall_risk`.
- **`AgentOrchestrator`**
  - Khi câu trả lời dùng dữ liệu của user và độ phủ `poor`, thêm bằng chứng `data_coverage` và mệnh đề cảnh báo cố định.
  - Câu chỉ tra tri thức (`search_knowledge`, `get_assumptions`) thì không thêm.

## 3. Quy tắc nghiệp vụ và test tương ứng

| Quy tắc | Cài ở | Test chính |
|---|---|---|
| BR-LH-01 app báo trạng thái | `ReportListenerStatusCommand`, `ListenerHealthRepository` (CTE upsert) | `ReportListenerStatusCommandHandlerTests`, `ListenerStatusControllerTests` |
| BR-LH-02 trạng thái thiết bị | `DeviceHealthEvaluator` | `DeviceHealthEvaluatorTests` |
| BR-LH-03 im lặng bất thường | `SilenceDetector` | `SilenceDetectorTests` (16 ca) |
| BR-LH-04/05 nhắc nhở và chống trùng | `ListenerHealthReminderService` | `ListenerHealthReminderServiceTests` |
| BR-LH-06 công tắc riêng | Dispatcher, prefs | `ListenerHealthPrefsTests`, `ForecastAlertDispatcherTests` |
| BR-LH-07 độ phủ | `CoverageCalculator`, `DataCoverageService` | `CoverageCalculatorTests`, `DataCoverageControllerTests` |
| BR-LH-08 độ phủ trên dự báo | `ForecastCoverage`, `PurchaseCheckService` | `ForecastCoverageTests`, `PurchaseVerdictTests` |
| BR-LH-09 agent | `AgentOrchestrator` | `AgentOrchestratorTests`, `test_agent_verifier.py` |
| BR-LH-10 ngân hàng đổi mẫu thông báo | `ProviderFormatMonitor` | `ProviderFormatMonitorTests` |
| BR-LH-11 thống kê cho admin | `ProviderFormatStatsService` | `ProviderFormatsControllerTests` |
| Lược đồ và xoá user (D20) | Migration | `ListenerHealthSchemaTests` |
| Job | `ListenerHealthJobs` | `ListenerHealthDailyJobTests`, `ListenerHealthJobTests` |
| Kịch bản (a)–(e) và S1 | — | `ListenerHealthScenarioTests` |

## 4. Kết quả test

| Bộ test | Trước Phase 84 | Sau Phase 84 |
|---|---|---|
| Backend | 3012/3013 | **3207/3208** (+195 ca) |
| ai-service | — | 2025 qua, 457 bỏ qua (máy không có Postgres), 0 đỏ |

Ca đỏ duy nhất là `MethodSelectionOnSeedDataTests` (FR-FIN-07), lỗi có sẵn từ `b6f1c42`. Mốc "trước" được chạy trên một worktree sạch tách từ `main`, nên con số so sánh tin được.

## 5. Bốn chỗ tự quyết khi làm (cần xác nhận)

1. **Cửa sổ 30 ngày của BR-LH-03 tính lùi từ thông báo cuối, không từ hôm nay.**
   - Nếu tính từ hôm nay, ví im khoảng ba tuần sẽ tụt từ `silent` xuống `not_enough_data`, dù thực ra vẫn đang im.
   - Có test `LongSilence_StaysSilent_…`: im 40 ngày vẫn là `silent`.
2. **D9:** thiết bị mất quyền mà không gửi danh sách app thì coi như nó giải thích sự im lặng của mọi ví. Khi đó chỉ gửi `permission_lost`, không gửi thêm `account_silent`.
3. **BR-LH-09:** độ phủ được gắn một lần ở orchestrator, không ở từng công cụ.
   - Câu cảnh báo luôn có mặt, kể cả khi LLM bỏ qua.
   - Câu chỉ chứa phần trăm, mà verifier của ai-service chỉ kiểm số tiền, nên câu luôn qua được. Đã có test chạy qua verifier thật.
4. **D20 không cần sửa code:** `UserHardDeleter` xoá `users` thì các bảng mới tự xoá theo cascade. Đã có test chạy qua `UserHardDeleter` thật.

## 6. Quyền riêng tư

- Server chỉ lưu package của các ví user đã chọn hoặc của provider đã biết. Package lạ bị bỏ lặng lẽ.
- Nội dung thông báo và số tiền không bao giờ được gửi lên.
- Đã grep toàn bộ lệnh ghi log của code mới: chỉ có `userId` và các con số đếm.
- Push `listener_health` không có số tiền.
- Endpoint admin chỉ trả số đếm theo provider và phiên bản app, không có user hay nội dung.
- Thời điểm báo cáo ở tương lai quá 10 phút bị kẹp về giờ server.

## 7. Rủi ro khi deploy

- **Index trên bảng lớn có thể khoá ghi.** Migration tạo `ix_notification_logs_account_received` và `ix_ai_results_created_at` bằng `CREATE INDEX` thường, không dùng `CONCURRENTLY`. Trong lúc tạo, ghi vào `notification_logs` và `ai_results` bị khoá. Bảng còn nhỏ thì không sao; bảng production đã lớn thì nên chạy migration giờ thấp điểm, hoặc chuyển sang tạo index thủ công bằng `CONCURRENTLY`.
- **Lần chạy đầu của job đổi mẫu thông báo có thể chưa có nền 7 ngày.** Khi đó quy tắc mẫu tối thiểu (20) và sàn 1% sẽ chặn báo động giả.
- **App cũ không ảnh hưởng.** App chưa báo trạng thái thì độ phủ là `unknown`, không phải `poor`, nên dự báo của user cũ không bị hạ độ tin cậy.

## 8. Còn mở

- **Chờ chủ sản phẩm quyết (đã ghi ở `REQUIREMENTS.md` §13.4):**
  - có nới dải dự báo theo độ phủ không;
  - có thêm cột `monitoring_enabled_at` không. Hiện mốc "ví theo dõi đầu tiên" lấy theo ngày tạo ví.
- **Nhóm Flutter:** phía app cần gọi PUT trạng thái (WorkManager mỗi ngày, khi quyền đổi, khi service mất hoặc có lại kết nối), sinh `installationId`, đọc `lastPostedAt`, và làm màn hình cho các push `listener_health`. Chi tiết ở mục "Báo nhóm Flutter" trong `.context/TASKS.md`.
- **Web ops:** chưa có màn hình cho `provider-formats` vì nằm ngoài repo này.

## 9. Tài liệu đã cập nhật

- `docs/REQUIREMENTS.md`: FR-LH-01…11 ở §6.11, §2.3, §13.4, số test.
- `.context/TASKS.md`: Phase 84 và mục "Báo nhóm Flutter".
- `docs/listener-health-api.md` (mới).
- `docs/ops-api.md`: mục Giám sát, nguồn `notification_format`, `provider-formats`.
- `docs/ERD.md`, `.context/ARCHITECTURE.md` §2.13, `CLAUDE.md`.
- Bảng BR → test cuối file kế hoạch.
