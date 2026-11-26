# Phase 92 — Trả góp: kỳ cuối cho khoản định kỳ

> Trạng thái: **đã duyệt 2026-11-26**. D1, D2, D4, D5, D7, D9, D11–D20 theo đề xuất; D6 chọn (a); S1 thay D3, S2 sửa D8, S3 sửa D10 (S thắng D và phần thân khi mâu thuẫn). Nhánh `feat/recurring-installments` từ `main` @ `96ac3e8` (đã có Phase 90, 91).
> Luật nghiệp vụ BR-INS-01…10 do chủ sản phẩm chốt; kế hoạch này chỉ thêm kết quả đọc code, cách làm và các điểm D1–D20.

## Đã duyệt (2026-11-26): điều chỉnh S1–S3

- **S1 (thay D3): kỳ quá hạn của khoản có `totalOccurrences` không tiêu kỳ.** Dự báo chiếu đủ `remainingOccurrences` kỳ: lấy `remaining` ngày đầu của lịch anchor tính từ `NextDueDate`, ngày nào < hôm nay (kể cả quá `overdueGraceDays`) chiếu vào **ngày mai**, các ngày sau giữ lịch anchor; `finalDueDate` = ngày cuối của dãy đó (lùi theo khi còn kỳ quá hạn, không bao giờ trước ngày mai). Kỳ đã thấy trả (`FindPayment`) vẫn bỏ và vẫn tính là một kỳ trong `remaining`. Khoản có `totalOccurrences` **không** vào luồng `LongOverdue` (tự tạm dừng + "còn dùng không?"); thay vào đó `BillReminderRule` trả loại mới `Overdue`, gửi **một lần cho mỗi kỳ hạn** vào sáng đầu tiên sau ngày đến hạn mà chưa thấy trả: kind `BillDue` có sẵn (không đổi enum hay CHECK của `forecast_alerts`), dedup `bill_overdue:{itemId}:{dueDate}`, tiêu đề "Quá hạn {tên khoản}", thân chỉ có số tiền và ngày như nhắc hạn hiện tại. Khoản chỉ có `endDate` và khoản không kỳ cuối giữ hành vi cũ (bỏ kỳ quá ân hạn, chiếu kỳ trong ân hạn vào hôm nay, `LongOverdue`). Kịch bản (j) mới.
- **S2 (sửa D8): backfill `anchor_day`.** Khoản tháng: có `last_seen_at` (đổi sang ngày giờ VN) → nếu cả `next_due_date` và ngày của `last_seen_at` đều là ngày cuối tháng của chúng thì 31, ngược lại max(ngày của `next_due_date`, ngày của `last_seen_at`); không có `last_seen_at` → cách của D8 (ngày cuối tháng → 31, còn lại → ngày). Viết bằng SQL trong migration. Test migration: ngày 30 đến hạn 30/11 có lần trả 30/10 → 30; cuối tháng (trả 31/10, đến hạn 30/11) → 31; ngày 28 đến hạn 28/02 có lần trả 28/01 → 28.
- **S3 (sửa D10): chạy tracker ngay sau giao dịch.** Sau khi một giao dịch được xác nhận (confirm, tạo tay, gộp), nếu user có khoản định kỳ `Active` cùng chiều thì enqueue một job Hangfire chạy tracker cho riêng user đó (`IRecurringTrackingTrigger`, tham số constructor tuỳ chọn như `IForecastAlertTrigger`, chỉ enqueue, không bao giờ ném); không chạy trong request. Chống trùng bằng khoá theo user (Redis `SET NX` có hạn, job đang chờ/chạy thì không enqueue thêm; job tự giữ khoá phân tán khi chạy để hai lượt không cùng ghi liên kết). Job 01:30 giữ nguyên. `RecurringDetectionService` tách phần tracker thành `TrackForUserAsync` để job mới chỉ chạy tracker (không phát hiện khoản mới). Kịch bản (b) thêm assert: `paidOccurrences` và `Completed` cập nhật sau khi job này chạy, không cần job đêm.

## 1. Kết quả "Đọc trước" (đã kiểm trong code)

### 1.1 Mọi chỗ chiếu khoản định kỳ ra tương lai

Gốc chung: `RecurringItem.ToView()` → `RecurringItemView` (`ForecastInputs.cs`), rồi lặp `RecurringSchedule.Next` từ `NextDueDate`. Có **ba vòng lặp lịch** riêng và **ba chỗ chỉ đọc `NextDueDate`**:

