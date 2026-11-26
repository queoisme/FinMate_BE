# Phase 94 — Dashboard KPI sản phẩm (bản gọn)

> Trạng thái: **đã duyệt bản gọn 2026-11-26** (chưa push, chưa mở PR). Nhánh `feat/kpi-dashboard` từ `main` @ `88b5049`.
> Đồ án chưa có người dùng thật, nên DAU và cohort sẽ không có số trước buổi bảo vệ. Bản gọn chỉ giữ những chỉ số **tính khi gọi** từ các bảng đã có.

## Đã duyệt (2026-11-26): phạm vi bản gọn

**Bỏ** (ghi vào `REQUIREMENTS.md` §13.4 "KPI — phần còn lại"): bảng `user_activity_days`, `UserActivityMiddleware` và mọi lọc request nền (D1–D3, D7); `kpi_daily`, `KpiDailyJob` và backfill (D10, D12, D15); giữ chân / cohort (BR-KPI-04, D13); thêm `TransactionSource.Chat` (D5); xuất `activity_days` (D14); câu chữ quyền riêng tư (D19). Không có trong danh sách giữ nên cũng bỏ: độ phủ dữ liệu (D8) và số câu hỏi Agent.

**Giữ:** quyền `ViewProductKpi`, `GET /ops/kpi/summary` tính khi gọi, cache Redis 10 phút, che ô nhỏ, chỉ `role = User`, khoảng tối đa 366 ngày, mục "Báo nhóm app" về `PUT /devices/listener-status`.

## 1. Kết quả "Đọc trước" (đã kiểm trong code và app `FinMate_FE_Mobile`)

### 1.1 Nguồn hiện có ghi nhận "user hoạt động"

| Nguồn | Độ tin cậy cho DAU/MAU | Vì sao |
|---|---|---|
| `refresh_tokens.created_at` (mỗi lần xoay token tạo một dòng, không bị xoá) | Thấp | Code Kotlin native (widget, dịch vụ đọc thông báo) cũng refresh khi chạy nền; mở app < 15 phút sau lần refresh trước thì không có dòng mới |
| `device_tokens.last_seen_at` | Rất thấp | App chỉ gọi `POST /devices` từ màn "Thiết bị & thông báo push" |
| `audit_logs` đăng nhập | Thấp | Phiên kéo dài 30 ngày qua refresh |
| `device_listener_*` (Phase 84) | Không dùng được | App chưa gọi `PUT /devices/listener-status`; khi có thì cũng là báo nền |
| `notification_logs` | Không phải hoạt động | Do dịch vụ nền gửi lên |
| `transactions` (trừ `Notification`) | Đúng nhưng thiếu | Bỏ sót người chỉ xem, xác nhận nháp, hỏi Agent |
| Log request Serilog | Không có | Ngoài Development không ghi file |

Không nguồn nào đủ tin cho DAU/MAU. Vì vậy bản gọn **không có DAU**; chỉ số gần nhất là `transactingUsers`, đặt đúng tên theo cách đo.

### 1.2 Nguồn của từng chỉ số được giữ

