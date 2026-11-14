# Phase 85 — Quỹ chung của nhóm

> Trạng thái: **đã làm xong** (2026-11-15), chờ duyệt merge. Đã duyệt 2026-11-14, phương án A. D1–D22 theo đề xuất, kèm S1–S7 bên dưới (S thắng D khi mâu thuẫn). Nhánh `feat/group-fund` tạo từ `main` @ `4601e1f` (đã có Phase 81–84, PR #30–#33).

## Đã duyệt (2026-11-14): điều chỉnh S1–S7

- **S1 (mốc test).** Kế hoạch Phase 84 ghi mốc 3012/3013, bản nháp này ghi 1352/1353. Trước Task 1: chạy lại toàn bộ `dotnet test` như Phase 84, giải thích chênh lệch, ghi mốc đúng vào mục Verification. Bộ lọc hoặc project thiếu thì sửa lệnh.
- **S2 (lỗi sẵn có, Task 0).** `FinanceSnapshotRepository` thiếu loại trừ `LoanSlug`, nên khoản cho vay/mượn bị tính vào `/me/spendable` và vào kế hoạch tháng mà công cụ Agent `simulate_purchase` đọc (`PurchaseCheckService.PlanImpactAsync` → `GoalPlanningService.GetSpendableAsync` → `GetDebitTotalAsync`). Sửa trong commit riêng ở đầu phase, kèm test: cho vay 2tr thì `/me/spendable` và purchase-check không coi là chi. Kiểm 12 chỗ còn lại: chúng đều **viết rời**, nên gom về `LoanExclusion` (`NotLoan` cho EF, `IsLoan` cho bộ nhớ) và `Predicates.And` để ghép vào `BudgetSpendScope.Counts`.
- **S3 (sửa D6 và BR-FUND-09 nửa đầu).**
  - Người có `ManageFunds` xoá dòng của **người khác**: **chỉ** xoá mềm dòng quỹ (số dư quỹ và ngân sách nhóm tính lại).
  - Giao dịch cá nhân của chủ dòng **giữ nguyên**, vẫn trỏ tới dòng đã xoá mềm (FK RESTRICT không cản vì là xoá mềm). `groupFundLink` thêm `entryDeleted: true`.
  - Push cho chủ dòng (không số tiền); activity `FundEntryDeleted`; audit `GroupFundEntryDeleted`. Chủ dòng tự xoá giao dịch cá nhân nếu muốn.
  - Chủ dòng tự xoá dòng của mình: vẫn xoá cả giao dịch cá nhân như cũ.
- **S4 (sửa D10).** `Expense` được gắn với sổ cá nhân của người ghi theo hai cách:
  - (a) `fromTransactionId`: một giao dịch `Debit` đã xác nhận của chính người ghi (không chia sẻ nhóm, không có `Relation`, không bị gộp, không `Suspected`). Giao dịch đó được **chuyển** thành `Transfer` gắn quỹ chiều `out`, và số tiền dòng = số tiền giao dịch. Tính lại ngân sách cá nhân và `daily_summaries` như Phase 83; ghi snapshot vào `transaction_merge_snapshots`.
  - (b) `financialAccountId`: tạo mới một `Transfer` gắn quỹ chiều `out`.
  - Xoá dòng `Expense` tạo theo (a) thì giao dịch về lại `Debit` với danh mục gốc (chi tiết ở D23, D24).
  - Kịch bản mới (i), xem Task 18.
- **S5 (sửa D14).** **Không** đổi `GroupMemberTotalsDto.UserId` thành nullable. `byMember` chỉ gồm người thật như cũ. Phần của người đã ẩn danh gom vào trường tuỳ chọn mới `formerMembers { totalSpentCents, fundSpentCents }` (null khi không có).
- **S6 (bổ sung D1).** Mọi chỗ đọc `CounterAccountId.Value`, `CounterAccount!`, `.CounterAccount.` hoặc join ví đích trên `Transfer` được liệt kê ở mục "Chỗ đọc ví đích", và mỗi chỗ có test với transfer gắn quỹ. `TransferLegs` dùng ở **mọi** chỗ đọc tác động số dư của transfer, kể cả `ForecastActuals` (qua boundary), dữ liệu khách của Ops, timeline.
- **S7 (bổ sung D11).** Ghi rõ vào `docs/REQUIREMENTS.md` §13.4 và `docs/group-api.md`: tiền góp quỹ chuyển vào tài khoản người giữ quỹ hiện bị tính là thu nhập của người đó; sẽ xử lý ở phase sau.

## Context

Nhóm hiện chỉ chia sẻ giao dịch: mỗi người ví riêng, số dư riêng, nhóm không có số dư. Phase này thêm **quỹ nhóm**: một sổ quỹ ảo thuộc về nhóm. FinMate không giữ tiền, chỉ ghi sổ. Ví cá nhân không bị chia sẻ. Góp hoặc rút có thể kèm ví cá nhân, khi đó sổ cá nhân có thêm một giao dịch tiền ra hoặc vào. Giao dịch này không phải chi tiêu, cũng không phải thu nhập.

**Dùng lại, không viết lại:**
- **Quyền:** `GroupPermissions.Effective`, `IGroupAccessService` (không phải thành viên → 404, thiếu quyền → 403).
- **Ngân sách nhóm:** `GroupFinanceRepository.GetMatchingBudgetsAsync` (khớp ngân sách), `BudgetSpendScope.Group` (phạm vi), `GroupLedger.RecomputeAsync` (tính lại kỳ, cảnh báo), `GroupAlertNotifier`.
- **Báo cáo nhóm:** `GroupReportService` + `ListReportRowsAsync`.
- **Dự báo:** `IForecastDataRepository.GetBoundaryTransfersAsync` và `BoundaryTransfers` (Phase 83).
- **Tên người:** `GroupMemberService.InactiveMemberName`. **Xoá cứng:** `UserHardDeleter`.
- **Đồng thời:** `xmin` + `IConcurrencyRetry` / `Retrying*`; khoá dòng giao dịch qua `RowLocked*` và `RunWithTransactionsLockedAsync`.
- **Thông báo:** hộp thư `forecast_alerts` (cách `GroupAlertNotifier` đang dùng).
- **Cờ:** `RequireGroupsEnabledAttribute`.

---

## Kết quả kiểm code

### 1. Mọi chỗ đang giả định ví thuộc về một user

Mô hình đã chọn **không** chia sẻ ví, nên không chỗ nào dưới đây phải đổi. Danh sách này để biết ranh giới và để Phase 87 hoặc một phase "ví chung" sau này dùng.

| # | Chỗ | Giả định |
|---|---|---|
| 1 | `financial_accounts.user_id NOT NULL`, FK `users` **ON DELETE CASCADE** (`FinancialAccountConfiguration:70`) | Ví có đúng một chủ; xoá chủ là xoá ví |
| 2 | `IFinancialAccountRepository`: `GetByIdAsync(id, userId)`, `GetListByUserAsync`, `ExistsByUserAndPackageNameAsync`, `GetByUserAndMonitoredPackageAsync` | Mọi lần đọc ví đều theo `userId` |
| 3 | `transactions.user_id` = chủ ví. Mọi handler giao dịch (`CreateManual`, `Confirm`, `Update`, `Delete`, `CreateRefund`, `CreateTransfer`, `ConvertToTransfer`, `MergeTransactions`, `Chat`, `ScanReceipt`) nạp ví bằng `(accountId, command.UserId)` | Người tạo giao dịch = chủ ví |
| 4 | `CreateTransferCommandHandler`, `ConvertToTransferCommand`, `TransferConversion`, `TransferMatcher`, `TransferSuggestions`, `TransferRecurringCheck`, `BalancePreview`, `MergeSnapshots` | Hai đầu chuyển khoản là ví **của cùng một người** |
| 5 | `DeleteFinancialAccountCommandHandler` | Ví còn giao dịch thì không xoá được (`HasTransactions`) |
| 6 | `AnalyzeNotificationCommandHandler`, `RetryFailedNotificationJob`, `ListenerHealthReader`, `ListenerHealthRepository`, `DataCoverageService` | Thông báo ngân hàng của một thiết bị thuộc ví của chính user đó |
| 7 | `ForecastService`, `ForecastAlertService`, `PurchaseCheckService`, `RecurringDetectionService`, `RecurringItemService`, `ForecastSettingsService`, `ForecastAccuracyService`, `ForecastDataRepository` | Tập ví được tính dự báo là ví của một user (`IncludeInForecast`) |
| 8 | `Agent/Forecasting/RepositoryFinancialDataSource`, `FinanceSnapshotRepository` | Số liệu Agent theo ví của một user |
| 9 | `DuplicateMatcher` / `MergePolicy` | `balance_after_cents` là số dư của một ví một người |
| 10 | `GroupShareRule.FinancialAccountId` (`GroupLedger.Matches`), `GroupCategoryService` | Quy tắc chia sẻ chỉ trỏ tới ví của chính người đặt |
| 11 | `GroupTransactionService` / `docs/group-api.md` | Thành viên không thấy ví và số dư của nhau |
| 12 | `SupportAccessService` / `IOpsCustomerDataService` | Nhân viên xem ví theo customer id lấy từ ticket |
| 13 | QR transfer intents (`QrTransferIntents.UserId`) | Ví gửi là của người quét |
| 14 | `UserHardDeleter` | Xoá user thì khoá ngoại xoá ví và giao dịch |

### 2. Các điểm quyết định thiết kế

1. **Ngân sách nhóm chỉ tính từ bảng `transactions`.** Có ba nơi tính lại từ đầu: `GroupFinanceRepository.SumSharedSpendAsync` (lúc tạo ngân sách), `SumSharedNetSpendAsync` (`GroupLedger.RecomputeAsync`), và `UserHardDeleter.RecalculateSpentAsync`. Nếu khoản chi từ quỹ nằm ở bảng riêng, cả ba phải cộng thêm phần của quỹ. Sót nơi thứ ba thì kịch bản (h) sai: B có thêm giao dịch chia sẻ cùng kỳ, xoá cứng B tính lại kỳ đó và làm mất khoản chi từ quỹ.
2. **`Transfer` đã bị loại sẵn khỏi gần như mọi tổng chi và thu.** Phần lớn các truy vấn lọc rõ `TransactionType.Debit` hoặc `Credit`: `ReportRepository`, `FinanceSnapshotRepository`, `TransactionRepository:41`, `AnomalyService`, `BudgetSpendScope.Counts`, `TransactionBudgetDelta`, `NetFlow`. `GetRawTransactionsAsync` loại `Transfer` bằng `!=`. Có thêm `chk_transactions_group_shareable`, nên một `Transfer` không chia sẻ nhóm được. Ngược lại, chỉ riêng loại trừ "cho vay/mượn" theo danh mục (`LoanSlug`) đã được áp **không đều**: có ở 12 chỗ nhưng không có ở `FinanceSnapshotRepository`.
3. **Mã số dư của `Transfer` luôn hiểu là "ví nguồn −, ví đích + nếu có".** Xem `DeleteTransactionCommandHandler:68–78` và `UpdateTransactionCommandHandler:156–170, 401–427`. Hai handler này đều kiểm `CounterAccountId is not null`. Vì vậy một `Transfer` không có ví đích hiện được xử lý đúng như **tiền ra**. Tiền **vào** thì không có cách biểu diễn.
4. **`GetBoundaryTransfersAsync` lọc `CounterAccountId != null`.** Tiền vào hoặc ra khỏi tập ví được tính chưa có nhánh cho "đầu kia không phải ví".
5. **Dự báo dòng tiền** lấy số dư hiện tại làm điểm xuất phát (`accounts.Sum(BalanceCents)`). Boundary outflows chỉ dùng để khớp khoản định kỳ, còn `AllAsync` dùng cho độ chính xác. Vì vậy ở (g), −500k nằm sẵn trong số dư đầu. Thói quen chi lấy từ `GetTransactionsAsync`, tức không gồm `Transfer`.
6. **`UserHardDeleter.SettleGroupAsync`** xoá mềm nhóm khi không còn ai. Hàm này không qua `GroupService.DeleteAsync`.
7. **Khoá:** `financial_accounts`, `budget_periods`, `saving_goals` có `xmin`. `transactions` không có `xmin`, nên confirm, update và delete được khoá dòng bằng `RowLocked*` (Phase 82).

### 3. Chỗ đọc ví đích hoặc tác động số dư của `Transfer` (S6)

Grep `CounterAccountId.Value | CounterAccountId! | CounterAccount! | .CounterAccount. | CounterAccountId` và `TransactionType.Transfer` trên `Application`, `Infrastructure`, `API` (không gồm migration, seed). Mỗi dòng có test với transfer gắn quỹ cả hai hướng, trừ khi ghi "chỉ tạo".

| # | Chỗ | Đang làm | Với transfer gắn quỹ |
|---|---|---|---|
| 1 | `DeleteTransactionCommandHandler:68–78` | Hoàn ví nguồn; `CounterAccountId.Value` → hoàn ví đích | Dùng `TransferLegs`; xoá theo D7 / S3 |
| 2 | `UpdateTransactionCommandHandler:105–186, 401–427` | `ApplyBalanceAsync` ví nguồn + `CounterAccountId.Value` ví đích | Dùng `TransferLegs`; giao dịch gắn quỹ bị chặn trước (D9), test 422 |
| 3 | `UpdateTransactionCommandValidator:14–26` | Transfer bắt buộc có ví đích | Không tới được (D9 chặn trước), test 422 chứ không phải 400 |
| 4 | `CreateTransferCommandHandler:76–79` | Tạo transfer thường | Chỉ tạo, không đổi |
| 5 | `ConvertToTransferCommand:125, 202–204, 241, 298–306, 358` | Chuyển Debit/Credit thành transfer, ghép hai chiều | Giao dịch gắn quỹ làm subject hoặc pair → 422 `TRANSACTION_GROUP_FUND_LINKED` |
| 6 | `TransferConversion`, `BalancePreview` | Tính `BalanceDeltas` | Dùng `TransferLegs` khi đọc một transfer có sẵn |
| 7 | `MergeSnapshots:23, 43` (+ khôi phục) | Lưu và khôi phục `CounterAccountId` | Lưu thêm `GroupFundEntryId`, `GroupFundDirection`; khôi phục qua `TransferLegs` |
| 8 | `MergePolicy:32–34, 65–74, 106–113` (`EnsureFitsTheTransfer`) | Credit phải ở ví đích; Debit ở ví nguồn, chênh phí | Theo hướng: `out` = như ví nguồn (Debit, cho chênh phí); `in` = Credit cùng ví, bằng đúng số tiền |
| 9 | `MergeTransactionsCommand:146` (`MergeIntoTransferAsync`) | Gộp vào transfer, phí thành khoản chi | Cùng luật theo hướng; bên thắng giữ liên kết quỹ (D11) |
| 10 | `DuplicateMatcher:87, 150` (`MatchesTransfer`), `DuplicateProbe.CounterAccountId` | Credit khớp ví đích, Debit khớp ví nguồn | Probe mang hướng; khớp theo `TransferLegs` |
| 11 | `TransactionRepository:325, 343` (truy vấn và dựng probe) | Dựng probe với `CounterAccountId` | Thêm hướng vào probe |
| 12 | `TransactionRepository:42, 155` | Debit có `CounterAccountId == null`; ví có giao dịch ở cả hai vế | Không đổi (tìm Debit; `FinancialAccountId` đã phủ ví) — test ví có giao dịch gắn quỹ không xoá được |
| 13 | `TransferRecurringCheck:37` | Bỏ qua khi không có ví đích | Đúng sẵn, có test |
| 14 | `ForecastDataRepository:22–56` (`GetBoundaryTransfersAsync`, join `CounterAccount!.AccountName`) | `CounterAccountId != null` | Nhánh mới qua `TransferLegs`, tên lấy từ quỹ; `ForecastActuals` đọc qua đây |
| 15 | `ForecastAccuracy:76` | Cộng boundary theo `Type` | Đúng khi #14 đúng, có test |
| 16 | `TransactionMapper:18`, `TransactionDto:8`, `TransactionsController` (timeline dùng mapper) | Trả `counterAccountId` | `null` + `groupFundLink {…, direction, entryDeleted}`; timeline không cộng transfer vào tổng ngày, có test |
| 17 | `SupportAccessService:393` (dữ liệu khách của Ops) | Trả `transactionType` dạng chuỗi | Thêm `moneyDirection` (`out`/`in`/`move`) từ `TransferLegs`; không kèm `groupFundLink` (D20) |
| 18 | `TransactionConfiguration:187, 202–204` | Index và FK ví đích | Không đổi |

`TransferLegs.Of(tx)` là chỗ duy nhất đọc `group_fund_direction`. Test kiến trúc: grep mã nguồn để chặn `GroupFundDirection` xuất hiện ngoài `TransferLegs`, cấu hình EF và chỗ tạo.

---

## So sánh cách biểu diễn giao dịch cá nhân gắn quỹ (BR-FUND-08)

| | **A. `Transfer`, ví đích NULL + `group_fund_entry_id` + `group_fund_direction`** (đề xuất) | **B. Loại mới `FundOut` / `FundIn`** | **C. `Debit`/`Credit` + `group_fund_entry_id`** |
|---|---|---|---|
| Không là chi/thu (ngân sách, báo cáo, hũ, thói quen, thu nhập, bất thường) | **Có sẵn**: mọi chỗ đang loại `Transfer` | Tuỳ từng chỗ: `!= Transfer` và `switch _ =>` sẽ lọt | **Phải thêm** `group_fund_entry_id IS NULL` vào khoảng 20 chỗ (bảng mục 2.2). Sót một chỗ là tính thành chi tiêu, không báo lỗi |
| Không chia sẻ nhóm | **Có sẵn**: `chk_transactions_group_shareable` | Phải thêm CHECK | Phải thêm CHECK và sửa `GroupLedger.IsShareable` |
| Số dư ví | Tiền ra đúng sẵn (mục 2.3). Tiền vào cần hướng → tách một hàm `TransferLegs.Of(tx)` dùng chung cho Delete/Update/BalancePreview/MergeSnapshots | Mọi chỗ `Credit ? + : −` hiểu `FundIn` là tiền **ra** → **sai số dư, không báo lỗi** | **Đúng sẵn** ở mọi nơi |
| Dự báo vượt ranh giới | Thêm một nhánh vào `GetBoundaryTransfersAsync` (cùng truy vấn, cùng kiểu kết quả) | Thêm nhánh | Thêm nhánh (`UNION` Debit/Credit có gắn quỹ) và loại khỏi `GetRawTransactionsAsync` |
| Ghép trùng / ghép chuyển ví / hoàn tiền (Phase 81–83) | `Transfer` đã không là ứng viên hoàn tiền hay ghép chuyển ví. Với ghép trùng, xem D11 | Phải chặn từng chỗ | Là ứng viên ở **cả ba** nơi → phải chặn từng chỗ |
| CHECK `chk_transactions_transfer_shape` | Giữ nguyên nhánh cũ. Thêm **một nhánh OR** chỉ mở khi `group_fund_entry_id IS NOT NULL` → transfer thường vẫn bắt buộc có ví đích | Đổi CHECK loại giao dịch | Không đổi |
| App cũ | Thấy `Transfer` có `counterAccountId = null`: không cộng vào chi/thu (đúng), nhưng có thể hiện lạ ("chuyển tới —") | Không parse được enum lạ → **có thể vỡ** | Hiện như chi/thu không danh mục. App tự cộng chi tiêu cục bộ thì **cộng sai** |

**Đề xuất A.** Lý do chính: điều phải đúng nhất của BR-FUND-08 là "không là chi/thu ở bất cứ đâu", và A có điều đó nhờ cấu trúc, không phải nhờ nhớ thêm điều kiện vào 20 chỗ. Rủi ro của A là hướng tiền vào. Rủi ro này gom về một chỗ:
- `TransferLegs.Of(tx)` trả danh sách `(accountId, delta)` và là chỗ **duy nhất** đọc `group_fund_direction`. Delete, Update, `BalancePreview`, `MergeSnapshots` chuyển sang gọi nó.
- Giao dịch gắn quỹ không sửa được qua API giao dịch cá nhân (422), nên Update chỉ chạm nó khi đi qua dịch vụ quỹ.
- Test bất biến số dư chạy mọi tổ hợp tạo, sửa, xoá theo cả hai hướng.

Cột hướng `group_fund_direction` (`'out'|'in'`) cần vì `financial_account_id` NOT NULL và luôn là ví của người đó. Không đặt ví vào `counter_account_id` cho chiều vào: làm vậy thì `financial_account_id` không còn gì hợp lệ để chứa.

---

## Quyết định (đã duyệt theo đề xuất, trừ chỗ S1–S7 ghi đè)

**D1. Cách biểu diễn giao dịch cá nhân gắn quỹ.** Đề xuất **A** ở bảng trên. Giao dịch đó có:
- `transaction_type = transfer`, `counter_account_id = NULL`, `category_id = NULL`;
- `group_fund_entry_id` (unique, FK → `group_fund_entries`, `ON DELETE RESTRICT`), `group_fund_direction`;
- `status = confirmed`, `source = manual`, `group_id = NULL`;
- `description` = ghi chú của dòng quỹ, `merchant_name = NULL` (tên quỹ đọc qua `groupFundLink`, không chép vào).

**D2. Số dư quỹ lưu sẵn hay tính khi đọc.** Đề xuất **lưu sẵn** `group_funds.balance_cents` + `xmin` (theo luật "số dư là tổng tính sẵn"). Cách làm:
- Mọi ghi dòng quỹ: đọc quỹ, tính, lưu **một lần** cùng ví cá nhân và kỳ ngân sách nhóm.
- `RetryingGroupFundEntryService` bọc ngoài, `CONCURRENT_UPDATE` (409) khi hết lượt thử lại.
- Test bất biến: `balance_cents = Σ dòng chưa xoá (có dấu)` sau mọi thao tác, kể cả hai người ghi đồng thời.

Báo cáo tháng (BR-FUND-12) tính số đầu kỳ bằng một tổng SQL trên các dòng trước kỳ. Đó là báo cáo, không phải số dư đọc thường xuyên.

**D3. Dấu và số tiền của từng loại dòng.** `amount_cents > 0` với `Contribution`, `Expense`, `Withdrawal`. `Adjustment` mang dấu (`≠ 0`): dương là tăng quỹ. Ánh xạ:
- `Contribution` +, `Expense` −, `Withdrawal` −, `Adjustment` ±.
- CHECK: `(kind = 'adjustment' AND amount_cents <> 0 AND note IS NOT NULL) OR (kind <> 'adjustment' AND amount_cents > 0)`.

**D4. Người gắn với dòng luôn là người gọi API.** `POST` không có `memberUserId`.
- `Contribution`, `Withdrawal`: cho chính mình (BR-FUND-04).
- `Expense`, `Adjustment`: người ghi.

Như vậy "người ghi" và "người gắn với dòng" là một cột: `member_user_id`, nullable (BR-FUND-15). Khi cột này null, tên hiện **"Thành viên cũ"** qua hằng mới `GroupMemberService.FormerMemberName`.

**D5. Ai sửa được gì (BR-FUND-05).**
- Người ghi sửa dòng của mình: số tiền, thời điểm, danh mục, ghi chú, nơi chi, ví kèm theo (thêm, bỏ hoặc đổi ví thì giao dịch cá nhân được tạo, xoá mềm hoặc chuyển ví trong cùng lượt lưu).
- **Không** đổi `kind`: muốn đổi thì xoá rồi ghi lại.
- Người có `ManageFunds` sửa được **danh mục và ghi chú** dòng của người khác, giống `ManageSharedTransactions` phân loại lại. Không sửa được số tiền, thời điểm hay ví → 403.
- "Owner/Manager xoá được mọi dòng" hiểu là **quyền `ManageFunds`**, để quyền ghi đè theo người vẫn có tác dụng.

**D6. Quản lý xoá dòng góp của người khác có kèm ví (BR-FUND-09 nửa đầu).** *Đã thay bằng S3: chỉ xoá mềm dòng quỹ, giao dịch cá nhân giữ nguyên với `entryDeleted: true`, push cho chủ dòng. Đoạn dưới là đề xuất cũ, giữ để tra lại.*
- Theo đúng luật: giao dịch cá nhân của chủ dòng bị xoá mềm và số dư ví của họ được hoàn trong cùng lượt lưu.
- Đây là lần đầu một người **ghi vào sổ cá nhân của người khác**, nên đề xuất ghi thêm hai thứ: hoạt động nhóm `FundEntryDeleted` (không số tiền), và `audit_logs` `GroupFundEntryDeleted` (người xoá, chủ dòng, id, không số tiền).
- Nếu anh muốn giữ ví cá nhân tuyệt đối riêng thì có phương án thay: chỉ **gỡ liên kết** giao dịch cá nhân, để chủ dòng tự xoá. Tôi vẫn đề xuất theo luật đã chốt.

**D7. Người đã rời nhóm xoá giao dịch cá nhân gắn quỹ (BR-FUND-09 nửa sau và BR-FUND-13 mâu thuẫn).** Luật 09 nói xoá giao dịch thì xoá dòng quỹ; luật 13 nói người đã rời không xoá được dòng.
- Đề xuất: còn là thành viên thì xoá cả dòng quỹ (luật 09).
- Đã rời, hoặc nhóm đã xoá mềm: xoá mềm giao dịch cá nhân và hoàn ví, **dòng quỹ ở lại**. Số dư quỹ không đổi.
- *Sửa khi làm Task 2:* `transactions.group_fund_entry_id` **không** bị xoá trên giao dịch đã xoá mềm. CHECK `chk_transactions_transfer_shape` cần cột này cho mọi transfer không có ví đích, và mọi truy vấn đã lọc `deleted_at`, nên liên kết trên dòng đã xoá mềm vô hại. Liên kết chỉ được xoá khi giao dịch về lại `Debit` (D24).
- Response giao dịch không đổi. Activity của nhóm không ghi gì.

**D8. Cờ tắt mà người dùng xoá giao dịch gắn quỹ.** Giống luật hiện có của giao dịch chia sẻ: vẫn đồng bộ xoá dòng quỹ, kèm số dư quỹ và ngân sách nhóm, để số của nhóm không lệch. API quỹ thì 422 `GROUP_FUNDS_DISABLED` (hoặc `GROUPS_DISABLED` nếu cờ nhóm tắt). Khoản chi từ quỹ vẫn được tính vào ngân sách nhóm khi cờ tắt; cảnh báo tạm dừng như cảnh báo nhóm hiện nay.

**D9. Sửa giao dịch cá nhân gắn quỹ qua API giao dịch.**
- `PUT /transactions/{id}`, `convert-to-transfer`, `merge` (bên thua), `refund`, `PUT /transactions/{id}/group` → 422 `TRANSACTION_GROUP_FUND_LINKED`. Sửa thì sửa dòng quỹ.
- `DELETE /transactions/{id}` theo D7.

**D10. Danh mục của `Expense` (BR-FUND-06).** *Ý "`Expense` kèm ví → 422" đã thay bằng S4: `Expense` nhận `fromTransactionId` hoặc `financialAccountId` (không cả hai). Phần danh mục giữ nguyên.*
- Bắt buộc có ít nhất một trong hai: `categoryId` phải là **danh mục hệ thống** (danh mục riêng của một user không hiện với người khác) và không phải `loan`; `groupCategoryId` phải là danh mục nhóm còn hoạt động.
- Có cả hai thì danh mục nhóm thắng khi khớp ngân sách, đúng như giao dịch chia sẻ.
- `Expense` kèm `financialAccountId` → 422 `GROUP_FUND_ENTRY_INVALID`. Chi từ quỹ không đụng ví cá nhân; ai trả bằng ví mình thì dùng chia sẻ giao dịch, hoặc góp rồi chi.
- Loại khác gửi `categoryId`/`groupCategoryId`/`merchantName` → 422.

**D11. Thông báo ngân hàng trùng với giao dịch góp quỹ.** Ví dụ A chuyển khoản 500k cho người giữ quỹ rồi ghi góp quỹ kèm ví VCB, sau đó thông báo ngân hàng −500k tới.
- Đề xuất: giao dịch gắn quỹ **là ứng viên trùng** và luôn **thắng khi ghép**, giống transfer nhập tay ở Phase 83. Bản nháp từ ngân hàng bị gộp vào; số tiền, ví và liên kết quỹ của bên thắng không đổi.
- Bên thua không bao giờ là giao dịch gắn quỹ (D9).
- Kiểm `MergePolicy` với ví đích null trong task 9.

Ngược lại, người giữ quỹ nhận +500k vào tài khoản của họ thì thông báo đó thành thu nhập của họ. Ghép thông báo đó với dòng góp quỹ **để phase sau**. Theo S7, ghi rõ hạn chế này ở `docs/REQUIREMENTS.md` §13.4 và `docs/group-api.md`.

**D12. Xoá cứng tài khoản ẩn danh dòng quỹ thay vì xoá (BR-FUND-14, khác FR-GRP-16).**
- `group_fund_entries.member_user_id` FK `ON DELETE SET NULL`.
- Lý do khác FR-GRP-16: giao dịch chia sẻ là **tiền của người đó** mà nhóm chỉ nhìn thấy, nên xoá người thì xoá nó là đúng. Dòng quỹ là **sổ của nhóm**, các thành viên khác dựa vào nó để biết ai còn giữ bao nhiêu. Xoá đi thì số dư quỹ sai: ví dụ B góp 500k, xoá dòng thì quỹ hụt 500k mà tiền vẫn nằm đâu đó trong nhóm. Ẩn danh giữ được số dư, và chỉ để lại số tiền, thời điểm, danh mục, không còn ai biết là của ai.
- Ghi chú và nơi chi của dòng có thể chứa tên: đề xuất **giữ** (cùng mức với ghi chú trên giao dịch chia sẻ trước khi xoá). Điều khoản cần nêu (§13.4).
- Giao dịch cá nhân gắn quỹ của B mất theo khoá ngoại, như mọi giao dịch của B.
- `UserHardDeleter.RecalculateSpentAsync` chuyển sang tổng chung (D13), nên kỳ ngân sách nhóm không mất khoản chi từ quỹ.

**D13. Một định nghĩa cho "chi tiêu nhóm".**
- Thêm `BudgetSpendScope.GroupFund(groupId, systemCategoryId, groupCategoryId, from, to)` (`Expression<Func<GroupFundEntry,bool>>`) **đặt cạnh** `Group(...)`, cùng luật: danh mục nhóm thắng, ngân sách theo danh mục hệ thống chỉ đếm dòng chưa có danh mục nhóm, cả hai null là ngân sách tổng. Chỉ `kind = expense`, `deleted_at IS NULL`, quỹ thuộc nhóm còn sống.
- Một test bảng chạy cùng bộ ca qua cả hai biểu thức, để hai biểu thức không lệch nhau.
- `IGroupFinanceRepository.SumGroupNetSpendAsync(scope, pendingTxIds, pendingEntryIds)` = phần giao dịch chia sẻ + phần quỹ. Thay chỗ gọi ở `SumSharedSpendAsync`, `GroupLedger.RecomputeAsync`, `UserHardDeleter.RecalculateSpentAsync`. `GroupLedger.RecomputeAsync` nhận thêm các dòng quỹ đang sửa trong bộ nhớ.
- Khớp ngân sách vẫn qua `GetMatchingBudgetsAsync`; `GroupScopeTouch` thêm `Of(GroupFundEntry)`.
- Mọi thay đổi khoản chi từ quỹ dùng **tính lại** (`RecomputeAsync`) với phạm vi cũ và mới, không cộng delta.

**D14. Báo cáo tháng của nhóm.** *Ý `UserId` nullable đã thay bằng S5: `byMember` giữ nguyên kiểu, phần của người đã ẩn danh vào `formerMembers { totalSpentCents, fundSpentCents }`.*
- `ListReportRowsAsync` thêm các dòng `Expense` của quỹ thành khoản chi, `UserId` = `member_user_id`. `Contribution`, `Withdrawal`, `Adjustment` không vào báo cáo tháng.
- `GroupMemberTotalsDto.UserId` thành `Guid?`: null kèm tên "Thành viên cũ" (sau xoá cứng). Trước đây trường hợp này không thể có, vì giao dịch của người bị xoá cứng mất theo.
- Thêm trường tuỳ chọn `fundSpentCents` (phần trong `totalSpentCents` đến từ quỹ).

**D15. Ai xem quỹ.** `GET` quỹ, dòng, summary cần `ViewOverview` (cả Viewer). **Không** phụ thuộc `transactionVisibility`: chế độ `Summary` chỉ ẩn giao dịch cá nhân đã chia sẻ, còn sổ quỹ là của nhóm, và minh bạch là lý do quỹ tồn tại. Dòng quỹ **không bao giờ** kèm ví hay số dư cá nhân, chỉ `hasPersonalLink: bool` khi người xem là chủ dòng.

**D16. Lưu trữ và giới hạn số quỹ.**
- `maxFundsPerGroup` chỉ đếm quỹ **chưa lưu trữ**.
- Quỹ đã lưu trữ thì chỉ đọc: ghi, sửa, xoá dòng → 422 `GROUP_FUND_ARCHIVED`. Không có API bỏ lưu trữ.
- `PATCH` quỹ đã lưu trữ (tên, ghi chú) vẫn được.

**D17. Xoá nhóm (BR-FUND-03).** `GroupService.DeleteAsync` → 422 `GROUP_FUND_NOT_SETTLED` khi còn quỹ chưa lưu trữ. Xoá cứng người cuối cùng (`SettleGroupAsync`) vẫn xoá mềm nhóm như cũ: đường hệ thống, không ai chặn được. Quỹ đông cứng cùng nhóm, số dư giữ nguyên.

**D18. Push khi quỹ âm (BR-FUND-16).**
- `group_funds.negative_episode int` tăng mỗi lần số dư chuyển từ ≥ 0 sang < 0, trong cùng lượt lưu.
- Sau khi lưu, gửi qua `forecast_alerts`: kind `group_fund_negative`, `dedup_key = group_fund_negative:{fundId}:{episode}`, mỗi người có `ManageFunds` lúc đó và `AlertsEnabled` một dòng. Tắt cờ nhóm hoặc cờ quỹ thì không gửi. Không có giờ yên lặng.
- Nội dung cố định: "Quỹ {tên quỹ} đang âm". `data = {kind, groupId, fundId}`, không số tiền.
- Tên quỹ do người dùng đặt nên **không log**.

**D19. Chống gửi lại.** `client_request_id` tuỳ chọn trên dòng quỹ: `UNIQUE (fund_id, member_user_id, client_request_id) WHERE client_request_id IS NOT NULL`. Gửi lại thì trả **200** với dòng cũ, không phải 201, như `POST /transactions`.

**D20. `groupFundLink` sau khi rời nhóm.** DTO giao dịch cá nhân vẫn trả `groupFundLink {groupId, fundId, fundName, kind}` sau khi rời: đó là lịch sử của chính họ. `fundName` là tên lúc đọc. Người đã rời gọi API quỹ vẫn 404. DTO phía vận hành (`IOpsCustomerDataService`) **không** kèm `groupFundLink`.

**D21. Thời điểm của dòng.** Cùng luật với `CreateManualTransactionCommandValidator` (không ở tương lai quá mức cho phép). Kỳ ngân sách nhóm theo `transacted_at` giờ VN (`VietnamTime.MonthRange`).

**D22. Hoạt động nhóm.** Thêm `GroupActivityType`: `FundCreated`, `FundUpdated`, `FundArchived`, `FundContributed`, `FundWithdrawn`, `FundEntryDeleted` (chỉ khi người xoá không phải chủ dòng). `SubjectId` = id quỹ hoặc dòng, không số tiền. `Expense` không ghi hoạt động; ngân sách nhóm và danh sách dòng đã thể hiện nó.

**D23. Chuyển một `Debit` thành khoản chi từ quỹ (S4a): điều kiện và việc đi kèm.**
- Điều kiện (sai → 422 `GROUP_FUND_SOURCE_NOT_ELIGIBLE`):
  - của chính người ghi, chưa xoá, `Confirmed`, `Debit`;
  - `GroupId IS NULL`, `Relation IS NULL`, `!IsReversed`, `MergedIntoTransactionId IS NULL`, `DuplicateStatus <> Suspected`;
  - không phải cho vay/mượn;
  - chưa gắn quỹ;
  - **không có khoản hoàn đã xác nhận trỏ tới nó**: hoàn của một transfer không có nghĩa.
- Khoản hoàn **nháp** trỏ tới nó thì được thả ra như `ConvertToTransferCommand` đang làm (`ReleasedDraftRefunds`). Bất thường đang mở thì đóng (`ClosedAnomalies`). Liên kết trùng `suspected` trỏ tới nó thì trỏ lại hoặc thả (BR-DUP-08).
- Số tiền dòng = số tiền giao dịch. `amountCents` gửi kèm mà khác → 422. Thời điểm dòng = `transacted_at` của giao dịch.
- `category_id` của giao dịch về null (CHECK shape). Ví giữ nguyên, nên **số dư ví không đổi**: Debit và Transfer `out` cùng trừ ví nguồn.
- Ngân sách cá nhân: **tính lại** kỳ của danh mục cũ (`IBudgetPeriodService.RecomputeAsync`, như Phase 83), và làm mới `daily_summaries` của ngày đó.
- Ghi `transaction_merge_snapshots` (kind mới `group_fund_expense`) trong cùng DB transaction, để vận hành tra lại.

**D24. Khôi phục khi xoá khoản chi tạo từ giao dịch (S4a).**
- `transaction_merge_snapshots` bị dọn sau `snapshots.retentionDays`, nên **không** dùng snapshot để khôi phục.
- Dòng quỹ lưu hai cột riêng: `origin` (`created` | `converted`) và `origin_category_id` (danh mục gốc, FK categories SET NULL). Xoá dòng `converted` → giao dịch về `Debit`, `category_id = origin_category_id` (danh mục đã bị xoá thì null), bỏ `group_fund_entry_id`, `group_fund_direction`. Ngân sách cá nhân tính lại, `daily_summaries` làm mới.
- Dòng `converted`:
  - không sửa được số tiền hay thời điểm: đó là sự thật ngân hàng (422 `GROUP_FUND_ENTRY_LOCKED`);
  - không đổi được ví hay bỏ liên kết;
  - danh mục, ghi chú, nơi chi sửa được.
- **Ai xoá:**
  - chủ dòng xoá → khôi phục `Debit`;
  - người có `ManageFunds` xoá dòng người khác → theo S3, giao dịch cá nhân giữ nguyên dạng transfer với `entryDeleted: true`. Chủ dòng có thể gọi `POST /transactions/{id}/restore-from-fund` để tự về lại `Debit` (chỉ khi dòng đã xoá mềm và `origin = converted`), hoặc xoá hẳn;
  - chủ dòng xoá giao dịch cá nhân → xoá dòng quỹ như D7 (không khôi phục, vì chính giao dịch bị xoá).
- Dòng `created` (S4b) xoá theo luật thường: chủ dòng xoá thì giao dịch cá nhân bị xoá mềm và ví được hoàn.

### Phát sinh khi làm (ghi lại để anh xem, đều theo tinh thần D đã duyệt)

- **D7 mở rộng (Task 8).** Xoá giao dịch cá nhân gắn quỹ khi **quỹ đã lưu trữ** cũng đi như trường hợp đã rời nhóm: giao dịch xoá được, ví được hoàn, dòng quỹ ở lại. Nếu xoá dòng thì số dư quỹ lưu trữ khác 0, trái BR-FUND-03 (và CHECK `chk_group_funds_archived_settled`).
- **D9 (Task 8).** Chốt chung `GroupFundGuard.EnsureNotLinked` gắn ở: sửa giao dịch, chuyển ví (cả hai chiều), bên thua khi gộp, tạo hoàn, liên kết hoàn/đảo, chia sẻ nhóm. Hoàn và chia sẻ trước đây cũng đã bị chặn vì là `Transfer`, nhưng với mã lỗi khác; nay đều là `TRANSACTION_GROUP_FUND_LINKED`.
- **Task 9 gộp vào Task 3 và 8.** `DuplicateMatcher` và `MergePolicy` đọc vế qua `TransferLegs` (test bảng ở Task 3); test đầu-cuối thông báo ngân hàng gộp vào giao dịch góp quỹ (bên thắng giữ liên kết) ở `GroupFundPersonalSyncTests`.
- **D23 (Task 7b).** Bất thường đang mở của giao dịch nguồn được đóng với lý do `transfer` có sẵn (không thêm lý do mới, để khỏi đổi CHECK).
- **D24 (Task 7b).** Sửa ghi chú của dòng chuyển đổi không ghi đè mô tả của giao dịch ngân hàng.
- **Thông báo S3 (Task 7).** Kind `group_fund_removed` (cột `kind` tối đa 20 ký tự). Gửi khi chủ dòng còn là thành viên và đang bật cảnh báo nhóm; cờ nhóm hoặc cờ quỹ tắt thì không gửi.
- **Lỗi có sẵn nghi ngờ (Task 7).** `GroupFinanceRepository.ListReportRowsAsync` và `ListSharedTransactionsAsync` dùng `IgnoreQueryFilters()` cho bảng người dùng. Trong EF Core lệnh này tắt bộ lọc của **cả** câu truy vấn, nên giao dịch chia sẻ đã xoá mềm có thể vẫn vào báo cáo và danh sách của nhóm. Sẽ kiểm bằng test và sửa ở Task 13, commit riêng.

---

## Thiết kế

### Dữ liệu (migration `AddGroupFunds`)

`group_funds`
- `id uuid`, `group_id uuid` FK groups, `name varchar(100)`, `note varchar(500) null`, `balance_cents bigint default 0`, `negative_episode int default 0`, `created_by_user_id uuid null` (FK users SET NULL), `archived_at timestamptz null`, `created_at`, `updated_at`, `xmin`.
- CHECK `char_length(name) BETWEEN 1 AND 100`; CHECK `archived_at IS NULL OR balance_cents = 0`.
- Index `(group_id) WHERE archived_at IS NULL`.

`group_fund_entries`
- `id`, `fund_id` FK group_funds, `group_id` (lặp lại để lọc và FK nhanh), `member_user_id uuid null` (FK users **SET NULL**), `kind varchar(20)` (`contribution|expense|withdrawal|adjustment`), `amount_cents bigint`, `transacted_at timestamptz`.
- `category_id uuid null` (FK categories), `group_category_id uuid null` (FK group_categories), `note varchar(500) null`, `merchant_name varchar(200) null`, `client_request_id uuid null`, `created_at`, `updated_at`, `deleted_at null`, `deleted_by_user_id null` (SET NULL).
- `origin varchar(10) default 'created'` (`created|converted`), `origin_category_id uuid null` (FK categories SET NULL). CHECK `origin = 'created' OR kind = 'expense'`; CHECK `origin_category_id IS NULL OR origin = 'converted'` (D24).
- CHECK theo D3. CHECK `kind = 'expense' OR (category_id IS NULL AND group_category_id IS NULL AND merchant_name IS NULL)`. CHECK `kind <> 'expense' OR category_id IS NOT NULL OR group_category_id IS NOT NULL`.
- Index `(fund_id, transacted_at desc, id desc)`, `(group_id, kind, transacted_at)` cho ngân sách và báo cáo, unique một phần theo D19.

`transactions`
- Thêm `group_fund_entry_id uuid null` (unique một phần, FK RESTRICT) và `group_fund_direction varchar(3) null`.
- CHECK `chk_transactions_transfer_shape` thay bằng: *(nhánh transfer cũ, nguyên văn)* `OR (transaction_type = 'transfer' AND counter_account_id IS NULL AND category_id IS NULL AND group_fund_entry_id IS NOT NULL) OR (transaction_type <> 'transfer' AND counter_account_id IS NULL)`.
- CHECK mới `chk_transactions_group_fund_link`: `(group_fund_entry_id IS NULL) = (group_fund_direction IS NULL) AND (group_fund_direction IS NULL OR group_fund_direction IN ('out','in')) AND (group_fund_entry_id IS NULL OR (transaction_type = 'transfer' AND group_id IS NULL AND status = 'confirmed'))`.
- FK `group_fund_entry_id` RESTRICT không cản xoá mềm dòng quỹ (S3): giao dịch vẫn trỏ tới dòng đã xoá mềm.
- `transaction_merge_snapshots.kind` thêm `group_fund_expense` (D23).
- Test DB: transfer thường thiếu `counter_account_id` vẫn bị từ chối.

**Quyền:** `GroupPermission.ManageFunds = 2048`, `RecordFundEntries = 4096`. Owner và Manager có sẵn qua `All`. `MemberDefaults` thêm `RecordFundEntries`. Viewer không có.

**Cấu hình:** `GroupSettings.MaxFundsPerGroup = 3` (validator 1–20); `FeatureFlags.GroupFunds = false`. `GET /app-config` trả `features.groupFunds`.

### Code

- `Domain`: `GroupFund`, `GroupFundEntry`, `GroupFundEntryKind`, `GroupFundDirection`; thêm `Transaction.GroupFundEntryId`, `GroupFundDirection`.
- `Application/Groups/Funds/`:
  - `GroupFundService` (quỹ: CRUD, lưu trữ).
  - `GroupFundEntryService` (dòng: tạo, sửa, xoá; một lần lưu: quỹ + ví + giao dịch cá nhân + kỳ ngân sách nhóm), `RetryingGroupFundEntryService`.
  - `GroupFundSummaryService`.
  - `GroupFundMath` (thuần: dấu theo loại, số dư mới, chuyển sang âm hay không).
  - `GroupFundPersonalLink` (thuần: từ dòng ra giao dịch cá nhân, `TransferLegs`).
  - `GroupFundAlertNotifier`.
- `Application/Transactions/TransferLegs.cs`: `(accountId, delta)` cho mọi `Transfer`, gồm cả nhánh gắn quỹ. Delete, Update, `BalancePreview`, `MergeSnapshots` dùng nó.
- `DeleteTransactionCommandHandler` gọi `IGroupFundLinkSync` khi giao dịch có `GroupFundEntryId` (D7, D8). Interface đặt trong Application, tiêm tuỳ chọn như `IForecastAlertTrigger`.
- `ForecastDataRepository.GetBoundaryTransfersAsync`: thêm nhánh `CounterAccountId == null && GroupFundEntryId != null && counted.Contains(FinancialAccountId)`. Hướng lấy từ `group_fund_direction`, tên lấy từ tên quỹ (join).
- API: `GroupFundsController` dưới `/api/v1/groups/{groupId}/funds`, `[RequireGroupsEnabled]` + `[RequireGroupFundsEnabled]` mới.

### Đường lưu một dòng góp kèm ví (khoá theo thứ tự)

1. Kiểm quyền (`RecordFundEntries`), quỹ chưa lưu trữ, ví thuộc người gọi.
2. Lần sửa hoặc xoá: khoá dòng giao dịch cá nhân `FOR UPDATE` (`RunWithTransactionsLockedAsync`), cùng thứ tự với `RowLockedDeleteTransactionCommandHandler`.
3. Đổi `group_funds.balance_cents` (`xmin`), ví (`xmin`), kỳ ngân sách nhóm (`xmin`, chỉ với `Expense`).
4. `SaveChanges` một lần, rồi mới push và cảnh báo.

Thứ tự khoá: giao dịch → quỹ → ví → kỳ ngân sách.

### Nội dung push và hoạt động

Không số tiền, không log tên quỹ, tên người hay ghi chú.

---

## Task (TDD; mỗi task một commit nhỏ; không đụng `docs/diagrams/*.drawio`)

0. **(S2) Loại trừ cho vay/mượn ở một chỗ.** `LoanExclusion` + `Predicates.And`; thay 12 chỗ viết rời và thêm vào `FinanceSnapshotRepository`. Test `LoanExclusionTests`: cho vay 2tr thì `/me/spendable` và `/forecast/purchase-check` (đường của `simulate_purchase`) không coi là chi. Commit riêng.
1. **Quyền và cấu hình.** Thêm hai quyền, `MemberDefaults`, `GroupSettings.MaxFundsPerGroup`, `features.groupFunds`, `RequireGroupFundsEnabledAttribute`, mã lỗi. Test: `GroupPermissionsTests` theo vai trò và ghi đè; validator cấu hình.
2. **Domain + migration `AddGroupFunds`.** Entity, cấu hình EF, CHECK. Test DB: các CHECK (transfer thường thiếu ví đích vẫn bị từ chối; transfer gắn quỹ hợp lệ; dòng quỹ sai hình dạng bị từ chối).
3. **`TransferLegs` ở mọi chỗ trong bảng S6 (#1, 2, 6, 7, 8, 10, 14, 17).** Không đổi hành vi với transfer thường. Test bảng `TransferLegs` (transfer thường, gắn quỹ `out`, gắn quỹ `in`), một test cho mỗi dòng của bảng S6, và test kiến trúc chặn `GroupFundDirection` ngoài `TransferLegs`.
4. **`BudgetSpendScope.GroupFund` + `SumGroupNetSpendAsync`.** Thay ba chỗ gọi (D13). Test bảng hai biểu thức; test `SumSharedSpendAsync` cũ không đổi khi chưa có quỹ.
5. **`GroupFundService`.** Tạo, sửa, liệt kê, lưu trữ; giới hạn số quỹ; `GroupService.DeleteAsync` chặn (D17). Test BR-FUND-01, 03.
6. **`GroupFundEntryService` tạo dòng.** Bốn loại, giao dịch cá nhân kèm ví (góp, rút, `Expense` theo S4b), `clientRequestId`, kỳ ngân sách nhóm. Test BR-FUND-02, 04, 06, 07, 10, 11; quyền theo vai trò và ghi đè.
7. **Sửa và xoá dòng.** D5, S3 (quản lý xoá dòng người khác: giao dịch cá nhân giữ nguyên, `entryDeleted`, push chủ dòng); BR-FUND-05, 09 nửa đầu. Test bất biến: Σ dòng = số dư quỹ; số dư ví đúng sau mọi tổ hợp tạo, sửa, xoá × có ví hay không × đổi ví × hai hướng.
7b. **(S4a) `Expense` từ một giao dịch có sẵn.** D23, D24: điều kiện, chuyển thành transfer `out`, tính lại ngân sách cá nhân và `daily_summaries`, snapshot `group_fund_expense`, khôi phục `Debit` khi chủ dòng xoá, `restore-from-fund` sau khi quản lý xoá. Test: mỗi điều kiện sai → 422; số dư ví không đổi khi chuyển và khi khôi phục; chi tiêu cá nhân về như trước khi có khoản đó.
8. **Đồng bộ từ phía cá nhân.** `IGroupFundLinkSync` trong Delete (D7, D8), chặn sửa theo D9. Test BR-FUND-09 nửa sau; trường hợp đã rời nhóm; cờ tắt.
9. **Ghép trùng với giao dịch gắn quỹ (D11).** Kiểm `DuplicateMatcher` và `MergePolicy` với ví đích null. Test: bên thắng giữ liên kết quỹ.
10. **Đồng thời.** `RetryingGroupFundEntryService`. Test hai người ghi cùng lúc lên cùng quỹ: số dư đúng, không mất dòng.
11. **Dự báo.** Nhánh mới trong `GetBoundaryTransfersAsync`. Test (g): số dư đầu có −500k, `GetTransactionsAsync` (thói quen) không đổi, `OutflowsAsync` có khoản góp, `AllAsync` có khoản rút.
12. **Loại trừ khỏi số liệu cá nhân.** Một integration test cho (a): trước và sau ba lần góp, các API sau trả số giống hệt nhau:
    - báo cáo tháng, timeline, ngân sách cá nhân, hũ;
    - `/forecast/spendable`, `/forecast/category`, bất thường, insight;
    - snapshot Agent (`FinanceSnapshotRepository`).

    Số dư ví giảm đúng 500k.
13. **Báo cáo nhóm và summary quỹ.** D14 theo S5 (`formerMembers`), BR-FUND-12. Test: tháng có góp, chi, rút, điều chỉnh; tháng trước có dòng (đầu kỳ đúng).
14. **Rời nhóm, chờ xoá, xoá cứng.** BR-FUND-13, 14, 15; `UserHardDeleter` tổng chung. Test (d), (h); người chờ xoá hiện `InactiveMemberName`.
15. **Push quỹ âm.** D18, BR-FUND-16. Test: âm → push một lần; âm tiếp → không push; về ≥ 0 rồi âm lại → push lại; người không có `ManageFunds` không nhận; cờ tắt không gửi.
16. **API + DTO.** `GroupFundsController`, `myPermissions`, `groupFundLink` trong `TransactionDto`, `GroupsFeatureFlagTests` cho hai cờ.
17. **Kịch bản đầu-cuối (a)–(i)** trong một lớp test, nhóm "Phòng trọ" A, B, C; ngân sách nhóm "Điện nước" 1.000.000.
    - **(i)** B giữ quỹ. VCB của B báo −800k EVN thành nháp, B xác nhận, rồi B tạo `Expense` từ giao dịch đó (`fromTransactionId`). Kết quả: chi tiêu cá nhân tháng của B bằng đúng lúc trước khi có khoản này; quỹ −800k; ngân sách nhóm +800k; số dư VCB của B vẫn là sau −800k.
18. **Tài liệu.**
    - `docs/REQUIREMENTS.md`: FR-FUND; bỏ "ví chung" ở §13.1 và ở §13.6 dòng 2026-11-05; ba mục §13.4, cộng S7 (tiền góp vào tài khoản người giữ quỹ đang bị tính là thu nhập của họ, xử lý phase sau).
    - `docs/group-api.md`: mục Quỹ chung, bảng quyền, mã lỗi, và hạn chế của S7.
    - `docs/ERD.md`.
    - `.context/TASKS.md`: Phase 85 kèm "Báo nhóm Flutter".
    - `.context/ARCHITECTURE.md` §2.7: "quỹ là sổ của nhóm, chi từ quỹ chỉ tính cho nhóm", và vì sao dùng `Transfer` không ví đích. Mục này sửa sau khi anh duyệt, theo AGENTS.md §4.
    - `CLAUDE.md`: một đoạn Phase 85.

**Báo nhóm Flutter (ghi trong TASKS):**
- `Transfer` có `counterAccountId = null` kèm `groupFundLink`: hiện là "Góp quỹ" / "Nhận từ quỹ", không phải chuyển khoản.
- Không cộng vào chi hay thu khi tính cục bộ.
- Giao dịch này không sửa được (422), xoá thì theo D7. `groupFundLink.entryDeleted = true` khi quản lý đã xoá dòng (S3): hiện nút "Xoá" và, với khoản chi tạo từ giao dịch, "Khôi phục thành khoản chi" (`restore-from-fund`).
- `Expense` nhận `fromTransactionId` (chuyển một khoản chi đã có thành khoản chi từ quỹ) hoặc `financialAccountId`.
- Báo cáo tháng nhóm: `formerMembers` tuỳ chọn.
- Hai quyền mới trong `myPermissions`; cờ `features.groupFunds`.

## Verification

- Mỗi task: lớp test của tính năng. Trước commit: unit + test liên quan (memory: test theo tầng).
- **Mốc (S1, đo 2026-11-14 trên `feat/group-fund` @ `4601e1f`, trước mọi thay đổi):** backend **3207/3208**. Test đỏ duy nhất là `MethodSelectionOnSeedDataTests.TheExampleProfileInTheMdFile_GetsCashFlowBudget_WithASmallEmergencyBufferAsSupport`: rule fallback chọn `rule_80_20` thay cho `cash_flow_budget`, có sẵn từ `b6f1c42`, cùng test mà Phase 84 gọi là FR-FIN-07. Lệnh: `podman run --rm --pids-limit=-1 --security-opt label=disable --network host -v "$PWD":/repo -w /repo/backend -v /run/user/1000/podman/podman.sock:/var/run/docker.sock -e TESTCONTAINERS_RYUK_DISABLED=true -e TESTCONTAINERS_HOST_OVERRIDE=localhost mcr.microsoft.com/dotnet/sdk:9.0 dotnet test` (một project test duy nhất, không bộ lọc), 16 phút.
- **Chênh lệch:**
  - 1352/1353 trong bản nháp chép từ CLAUDE.md, là số của 2026-11-02 (Phase 1–51), đã cũ.
  - 3012/3013 là mốc Phase 84 đo **trước** khi làm Phase 84 (`d589097`). Phase 84 thêm 195 test case: 19 file test, 163 dòng `[Fact]`/`[InlineData]` mới, phần còn lại là case `MemberData`. 3013 + 195 = 3208.
  - Lệnh chạy không thiếu project hay bộ lọc.
- **Test đỏ phụ thuộc giờ (phát hiện 2026-11-15 01:03 giờ VN):** `ListenerHealthScenarioTests.D_FifteenOfThirtyDaysWithoutAnOkDevice_…` ra 48% thay vì 50% khi chạy sau nửa đêm giờ VN. Trên `main` (`4601e1f`) cùng giờ cũng đỏ y hệt, nên là lỗi có sẵn của Phase 84 (cửa sổ 30 ngày tính lệch một ngày quanh 00:00 VN), không do Phase 85. Chưa sửa; báo ở phần kết.
- Trước khi xin merge: toàn bộ backend như trên. Không được có thêm test đỏ nào so với mốc.
- `dotnet build` không cảnh báo mới.

## Ngoài phạm vi

- Chia tiền và công nợ giữa thành viên.
- Ghép thông báo ngân hàng của người giữ quỹ với dòng góp (D11).
- Rút dữ liệu khi rời nhóm (Phase 87; cột đã nullable sẵn).
- Dự báo cấp nhóm.
- Bỏ lưu trữ quỹ.
- Client Flutter.

## Bảng BR → test

Lớp test nằm ở `backend/FinMate.Tests/Integration/Controllers/` trừ khi ghi khác.

| BR / quyết định | Test |
|---|---|
| S2 (Task 0) | `LoanExclusionTests` |
| BR-FUND-01 | `GroupFundsControllerTests` (tạo, quyền theo vai trò và ghi đè, giới hạn 3 quỹ, tên) |
| BR-FUND-02 | `GroupFundEntriesControllerTests.AWithdrawalIntoAWallet_…`, `GroupFundEntryChangeTests.ManyChanges_…` (bất biến Σ dòng = số dư), `GroupFundConcurrencyTests` |
| BR-FUND-03 | `GroupFundsControllerTests.ArchivingNeedsAZeroBalance`, `.DeletingAGroup_…`, `GroupFundSchemaTests.ArchivedFund_MustBeSettled` |
| BR-FUND-04 | `GroupFundEntriesControllerTests.RecordingNeedsRecordFundEntries`, `.RecordFundEntries_FollowsOverrides`, `.Adjustments_…` |
| BR-FUND-05, D5 | `GroupFundEntryChangeTests.NobodyChangesTheAmountOfSomeoneElsesEntry_…` |
| BR-FUND-06, D10 | `GroupFundEntriesControllerTests.ExpenseShape_IsChecked`, `GroupFundSchemaTests.Expense_NeedsACategory_…` |
| BR-FUND-07, S4 | `GroupFundEntriesControllerTests.AContributionFromAWallet_…`, `.AFundExpensePaidFromAWallet_…`, `GroupFundExpenseFromTransactionTests` |
| BR-FUND-08, D1 | `GroupFundSchemaTests` (CHECK), `GroupFundPersonalExclusionTests`, `GroupFundTransferLegsTests`, `Unit/…/TransferLegsTests` |
| BR-FUND-09, S3, D7, D8 | `GroupFundEntryChangeTests.TheOwnerDeleting…`, `.AManagerDeletingSomeoneElsesEntry_…`, `GroupFundPersonalSyncTests` |
| BR-FUND-10, D13 | `Unit/…/GroupFundSpendScopeTests`, `GroupFundBudgetScopeTests`, `GroupFundEntriesControllerTests.AFundExpense_CountsForTheGroupBudget_…`, `GroupFundEntryChangeTests.EditingAnExpense_…` |
| BR-FUND-11 | `GroupFundEntriesControllerTests.AFundExpense_…` (báo cáo cá nhân của người ghi), `GroupFundPersonalExclusionTests` |
| BR-FUND-12 | `GroupFundReportTests.TheFundSummary_…` |
| BR-FUND-13 | `GroupFundMembershipTests.LeavingOrBeingRemoved_…` |
| BR-FUND-14, D12 | `GroupFundMembershipTests.AMemberAwaitingDeletion_…`, `.HardDeletingAMember_…`, `GroupFundSchemaTests.HardDeletingTheMember_…` |
| BR-FUND-15 | `GroupFundSchemaTests.HardDeletingTheMember_…` (cột nullable, SET NULL) |
| BR-FUND-16, D18 | `GroupFundNegativeAlertTests` |
| D9 | `GroupFundPersonalSyncTests.APersonalFundTransaction_IsOnlyChangedThroughItsFundLine` |
| D11 | `GroupFundPersonalSyncTests.ABankNoticeOfTheContribution_…`, `Unit/…/MergePolicyTests`, `Unit/…/DuplicateMatcherTests` (phần Phase 85) |
| D14 / S5 | `GroupFundReportTests.TheGroupMonthlyReport_…`, `GroupFundMembershipTests.HardDeletingAMember_…` (`formerMembers`) |
| D15 | `GroupFundEntriesControllerTests.TheLedger_IsVisibleToEveryMember_…` |
| D16 | `GroupFundEntriesControllerTests.AnArchivedFund_…`, `GroupFundEntryChangeTests.AnArchivedFund_CannotBeEdited` |
| D19 | `GroupFundEntriesControllerTests.ResendingWithTheSameClientRequestId_…`, `GroupFundConcurrencyTests.ResendingTheSameEntryAtOnce_…` |
| D20 | `GroupFundApiSurfaceTests.TheFundLink_StaysAfterLeaving_…` |
| D22 | `GroupFundEntriesControllerTests.RecordingWritesActivities_…` |
| D23, D24 | `GroupFundExpenseFromTransactionTests` |
| S6 | Bảng S6 ở trên: `GroupFundTransferLegsTests` (#1, 12, 14, 15), `MergePolicyTests` (#8), `DuplicateMatcherTests` (#10), `GroupFundPersonalSyncTests` (#2, 3, 5, 9), `SupportConsentedAccessTests.TransactionView_ShowsTheMoneyDirection_…` (#17), `GroupFundApiSurfaceTests` (#16), `Unit/Architecture/GroupFundDirectionUsageTests` |
| Cờ, quyền, DTO | `GroupFundApiSurfaceTests` |
| Kịch bản (a)–(i) | `Integration/Scenarios/GroupFundScenarioTests` |
| Lỗi sẵn có tìm ra | `GroupDeletedSharedTransactionTests` |