| # | Chỗ | Cách chiếu | Ai dùng | Đã lọc trạng thái |
|---|---|---|---|---|
| P1 | `CashflowEngine.RecurringEvents` → `DueDates` (vòng lặp riêng) | từ `NextDueDate`, bỏ lần quá `overdueGraceDays`, tới `end`; lần đã thấy trả (`RecurringMatcher.FindPayment`) thì bỏ | `ForecastService` (`/forecast/cashflow`, `/scenario`, `/forecast/spendable`), `PurchaseCheckService` (qua `GetCashflowAsync`/`RunScenarioAsync`) + `SimulatePurchaseTool` của Agent, `LowBalanceRule`/cảnh báo dự báo (`ForecastAlertService.CheckUserAsync`), `BudgetForecastService` → `BudgetRiskCalculator` (phần định kỳ còn lại trong kỳ) → `CategoryForecast`/lưu ngân sách tháng sau, `IncomeEstimator.Upcoming` (`/forecast/income`) | `ListAsync(Active)` |
| P2 | `RecurringSchedule.Occurrences` (vòng lặp thứ hai) | từ `NextDueDate` trong `[from, to]` | `SpendableCalculator.Period` (ngày nhận thu nhập kế tiếp quyết định kỳ tính spendable) | Active |
| P3 | `BillReminderRule.Evaluate` | chỉ `NextDueDate`: nhắc trước `billReminderDaysBefore`, quá ân hạn → `LongOverdue` (tạm dừng + hỏi) | `ForecastAlertService.RunDailyAsync` 08:00 | Active |
| P4 | `IncomeEstimator.Late` | chỉ `NextDueDate`: thu quá `incomeLateDays` chưa về | `/forecast/income`, cảnh báo `income_late` 08:00 | Active |
| P5 | `UpcomingItemsTool` (Agent, `ManagementTools.cs`) | chỉ `NextDueDate` trong N ngày | Agent | Active |
| P6 | `RecurringTracker.Advance` (vòng đuổi kịp) | đẩy `NextDueDate` từng kỳ khi thấy trả; đổi giá | `RecurringDetectionService` (job 01:30 **duy nhất**) | Active/Suggested/Paused |
| P7 | `RefundFollowUps.RewindRecurringAsync` | `RecurringSchedule.Previous` khi khoản trả bị đảo (Phase 81 R1) | `RefundLedger` | mọi khoản theo `LastSeenAt` |

Không chiếu nhưng **bị ảnh hưởng khi khoản kết thúc** (dùng danh sách khoản để *loại* giao dịch khớp, tránh trừ hai lần): `SpendingBaselineCalculator.Compute`, `BudgetRiskCalculator` (chi linh hoạt), `IncomeEstimator.Irregular` và `ForecastService.IrregularIncomeAsync` (`items.Any(Inflow)` tắt thu không đều), `AnomalyDetector`. Xem D5.

Không đọc khoản định kỳ (đã kiểm):
- **Agent `runway`, `stress_test`, `sensitivity`**: dùng `IFinancialDataSource.GetMonthlySeriesAsync` (trung bình chi/thu theo tháng từ lịch sử). Chúng "trừ tiền trả góp" gián tiếp vì lịch sử có các lần trả, không qua `recurring_items`. Xem D6.
- **Ngày đạt mục tiêu** (`GoalForecastService`/`GoalForecastCalculator`, `GoalSimulator`): tính từ nhịp góp của mục tiêu, không đọc khoản định kỳ. Không cần sửa.
- `TransferRecurringCheck`: chỉ hỏi "còn dùng không?" khi lần trả gần nhất thành chuyển ví; không chiếu.

### 1.2 Ràng buộc duy nhất của `recurring_items` và dòng bị từ chối/huỷ

- Index duy nhất `uq_recurring_items_user_direction_counterparty` trên `(user_id, direction, counterparty_key)` `WHERE counterparty_key IS NOT NULL AND deleted_at IS NULL`. Không có ràng buộc nào khác (CHECK: direction, cadence, status `suggested|active|paused|rejected|cancelled`, source, `amount_cents > 0`, `occurrences >= 0`).
- Từ chối / huỷ **giữ dòng** (không xoá mềm) để giữ chỗ trong index → `RecurringDetector` nhận tập `known` gồm mọi `(direction, key)` hiện có nên không đề xuất lại.
- `RecurringItemService.CreateAsync`: tìm bằng `FindByCounterpartyAsync` (không lọc trạng thái); `Active`/`Paused` → 409 `RECURRING_DUPLICATE`; `Suggested`/`Rejected`/`Cancelled` → **dùng lại dòng đó** (ghi đè thông tin, chuyển `Active`, `Source = Manual`).
- `GET /recurring-items` không truyền `status` → mọi khoản trừ `Cancelled`/`Rejected`.

### 1.3 `RecurringTracker` có lưu giao dịch đã khớp không