| Chỉ số | Nguồn | Loại |
|---|---|---|
| `signups` | `users.created_at`, `role = User` (qua `user_roles`) | dòng chảy |
| `emailVerified` | user đăng ký trong ngày, `email_verified_at IS NOT NULL` (D18: theo ngày đăng ký) | dòng chảy |
| `onboardingCompleted` | **S1 (lúc làm):** user đăng ký trong ngày, `onboarding_status = completed` (theo ngày đăng ký như D18). `onboarding_completed_at` không dùng được: trạng thái `skipped` cũng có mốc, và migration `AddFinanceMethodsAndOnboarding` đặt mốc = giờ chạy migration cho mọi tài khoản cũ | dòng chảy |
| `methodChosen` | user có dòng `user_methods` **đầu tiên** trong ngày | dòng chảy |
| `deletionRequests` | `data_deletion_requests.created_at` | dòng chảy |
| `transactingUsers` | user khác nhau có giao dịch `Confirmed`, chưa xoá, `source <> Notification`, theo ngày `created_at` | đếm người khác nhau trong kỳ |
| `transactions.total`, `bySource` | giao dịch `Confirmed`, chưa xoá, theo ngày `created_at`; nguồn hiện có `notification/manual/voice/receipt/support` (`manual` gồm cả chat và giọng nói qua server) | dòng chảy |
| `notifications.*` | `notification_logs` theo ngày `created_at`; `converted` = log có giao dịch chưa xoá trỏ tới qua `transactions.notification_log_id` (D16) | dòng chảy + tỉ lệ |
| `featureUsers.*` | ảnh chụp **hiện tại** (không theo kỳ): ngân sách cá nhân đang bật, mục tiêu chưa xoá, khoản vay còn dư nợ, thành viên nhóm đang hoạt động | ảnh chụp |
| `subscriptions.payingTotal`, `payingByPlan` | `user_subscription_periods` chứa thời điểm cuối kỳ, `voided_at IS NULL` | tồn cuối kỳ |
| `subscriptions.paidOrders`, `revenueCents`, `needsReview` | `subscription_orders` theo ngày `paid_at` (`Paid`) và ngày `created_at` (`NeedsReview`) | dòng chảy |

Các cột cụ thể (tên bảng con, trạng thái khoản vay, mục tiêu) được kiểm lại trong code ở Task 3, trước khi viết truy vấn.

## 2. Thiết kế

### 2.1 Dữ liệu (migration `AddViewProductKpiCapability`)

- `OpsCapability.ViewProductKpi` (bit kế tiếp) và `UPDATE role_permissions SET capabilities = capabilities | <bit> WHERE role_id = <OpsManager>` (giống `AddLegalVersions`, vì `RolePermissionSeeder` không đụng tới dòng đã có). Down gỡ bit. Admin luôn đủ quyền; Staff không có, Admin cấp thêm được qua `/admin/role-permissions`.
- Không có bảng mới.

### 2.2 Tính khi gọi

- `IKpiRepository` (Infrastructure) chạy mỗi nhóm chỉ số bằng **một** truy vấn `GROUP BY` theo ngày VN (`(ts AT TIME ZONE 'Asia/Ho_Chi_Minh')::date`) trên `[from 00:00 VN, to+1 00:00 VN)`, trả về các dòng `(day, key?, count)`. Riêng `transactingUsers` và `payingTotal`/`payingByPlan` cần truy vấn theo kỳ (đếm người khác nhau trong kỳ, hoặc tại thời điểm cuối kỳ), nên repository nhận danh sách kỳ `[start, end)` thay vì chỉ ngày.
- `KpiSummaryService` (Application, `Application/Kpi/`) chia kỳ, ghép số, che ô nhỏ. Phần thuần (`KpiPeriods`, `KpiAggregation`, `SmallCellMask`, `KpiRange`) tách riêng để unit test.
- Cache Redis 10 phút qua `ICacheService` có sẵn, khoá `kpi:summary:{from}:{to}:{granularity}:{minCellSize}`.

### 2.3 Công thức

- **Kỳ** (D11): `day` = từng ngày; `week` = thứ Hai đến Chủ nhật theo giờ VN; `month` = tháng lịch. Kỳ đầu và kỳ cuối bị cắt theo `from`/`to` và có `partial: true` khi không đủ cả kỳ.
- **Dòng chảy** (đăng ký, giao dịch, thông báo, đơn đã trả, doanh thu, `needsReview`): tổng các ngày trong kỳ.
- **`transactingUsers`**: số người **khác nhau** trong cả kỳ (không cộng các ngày, nếu không một người giao dịch 7 ngày sẽ bị đếm 7 lần).
- **`payingTotal`, `payingByPlan`**: số người có kỳ thuê bao chứa thời điểm cuối kỳ (hoặc *bây giờ*, nếu kỳ cuối chưa hết).
- **Tỉ lệ** `conversionRate = converted / (received − ignored − duplicate)`, tính lại từ tổng của kỳ; mẫu số 0 → `null`. Làm tròn 4 chữ số.
- **Doanh thu**: tổng `amount_cents` (`long`, đơn vị đồng) của đơn đang `Paid` có `paid_at` trong kỳ. `Voided` không tính; `NeedsReview` đếm riêng, không vào doanh thu (D9). Không có doanh thu theo gói.
- **Ô nhỏ** (BR-KPI-07): trong các nhóm con — `transactions.bySource`, `subscriptions.payingByPlan` — giá trị 1 đến `minCellSize − 1` trả chuỗi `"<{minCellSize}"`; 0 vẫn là 0. Che **sau** khi gộp kỳ. Tổng toàn hệ thống (mọi trường còn lại, kể cả `payingTotal`, `revenueCents`) không che.
- **Loại trừ** (D17): mọi chỉ số về người dùng chỉ đếm user có vai trò `User`. Tài khoản đang chờ xoá vẫn tính vào số đăng ký của ngày họ đăng ký, nhưng không tính vào `payingTotal` và `featureUsers`.