**Không.** Chỉ lưu `Occurrences` (đếm), `LastSeenAt`, `LastSeenAmountCents` và đẩy `NextDueDate`. Chống khớp lại bằng điều kiện `payment.TransactedAt <= LastSeenAt`. Hệ quả:
- Xoá/sửa/gộp giao dịch đã khớp **không** đổi gì ở khoản định kỳ (không đường nào cập nhật). Chỉ đảo giao dịch (Phase 81 R1) lùi lại, và phải đoán bằng `LastSeenAt == original.TransactedAt`.
- Tracker chỉ chạy trong `RecurringDetectionJob` (01:30). Ban ngày, engine tự coi kỳ là đã trả nhờ `FindPayment` trên giao dịch gần đây.
- `ForecastTransaction` đã có `Id`, nên tracker trả về được cặp (kỳ, giao dịch) mà không đổi truy vấn.

### 1.4 Lỗi ngày 28 (§13.1)

`RecurringItemMapping.PreferredDay` suy ngày gốc từ `NextDueDate`: đến hạn đúng ngày cuối tháng → coi là "cuối tháng" (31). Khoản ngày 28 đến hạn 28/02 năm không nhuận → 31/03. Test duy nhất gắn với hành vi này: `RecurringTrackerTests.EndOfMonthBill_ReturnsToTheLastDayAfterAShortMonth` (28/02/2027 → 31/03/2027). `RecurringScheduleTests` truyền `preferredDay` tường minh nên không bị ảnh hưởng.

## 2. Thiết kế

### 2.1 Dữ liệu (migration `AddRecurringInstallments`)

`recurring_items` thêm (đều tuỳ chọn, không phá app cũ):

| Cột | Kiểu | Ràng buộc |
|---|---|---|
| `total_occurrences` | int null | 1–600 |
| `end_date` | date null | |
| `anchor_day` | smallint null | 1–31; `anchor_day IS NULL OR cadence = 'monthly'` |
| `prepaid_occurrences` | int not null default 0 | ≥ 0 |
| `completed_at` | timestamptz null | `(status = 'completed') = (completed_at IS NOT NULL)` |
| `settled_early` | bool not null default false | `NOT settled_early OR status = 'completed'` |

CHECK `NOT (total_occurrences IS NOT NULL AND end_date IS NOT NULL)`; `chk_recurring_items_status` thêm `completed` (9 ký tự, vừa `varchar(10)`).
Index duy nhất đổi filter thành `counterparty_key IS NOT NULL AND deleted_at IS NULL AND status <> 'completed'` (BR-INS-09).
Backfill `anchor_day` cho `cadence = 'monthly'` theo D8.

Bảng mới `recurring_item_payments` (D1):

| Cột | Ghi chú |
|---|---|
| `id` uuid PK | |
| `user_id` uuid FK `users` cascade | mọi truy vấn lọc theo user |
| `recurring_item_id` uuid FK cascade | |
| `transaction_id` uuid null FK `transactions` cascade | null = trả tiền mặt không ghi sổ |
| `due_date` date | kỳ hạn mà lần trả này phủ |
| `kind` | `matched` (tracker) / `manual` (đánh dấu tay) / `settlement` (tất toán) |
| `created_at`, `deleted_at` | gỡ = xoá mềm |

Index duy nhất: `(transaction_id) WHERE transaction_id IS NOT NULL AND deleted_at IS NULL` (một giao dịch chỉ phủ một khoản định kỳ một lần); `(recurring_item_id, due_date) WHERE deleted_at IS NULL AND kind <> 'settlement'` (tối đa một lần trả mỗi kỳ hạn — BR-INS-03 ở mức DB).

**Số kỳ đã trả** = số dòng còn sống (`deleted_at IS NULL` và giao dịch null hoặc chưa xoá) + `prepaid_occurrences`. Không lưu bộ đếm. Repository đếm bằng một truy vấn `GROUP BY` và gắn vào thuộc tính không ánh xạ `RecurringItem.PaidOccurrences` ở `GetAsync`/`ListAsync`/`ListAllTrackedAsync`, để `ToView()` có số mà không phải sửa từng nơi gọi. `occurrences` cũ giữ nguyên nghĩa (số lần bộ phát hiện thấy, dùng cho tự ghi nhận), tài liệu ghi rõ khác `paidOccurrences`.

### 2.2 Lịch và số kỳ chiếu

- `RecurringSchedule.DayOf(item)`: tháng → `anchor_day ?? PreferredDay(cũ)`; năm → `PreferredDay(cũ)`; tuần → null. Mọi chỗ gọi `PreferredDay` (tracker, `ToView`, `RefundFollowUps`) chuyển sang hàm này.
- `RecurringItemView` thêm `int? RemainingOccurrences` (null = không có kỳ cuối theo số kỳ) và `DateOnly? EndDate`.
- Một hàm duy nhất `RecurringSchedule.DueDatesFrom(item)` sinh lịch từ `NextDueDate`, dừng khi đã sinh `RemainingOccurrences` kỳ hoặc vượt `EndDate`. P1 và P2 dùng hàm này (bỏ hai vòng lặp riêng), nên cap áp ở một chỗ. Lần thứ *i* tính từ `NextDueDate` là kỳ `paid + i` (D3).
- P3, P4, P5: bỏ qua khoản có `RemainingOccurrences <= 0` hoặc `NextDueDate > EndDate`. P3 với khoản có `totalOccurrences`: không `LongOverdue`, có `Overdue` (S1).
- S1: với `RemainingOccurrences` có giá trị, `DueDatesFrom` không bỏ kỳ quá ân hạn; engine dời mọi kỳ < hôm nay sang ngày mai. Khoản không có `totalOccurrences` đi nhánh cũ.
- Khoản không có kỳ cuối: `RemainingOccurrences = null`, `EndDate = null` → mọi hàm chạy đúng như cũ (BR-INS-02).

### 2.3 Trạng thái (`InstallmentProgress`, hàm thuần)

Từ `(total, endDate, prepaid, paid, NextDueDate, cadence, anchor, today)` tính: `paidOccurrences`, `remainingOccurrences`, `remainingAmountCents` (= còn lại × `amount_cents`, ước tính), `finalDueDate`, `ShouldComplete`, `ShouldReopen`. Mọi đường làm đổi số kỳ đã trả (tracker, đánh dấu tay, gỡ, PATCH `prepaidOccurrences`/`totalOccurrences`/`endDate`, sync giao dịch) gọi một hàm `RecurringCompletion.Apply(item, now)` trước khi lưu, trong cùng lượt lưu.

- Có `total`: đã trả ≥ tổng → `Completed`, `completed_at = now`. Gỡ làm < tổng → `Active`, xoá `completed_at` (không mở lại khoản `settled_early`, phải `PATCH status`).
- Có `end_date`: `Completed` khi `NextDueDate > end_date` (mọi kỳ đã trả) hoặc hôm nay > `end_date` (D9).

### 2.4 API

- `POST /recurring-items`, `PATCH /{id}`, `POST /{id}/confirm` (thêm body tuỳ chọn), `POST /from-transaction/{transactionId}`: nhận `totalOccurrences`, `endDate`, `anchorDay`, `prepaidOccurrences`. Cả `total` và `endDate` → 400. `anchorDay` với chu kỳ khác tháng → 400. PATCH gửi `null` tường minh để bỏ kỳ cuối: dùng cờ `clearEnd: true` (D17) vì record hiện coi null là "giữ nguyên".
- `PATCH` thêm `status` (chỉ nhận `Active`, chỉ từ `Completed` có `settled_early`; khác → 409 `RECURRING_STATE`).
- `POST /{id}/payments` `{transactionId?}` → 201 + DTO khoản (kèm `paymentId`). Giao dịch phải của user (khác → 404), đã xác nhận, đúng chiều (khác → 422 `RECURRING_PAYMENT_INVALID`), chưa phủ khoản định kỳ nào (→ 409 `RECURRING_PAYMENT_LINKED`). Ghi một kỳ `due_date = NextDueDate`, đẩy `NextDueDate` một kỳ. Khoản `Completed`/`Rejected`/`Cancelled` → 409.
- `DELETE /{id}/payments/{paymentId}` → 204; gọi lại → 204 (idempotent); id của người khác → 404.
- `POST /{id}/settle` `{transactionId?}`: không có kỳ cuối → 422 `RECURRING_NO_FINAL`; đã `Completed` → 200 với DTO như cũ (không ghi thêm); thành công → `Completed`, `settled_early = true`, nếu có giao dịch thì thêm dòng `settlement` (tính một kỳ trả, không ép số tiền).
- `GET /recurring-items?status=Completed`. Mặc định (không `status`) **có** `Completed`? Xem D16.
- `RecurringItemDto` thêm ở cuối: `totalOccurrences`, `prepaidOccurrences`, `paidOccurrences`, `remainingOccurrences`, `remainingAmountCents`, `endDate`, `anchorDay`, `finalDueDate`, `completedAt`, `settledEarly`. Mã lỗi mới vào `ForecastErrorCodes`.

### 2.5 Giữ liên kết khớp với giao dịch (BR-INS-03)

`IRecurringPaymentSync` (tham số constructor tuỳ chọn như `ILoanLinkSync`), gọi trước khi lưu, cùng lượt lưu:

| Đường | Việc |
|---|---|
| `DeleteTransactionCommandHandler` | xoá mềm dòng liên kết, lùi `NextDueDate` một kỳ (D4), `RecurringCompletion.Apply` |
| `RefundFollowUps` (đảo, R1) | như xoá; thay phép đoán `LastSeenAt` bằng liên kết khi có (giữ phép đoán cho khoản chưa có liên kết) |
| `MergeTransactionsCommand` | liên kết của bản bị gộp chuyển sang bản giữ lại; nếu bản giữ lại đã có liên kết thì bỏ của bản bị gộp (mất một kỳ, tính lại) |
| `ConvertToTransferCommand`, `GroupFundSourceConverter` | như xoá (D12) |
| `UpdateTransactionCommandHandler` | giữ liên kết, trừ khi đổi chiều tiền (D11) |

`RecurringTracker.Advance` thêm đầu ra `ICollection<RecurringMatch>` (kỳ, giao dịch); bỏ qua giao dịch đã có liên kết sống; `RecurringDetectionService` thêm dòng `matched` và gọi `RecurringCompletion.Apply`. Không bao giờ khớp cho khoản `Completed`. Tối đa một giao dịch mỗi kỳ là có sẵn trong vòng lặp (khớp xong thì đẩy sang kỳ sau) và có index duy nhất chặn ở DB.

### 2.6 Bảo mật, log

Không log số tiền, tên khoản, `counterparty_key`. Mọi hàm repository mới lọc `userId`; dòng thanh toán lấy theo `(paymentId, itemId, userId)`. Xoá cứng user: cả hai bảng cascade theo `users`, không có số tính sẵn mới (không cần sửa `UserHardDeleter`, thêm test).

## 3. Cần quyết (mỗi mục có đề xuất)