### 2.4 API

`OpsKpiController` `GET /api/v1/ops/kpi/summary?from=&to=&granularity=day|week|month`, kế thừa `OpsControllerBase` (đã có 2FA qua `OpsVerifiedRequirement`). Service gọi `IOpsAccessService.RequireAsync(actor, ViewProductKpi)` (đọc DB).

- `from`, `to`: ngày VN (`yyyy-MM-dd`), `from ≤ to`, tối đa 366 ngày tính cả hai đầu, `to` không quá hôm nay; sai thì 422 `KPI_INVALID_RANGE`. `granularity` mặc định `day`.
- Response:

```json
{
  "granularity": "week",
  "minCellSize": 5,
  "featureUsers": { "budgets": 12, "goals": 7, "loans": 3, "groups": 0, "asOf": "2026-11-26T08:00:00+07:00" },
  "notes": { "manualIncludesChat": true },
  "periods": [{
    "start": "2026-11-02", "end": "2026-11-08", "partial": false,
    "signups": 9, "emailVerified": 8, "onboardingCompleted": 6, "methodChosen": 5, "deletionRequests": 0,
    "transactingUsers": 7,
    "transactions": { "total": 41, "bySource": { "notification": 20, "manual": 15, "voice": "<5", "receipt": 6, "support": 0 } },
    "notifications": { "received": 30, "ignored": 4, "duplicate": 2, "converted": 20, "conversionRate": 0.8333 },
    "subscriptions": { "payingTotal": 3, "payingByPlan": { "pro": "<5" }, "paidOrders": 3, "revenueCents": 297000, "needsReview": 1 }
  }]
}
```

`featureUsers` nằm ngoài `periods` vì là ảnh chụp hiện tại, không theo kỳ. Kiểu số đếm có thể che dùng `KpiCount` (serialize thành số, hoặc chuỗi khi bị che). Không response nào có id, email, tên hay số tiền của từng người.

### 2.5 Tài liệu

`REQUIREMENTS.md`: FR-KPI-01…, NFR quyền riêng tư cho số liệu tổng hợp, §13.4 "KPI — phần còn lại". `docs/ops-api.md`: mục KPI và dòng `ViewProductKpi` trong bảng quyền. ERD: không có bảng mới, chỉ ghi chú bit quyền nếu ERD có liệt kê. `.context/TASKS.md` Phase 94 kèm "Báo nhóm web admin" và "Báo nhóm app". `CLAUDE.md` thêm một đoạn.

## 3. Các điểm còn hiệu lực

- **S1 (phát hiện lúc làm, 2026-11-27):** `onboardingCompleted` đếm theo ngày đăng ký, chỉ trạng thái `completed` (xem bảng §1.2).
- **D9** doanh thu như §2.3. **D11** quy đổi kỳ như §2.3 (không còn `avgDau`). **D16** tỉ lệ thông báo như §2.3. **D17** loại trừ như §2.3. **D18** `emailVerified` theo ngày đăng ký.
- **D6 (sửa):** `featureUsers` chỉ là ảnh chụp hiện tại, không theo kỳ.
- **D20 (sửa):** cache `summary` 10 phút trong Redis.

## 4. Các bước (TDD, một commit mỗi task)