- **D1 Lưu liên kết ở đâu.** (a) `transactions.recurring_item_id`; (b) bảng `recurring_item_payments`. **Đề xuất (b)**: BR-INS-04 cần ghi kỳ *không có giao dịch* (tiền mặt) và tất toán — một cột trên `transactions` không biểu diễn được; bảng có `due_date` cho phép index "một lần mỗi kỳ"; không chạm bảng `transactions` vốn đã nhiều cột và nhiều đường ghi.
- **D2 Liên kết cho mọi khoản hay chỉ khoản có kỳ cuối.** **Đề xuất: mọi khoản** (một đường code; thêm kỳ cuối cho một khoản đang theo dõi thì các lần trả từ giờ đã được đếm). Khoản không kỳ cuối không đọc số này nên hành vi không đổi. Các lần trả *trước* Phase 92 không có liên kết: user nhập `prepaidOccurrences`.
- ~~**D3**~~ *Thay bằng S1.* Bản cũ: **Kỳ quá hạn có tiêu kỳ không.** Lần thứ *i* từ `NextDueDate` là kỳ `paid + i`, theo lịch hợp đồng. Kỳ quá hạn bị bỏ khỏi dự báo vì quá `overdueGraceDays` (như cũ) **vẫn tiêu một kỳ** trong số còn lại, nên dự báo không đẩy kỳ cuối ra sau. **Đề xuất: như vậy** (đúng nghĩa "không vượt số kỳ còn lại"; phạt chậm và dời lịch nằm ngoài phạm vi).
- **D4 Gỡ một kỳ thì `NextDueDate` thế nào.** **Đề xuất: lùi đúng một kỳ** (`RecurringSchedule.Previous` theo `anchor_day`), giống Phase 81 R1, giữ bất biến "NextDueDate = kỳ thứ paid+1". Gỡ một kỳ ở giữa (vd. xoá kỳ 10 khi 11, 12 còn) cũng chỉ lùi một kỳ; ghi rõ là giới hạn trong tài liệu.
- **D5 Sau khi `Completed`, các lần trả cũ quay lại "thói quen chi tiêu".** Lịch sử 90 ngày còn 3 lần −1,2tr; nếu khoản không còn trong danh sách loại trừ, baseline chi linh hoạt tăng ~40k/ngày, dự báo vẫn trừ trả góp dưới dạng khác. Chiều thu tương tự: học bổng xong thành "thu không đều" và thổi phồng mức thận trọng. **Đề xuất:** danh sách *loại trừ* (`SpendingBaseline`, `BudgetRiskCalculator`, `IncomeEstimator.Irregular`, `AnomalyDetector`) = Active ∪ Completed có `completed_at` trong cửa sổ lịch sử; danh sách *chiếu* giữ Active. `IrregularIncomeAsync` vẫn chỉ xét Active. Hai danh sách tách rõ trong `ForecastService` để không lẫn.
- **D6 (đã chọn a)** Agent `runway`/`stress_test`/`sensitivity`.** Chúng không đọc `recurring_items` (1.1). (a) Ngoài phạm vi, ghi §13.4; (b) `IFinancialDataSource` thêm "khoản chi cố định sắp kết thúc" (số tiền, tháng kết thúc), `RunwayTool`/`StressTestTool` trừ số đó khỏi chi tháng kể từ tháng sau `finalDueDate`; `sensitivity` gọi lại runway nên theo luôn. **Đề xuất (b)**, giới hạn ở khoản chi có kỳ cuối; thêm khoảng 1 task. Nếu muốn giữ phase gọn thì chọn (a).
- **D7 Ngày đạt mục tiêu.** Không đọc khoản định kỳ, **không sửa**; ghi trong báo cáo.
- **D8 (sửa bởi S2)** Suy `anchor_day` khi migrate. **Đề xuất:** `next_due_date` là ngày cuối tháng → 31 (giữ đúng lịch hiện tại của khoản "cuối tháng", vd. lương), còn lại → ngày của `next_due_date`. Khoản tạo/sửa mới: `anchor_day` mặc định = ngày của `nextDueDate`, **không** đoán "cuối tháng" (đây là phần sửa lỗi 28). Khoản ngày 30 đang đến hạn 30/11 sẽ bị coi là cuối tháng; user sửa bằng PATCH. Ghi §13.6.
- **D9 Khi nào khoản `end_date` thành `Completed`.** **Đề xuất:** khi `NextDueDate > end_date` (đã trả hết các kỳ) hoặc hôm nay > `end_date` (đúng chữ BR-INS-06). Kỳ cuối quá hạn mà qua `end_date` thì không còn được chiếu trong ân hạn; chấp nhận.
- **D10 (sửa bởi S3)** Tracker chạy lúc nào. Chỉ job 01:30 (như cũ). Ban ngày dự báo vẫn đúng ngay (engine bỏ kỳ đã thấy trả, và cap đếm cả kỳ đó), trạng thái `Completed` lật vào đêm đó. **Đề xuất: giữ job**, đánh dấu tay/gỡ/tất toán/sync giao dịch cập nhật ngay. Kịch bản (b) chạy `RecurringDetectionService` trong test.
- **D11 Sửa giao dịch đã khớp.** **Đề xuất:** giữ liên kết khi sửa số tiền, ngày, người nhận, danh mục; gỡ khi đổi chiều (chi ↔ thu, thành Transfer). User tự gỡ bằng `DELETE .../payments/{id}` nếu muốn.
- **D12 Chuyển thành Transfer / chi quỹ nhóm.** **Đề xuất: gỡ liên kết** (tiền không trả cho người nhận nữa). `TransferRecurringCheck` giữ nguyên cảnh báo của nó.
- **D13 Tất toán có giao dịch, rồi mở lại.** **Đề xuất:** dòng `settlement` giữ lại và vẫn tính một kỳ; user gỡ được bằng `DELETE .../payments/{id}`.
- **D14 Đánh dấu tay cho khoản không có kỳ cuối** (Netflix trả tiền mặt). **Đề xuất: cho phép** (đẩy kỳ, không nhắc nhầm); chỉ chặn `Completed`/`Rejected`/`Cancelled`; `Suggested` → 409 (phải xác nhận trước).
- **D15 Xuất dữ liệu (Phase 91).** **Đề xuất:** `recurring_items.json` thêm các trường mới; file mới `recurring_item_payments.json` (`recurringItemId`, `transactionId`, `dueDate`, `kind`, `createdAt`, `deletedAt`). Cập nhật `PackageReadme` và danh mục bảng của kế hoạch Phase 91.
- **D16 Danh sách mặc định có `Completed` không.** Mặc định hiện ẩn `Cancelled`/`Rejected`. **Đề xuất: ẩn cả `Completed`** khỏi mặc định (app cũ không biết trạng thái này, tránh hiện khoản đã xong lẫn khoản đang chạy); xem bằng `status=Completed`.
- **D17 Bỏ kỳ cuối bằng PATCH.** **Đề xuất:** cờ `clearEnd: true` xoá cả `total_occurrences` và `end_date` (record hiện coi null = giữ nguyên, không phân biệt được "gửi null"). Gửi cờ cùng một trong hai trường → 400.
- **D18 Kiểm hợp lệ khi tạo.** **Đề xuất:** `prepaidOccurrences ≥ totalOccurrences` → 400 khi tạo (không có gì để theo dõi); khi PATCH thì cho và khoản thành `Completed`. `endDate < nextDueDate` → 400. `endDate` tối đa 50 năm sau hôm nay (khớp trần 600 kỳ tháng). `totalOccurrences` áp cho mọi chu kỳ; `anchorDay` chỉ tháng.
- **D19 Khoản `Paused` có kỳ cuối.** Giữ luồng cũ (tạm dừng khi quá hạn lâu, không chiếu). `resume` không đổi số kỳ. **Đề xuất: không thêm gì.**
- **D20 Test sửa kỳ vọng.** Chỉ một: `RecurringTrackerTests.EndOfMonthBill_ReturnsToTheLastDayAfterAShortMonth` — đặt tường minh `AnchorDay = 31` (khoản "cuối tháng" vẫn về 31/3), thêm test mới anchor 28 → 28/03. Code giữ cách suy cũ khi `anchor_day` null (chỉ còn ở entity dựng trong test, vì migration điền mọi khoản tháng và đường tạo luôn đặt), nên các test khác không đổi. Nếu khi chạy có test nào khác đổi, sẽ liệt kê và giải thích trong báo cáo, không tự sửa kỳ vọng.