0. Mốc test trên `88b5049` (§6).
1. Quyền `ViewProductKpi` + migration + test quyền mặc định (OpsManager có, Staff không, `GET /admin/role-permissions` có trong catalog).
2. Phần thuần: `KpiRange` (kiểm `from/to/granularity`), `KpiPeriods` (chia kỳ, `partial`), `KpiAggregation` (dòng chảy, tỉ lệ), `SmallCellMask`, `KpiCount` + JSON converter. Unit test.
3. `IKpiRepository` + `KpiRepository` (truy vấn GROUP BY theo ngày VN, theo kỳ cho người khác nhau và người trả phí, ảnh chụp tính năng). Integration test với Postgres: biên ngày VN (23:30 UTC thuộc ngày VN hôm sau), loại tài khoản nội bộ, giao dịch nháp/đã xoá/nguồn thông báo.
4. `KpiSummaryService` + cache Redis 10 phút + `OpsKpiController`. Integration quyền: 403 Staff, 403 Staff đã được cấp `ViewDashboard`, 403 user thường, 403 chưa qua 2FA (`OpsAuthOptions(true)`), 200 OpsManager và Admin, 200 Staff sau khi được cấp `ViewProductKpi`, 422 khoảng ngày.
5. Kịch bản đầu-cuối §5.
6. Tài liệu (§2.5).
7. Toàn bộ test, so với mốc.

## 5. Kịch bản đầu-cuối (bắt buộc pass)

3 tuần (21 ngày VN), 20 user `role = User` đăng ký rải rác (một số chưa xác minh, một số chưa onboarding, một người xin xoá), cộng 2 tài khoản nội bộ có giao dịch (không được tính); giao dịch đủ nguồn hiện có, có nháp, có giao dịch đã xoá; thông báo có `Ignored`/`Duplicate`; 3 người trả phí (2 gói, một gói chỉ 1 người); một đơn `NeedsReview`, một đơn `Voided`.

- `summary` theo ngày: từng chỉ số đúng với số tính tay; `payingByPlan` của gói 1 người là `"<5"`, `payingTotal = 3` không che; doanh thu không có đơn `Voided`.
- `summary` theo tuần: dòng chảy là tổng; `transactingUsers` không đếm trùng người hoạt động nhiều ngày; `partial` đúng.
- Quét JSON của mọi response: không có id nào trong 22 tài khoản, không có email, không có `amount_cents` của từng đơn.

## 6. Mốc test

Mốc trên `88b5049` (podman, `--pids-limit=-1`, 24 phút): backend **4044/4046**, 2 đỏ sẵn trên `main` (giống mốc Phase 92):
- `MethodSelectionOnSeedDataTests.TheExampleProfileInTheMdFile_GetsCashFlowBudget_WithASmallEmergencyBufferAsSupport`
- `NotificationsControllerTests.Analyze_HighConfidenceFinancialNotification_AutoCommitsAndUpdatesBalance`

ai-service không đổi trong phase này nên không chạy lại.

## 7. Báo nhóm web admin (bản nháp, chép vào TASKS.md khi xong)

- Quyền mới `ViewProductKpi` (OpsManager, Admin; Admin cấp thêm cho Staff được). Menu KPI chỉ hiện khi `/ops/me` có quyền này.
- Gợi ý biểu đồ: cột đăng ký mới, chồng với số đã xác minh và đã hoàn thành onboarding; đường `transactingUsers` theo ngày/tuần (ghi chú rõ đây **không phải** DAU); biểu đồ tròn nguồn giao dịch (ô `"<5"` gộp vào "Khác"; `manual` gồm cả chat); thẻ số doanh thu (tổng, định dạng đồng) và số người trả phí; thẻ ảnh chụp `featureUsers`.
- Giá trị số có thể là chuỗi `"<5"`; `null` (chỉ ở `conversionRate`) nghĩa là không có mẫu số.

## 8. Báo nhóm app (bản nháp)

- App chưa gọi `PUT /devices/listener-status` (Phase 84), nên server chưa biết dịch vụ đọc thông báo có còn chạy không; độ phủ dữ liệu (và chỉ số độ phủ, nếu sau này thêm vào KPI) sẽ luôn là `unknown`.