## 4. Các bước (TDD, một commit mỗi task)

0. **Mốc.** Chạy toàn bộ backend + ai-service trên nhánh trước khi sửa; ghi test đỏ có sẵn vào mục 6.
1. **Domain + migration.** `RecurringStatus.Completed`, các cột, `RecurringItemPayment` + cấu hình, CHECK, index, backfill `anchor_day` (S2, ba ca test). Test cấu hình/migration (CHECK chặn cả `total` và `end_date`; index cho phép Completed + Active cùng người nhận). `docs/ERD.md`.
2. **Lịch.** `RecurringSchedule.DayOf`, anchor ở `Next`/`Previous`. Unit: anchor 15/28/29/30/31 qua tháng 2 năm nhuận (2028) và không nhuận (2027); khoản tuần không đổi; kịch bản (d). Sửa test D20.
3. **Số kỳ chiếu.** `RecurringItemView` + `DueDatesFrom`; P1–P5 dùng nó. Unit: cap theo số kỳ, theo `end_date`, kỳ quá hạn không tiêu kỳ và dời sang ngày mai (S1), không `LongOverdue` mà `Overdue` một lần mỗi kỳ, khoản thu dừng đúng kỳ; hồi quy khoản không kỳ cuối (6 lần trong 6 tháng, như cũ).
4. **`InstallmentProgress` + `RecurringCompletion` + đếm ở repository.** Unit: hoàn thành, mở lại, `end_date`, prepaid, `remainingAmountCents`, `finalDueDate`.
5. **Tracker ghi liên kết** (+ S3: `TrackForUserAsync`, `IRecurringTrackingTrigger`, job Hangfire theo user có khoá; gọi từ confirm, tạo tay, gộp). Đầu ra `RecurringMatch`, bỏ giao dịch đã liên kết, detection service lưu + hoàn thành; detector không đề xuất lại người nhận có dòng `Completed`. Unit: (b), (g), Completed không bị khớp/đổi giá.
6. **Service + API.** Trường mới ở create/patch/confirm, `payments` POST/DELETE, `settle`, lọc `Completed`, DTO, mã lỗi. Integration: 404 id người khác (khoản và payment), 400 (cả hai trường, anchor sai chu kỳ, ngoài khoảng), 409 (giao dịch đã liên kết, trạng thái), idempotency (`settle` hai lần, `DELETE` hai lần); (f), (h), (i).
7. **`from-transaction`** nhận trường mới, giữ hành vi cũ khi không gửi. Test hai nhánh.
8. **Sync giao dịch.** `IRecurringPaymentSync` ở năm đường (2.5) + `RefundFollowUps`. Unit + integration: xoá, đảo, gộp (chuyển liên kết, trùng liên kết), chuyển Transfer, sửa đổi chiều; (e).
9. **Loại trừ sau khi hoàn thành (D5).** Unit: baseline không tăng sau khi `Completed`; thu không đều không tăng sau học bổng.
10. **Xuất dữ liệu (D15).**
11. **Kịch bản đầu-cuối (a)–(j)** dạng integration trên API thật (Postgres test).
12. **Tài liệu**: REQUIREMENTS (FR-FCST-09 cập nhật; FR-FCST-17 mới cho trả góp; §13.1 bỏ dòng ngày 28; §13.4 "Trả góp — phần còn lại" kèm Agent `runway`/`stress_test`/`sensitivity` chưa trừ khoản kết thúc (D6 a); §13.6 đổi hành vi anchor), `docs/ERD.md`, `docs/recurring-items-api.md` (mới — hiện chưa có tài liệu API riêng cho khoản định kỳ), `docs/transaction-extras-api.md` (`from-transaction`), `docs/data-export-api.md`, `.context/TASKS.md` Phase 92 kèm "Báo nhóm Flutter" và "Báo MinhTuan", `CLAUDE.md` một đoạn luật dễ phá.
13. **Toàn bộ test** backend + ai-service; không thêm test đỏ so với mốc; build không cảnh báo.

## 5. Kịch bản đầu-cuối (bắt buộc pass)

| | Dựng | Kỳ vọng |
|---|---|---|
| a | 12 × 1.200.000, `prepaidOccurrences` 9, anchor 15 | `cashflow` 6 tháng: đúng 3 sự kiện −1.200.000 (ngày 15); spendable trừ đúng kỳ trong kỳ tính; `remainingAmountCents` 3.600.000 |
| b | (a) + ba giao dịch khớp kỳ 10, 11, 12 | sau job tracker theo user (S3, không chạy job đêm): `paidOccurrences` 12, `Completed`, `completedAt` có; `RunDailyAsync` không nhắc; cashflow không còn khoản |
| c | Netflix 260.000/tháng, không kỳ cuối | 6 sự kiện trong 6 tháng như trước; giao dịch 280.000 → giá tự cập nhật, cảnh báo đổi giá như cũ |
| d | Đến hạn 31, tạo tháng 1 | 31/01, 28/02 (29/02 năm 2028), 31/03, 30/04 |
| e | Sau (b), xoá giao dịch kỳ 12 | `Active`, `paidOccurrences` 11, cashflow có lại một kỳ |
| f | Kỳ 11 trả tiền mặt, `POST .../payments` không `transactionId` | `paidOccurrences` +1, `nextDueDate` sang tháng sau |
| g | Hai giao dịch cùng người nhận, cùng số tiền, cùng kỳ | đúng một liên kết `matched`, `paidOccurrences` +1 |
| h | 12 kỳ, đã trả 6, `settle` | `Completed`, `settledEarly`, cashflow 6 tháng không còn khoản |
| i | Sau (b), `POST /recurring-items` cùng người nhận | 201, không 409 |
| j | 12 kỳ, còn 3, kỳ đầu quá hạn 20 ngày chưa trả | cashflow có đủ 3 lần −1.200.000 (một lần ngày mai, hai lần theo anchor); `RunDailyAsync` không tạm dừng, có một cảnh báo quá hạn |

## 6. Mốc test

Đo 2026-11-26 trên `feat/recurring-installments` @ `8d4095a` (trước khi sửa code):
- backend: **3950/3952** (23 phút 20 giây), không cảnh báo CS. Hai test đỏ có sẵn trên `main`, cùng hai test ở mốc Phase 90/91:
  `MethodSelectionOnSeedDataTests.TheExampleProfileInTheMdFile_GetsCashFlowBudget_WithASmallEmergencyBufferAsSupport`,
  `NotificationsControllerTests.Analyze_HighConfidenceFinancialNotification_AutoCommitsAndUpdatesBalance`.
- ai-service: **2255 passed, 483 skipped** (không có Postgres).

Luật: không thêm test đỏ so với mốc này.

## 7. Báo nhóm Flutter (bản nháp, chép vào TASKS.md khi xong)

- `RecurringItemDto` thêm ở cuối (đều có thể null/mặc định, app cũ bỏ qua được): `totalOccurrences`, `prepaidOccurrences`, `paidOccurrences`, `remainingOccurrences`, `remainingAmountCents` (ước tính = còn lại × số tiền), `endDate`, `anchorDay`, `finalDueDate`, `completedAt`, `settledEarly`. Trạng thái mới `completed`.
- Thanh tiến độ "đã trả X/Y kỳ" khi có `totalOccurrences`; khi chỉ có `endDate` hiện "còn N kỳ đến {finalDueDate}".
- Form tạo/sửa: "Số kỳ" **hoặc** "Ngày kết thúc" (không cả hai), "Đã trả trước khi dùng app", "Ngày trong tháng" (chỉ chu kỳ tháng). Bỏ kỳ cuối: `clearEnd: true`.
- Nút "Đánh dấu đã trả" (`POST .../payments`, có thể chọn giao dịch), danh sách kỳ đã trả có nút gỡ, nút "Đã tất toán" (`POST .../settle`), "Mở lại" (`PATCH status=Active`).
- Bộ lọc "Đã hoàn thành" (`status=Completed`); danh sách mặc định không còn trả khoản đã hoàn thành (D16).
- Mã lỗi mới: `RECURRING_PAYMENT_INVALID`, `RECURRING_PAYMENT_LINKED`, `RECURRING_NO_FINAL`.

## 8. Báo MinhTuan

`POST /recurring-items/from-transaction/{id}` (`TransactionExtrasService.CreateRecurringAsync`) nhận thêm `totalOccurrences`, `endDate`, `anchorDay`, `prepaidOccurrences`; không gửi thì như cũ. `anchorDay` mặc định theo ngày của giao dịch.
