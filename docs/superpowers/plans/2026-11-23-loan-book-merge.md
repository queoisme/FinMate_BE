# Phase 90 — Hợp nhất sổ nợ vào `loans` (cách A)

> Trạng thái: **đã duyệt 2026-11-23**, đang làm. Q1–Q13 theo đề xuất, kèm S1–S5 bên dưới (S thắng Q khi mâu thuẫn). Nhánh `feat/loan-book` tạo từ `origin/main` @ `aba320c`.

## Đã duyệt (2026-11-23): điều chỉnh S1–S5

- **S1 (sửa Q11, trả kèm lãi).** Khoản có `interestRatePercentYear > 0`: một lần trả, hoặc một giao dịch được gắn / xác nhận kèm `loanId`, được phép lớn hơn gốc còn lại nếu phần vượt ≤ lãi tạm tính (tới ngày trả) + `loans.interestToleranceCents` (cấu hình, mặc định 10.000).
  - Phần gốc (đúng bằng còn lại) gắn vào khoản như thường, khoản thành `Settled`.
  - Phần vượt thành một giao dịch **riêng**, cùng ví và thời điểm, **không** thuộc `loan`: Borrow → `Debit` danh mục `other`, mô tả "Lãi vay"; Lend → `Credit` danh mục `income`, mô tả "Tiền lãi". Nó tính vào chi tiêu / thu nhập như giao dịch thường (ngân sách, báo cáo, tóm tắt ngày).
  - Một lượt lưu; id giao dịch lãi = `DeterministicGuid.V5(entryId, "interest")` như khoản phí Phase 83, nên chạy lại không tạo hai lần.
  - Gợi ý từ thông báo (Task 8) cũng nhận ứng viên trong ngưỡng này.
  - Không có lãi suất, hoặc vượt ngưỡng → vẫn 422 `LOAN_REPAYMENT_EXCEEDS_REMAINING`.
  - Kịch bản (i): vay 2tr lãi 12%/năm, sau 90 ngày, thông báo −2.060.000 → khoản `settled`, có giao dịch lãi 60.000 tính vào chi tiêu tháng. (Lãi tạm tính 2.000.000 × 12% × 90/365 = 59.178; vượt 60.000 ≤ 59.178 + 10.000.)
  - Xem Q14 về việc tách một giao dịch ngân hàng có sẵn.
- **S2 (sửa Q5).** `PUT principalCents` là **tổng gốc mới**. Dòng giải ngân đầu đổi đúng bằng chênh lệch (mới − cũ). Dòng đầu gắn giao dịch → 422 `LOAN_PRINCIPAL_LINKED`; dòng đầu sau khi đổi ≤ 0 → 422 `LOAN_INVALID`; tổng mới < đã trả → 422 `LOAN_PRINCIPAL_BELOW_PAID`. Test với khoản có hai lần giải ngân.
- **S3 (sửa Q9).** Mọi test chat hiện có của `TryRepayAsync` phải xanh mà **không** sửa kỳ vọng. Test mới: "Bạn An" ↔ "An", "chị Hoa" ↔ "Hoa" khớp; "Hoa" ↔ "Hòa" không khớp. Một hàm khớp tên dùng chung (`PersonNameMatch`) cho chat và gợi ý từ thông báo; nếu cần bỏ tiền tố xưng hô thì bỏ ở đó, không viết logic riêng cho chat. Chi tiết luật ở Q15.
- **S4.** Kịch bản (h): `POST /loans/{id}/repayments` có ví → giao dịch tạo ra; sau đó thông báo ngân hàng cùng số tiền về → `Suspected` → gộp → liên kết khoản vay nằm ở bản giữ lại, `paidCents` không đổi, số dư ví không bị trừ hai lần.
- **S5.**
  - **Cổng trước Task 3** (không chỉ trước Task 1): xác nhận với nhóm Flutter app đang đọc những trường nào của `/loans` và ghi vào mục "Trường app đang dùng" bên dưới; báo MinhTuan trước khi sửa `LoanService`. Repo Flutter không có trên máy này nên phải hỏi người. Task 0–2 không đụng `/loans` nên làm trước được.
  - **`GroupDebtService` đã kiểm (2026-11-23):** không đọc thông báo ngân hàng, `relation_hint` hay giao dịch; nhóm chỉ tự ghi lần thanh toán (`SettleAsync`, không tạo giao dịch). Hai loại gợi ý không chồng nhau, nên không cần luật ưu tiên. Test khoá điều này: một thông báo "TRA NO" khi người dùng vừa có khoản vay vừa đang nợ trong nhóm chỉ ra `loanSuggestions`, nhóm không có gì đổi.

## Đã chốt (2026-11-23, lần 2): Q14, Q15, Q16, test đỏ sẵn

- **Q16: cách 3, "chi tiêu thật" + dòng tiền riêng.** Nguyên tắc (ghi vào `.context/ARCHITECTURE.md` cạnh quyết định #1 về `Transfer`): *chi tiêu / thu nhập là tiền đã dùng hết hoặc thực sự kiếm được; tiền đổi chỗ (chuyển giữa ví của mình, góp/rút quỹ nhóm, cho vay / đi vay / trả nợ) không phải chi tiêu hay thu nhập nhưng vẫn hiện trong dòng tiền; lãi vay (S1) là chi tiêu / thu nhập thật.* Nguồn gốc mâu thuẫn: commit `3df2182` (13/11, `dev/tho`) cố ý tính `loan` vào thu/chi của `GetDailySpendAsync`; Phase 85 Task 0 (`5281eba`) vô tình thêm lại `NotLoan` khi gom `LoanExclusion`; test của MinhTuan (17/11) viết theo ý commit 13/11. Làm ở **Task 0b**. Hệ quả cho Phase 90: gắn một giao dịch thu vào sổ nợ (đổi sang `loan`) làm `totalIncomeCents` giảm và `loanInflowCents` tăng đúng bằng số đó, `cashFlow.netCents` không đổi; assert này thêm vào kịch bản (b).
- **8 test gợi ý AI đỏ sẵn:** để MinhTuan sửa trên `main`; ghi tên vào mốc, luật "không thêm test đỏ" so với mốc. Ngoại lệ: nếu tới Task 2 mà `main` chưa sửa và lỗi nằm trong phần Task 2 đụng (`NotificationHints`, hợp đồng AI client) thì sửa bằng một commit `fix:` riêng trước Task 2, ghi vào báo cáo cho MinhTuan. Rebase lấy bản sửa về thì đo lại mốc.
- **Q14: duyệt.** Khi so "cùng sự kiện ngân hàng", cộng lại phần lãi. Liên kết lưu bằng cột `loan_repayments.interest_transaction_id` (FK NO ACTION, UNIQUE khi khác null) cạnh `transaction_id`, nên biết phần nào cần cộng mà không đoán. Thông báo gửi lại y hệt đã bị `contentHash` chặn ở `notification_logs`, nên chỉ xử lý trùng nghiệp vụ. Test: nhập tay trả 2.060.000 (tách 2.000.000 gốc + 60.000 lãi), rồi thông báo −2.060.000 về → `Suspected` trỏ đúng giao dịch gốc → gộp → vẫn một giao dịch gốc + một giao dịch lãi, số dư không trừ hai lần.
- **Q15: duyệt.** Câu có dấu so giữ dấu, câu không dấu so bỏ dấu. Một câu khớp nhiều khoản (Hoa, Hòa) → trả **tất cả** trong `loanSuggestions` theo hạng, không tự chọn; khớp tên chỉ dùng để xếp hạng. Test "HOA TRA NO" với hai khoản Hoa và Hòa cùng mở.

## Task 0b dừng lại: ba chỗ làm bất biến dòng tiền lệch (2026-11-23, chờ chốt)

Bất biến: `netCashFlowCents = totalIncomeCents − totalSpentCents − loanOutflowCents + loanInflowCents − groupFundOutflowCents + groupFundInflowCents`, với `netCashFlowCents` tính trực tiếp từ tác động số dư. Kiểm code trước khi sửa thấy ba trường hợp nó **không** đúng. Chưa sửa code nào.

**D1. Cặp đảo giao dịch nằm ở hai kỳ (hoặc hai ngày).** `NetFlow` loại cả khoản chi bị đảo lẫn lệnh đảo khỏi chi, thu và số lượng (Phase 81, BR-RF-08). Cùng một kỳ thì tiền ra rồi vào bù nhau, bất biến đúng. Khoản chi ngày 30/10 bị đảo ngày 2/11: tháng 10 số dư giảm `a` mà chi không tăng, tháng 11 số dư tăng `a` mà thu không tăng. Dòng thời gian từng ngày lệch cả khi cặp đảo cùng tháng nhưng khác ngày.
*Đề xuất:* trường riêng có tên `reversalNetCents` (có dấu): tác động số dư của các giao dịch thuộc cặp đảo trong kỳ; bằng 0 khi cả cặp nằm trong kỳ. Bất biến thêm `+ reversalNetCents`. Không phải "khác": nó chỉ chứa đúng một loại (cặp đảo, mà báo cáo đã cố ý loại khỏi chi/thu).

**D2. Báo cáo tháng chặn chi ở 0.** `GetMonthlySummaryQueryHandler.ReadRangeAsync` trả `Math.Max(0, spent)` (chi ròng âm khi tiền hoàn trong tháng nhiều hơn tiền chi, vd. được hoàn món mua tháng trước). Khi bị chặn, `totalSpentCents` = 0 nhưng tiền hoàn đã vào ví, nên bất biến lệch đúng bằng phần bị chặn.
*Đề xuất:* trường riêng `refundSurplusCents` = `max(0, −chi ròng)` của kỳ (thường 0), bất biến thêm `+ refundSurplusCents`. Giữ nguyên cách hiển thị `totalSpentCents` (không bao giờ âm). Phương án khác: bỏ chặn, để `totalSpentCents` âm được (đổi ý nghĩa với app, không đề xuất).

**D3. Báo cáo theo một ví (`accountId`).** Khi xem một ví, chuyển tiền giữa ví đó và ví khác của chính mình làm số dư ví đó đổi, nhưng chuyển ví không phải chi, thu, vay nợ hay quỹ, nên bất biến lệch. Xem tất cả ví thì hai vế bù về 0, không sao.
*Đề xuất:* hai trường riêng `transferOutflowCents` / `transferInflowCents` (chuyển ví nội bộ ra / vào ví đang xem), luôn 0 khi xem tất cả ví; bất biến thêm `− transferOutflowCents + transferInflowCents`. Phí chuyển ví (Phase 83) vẫn là khoản chi thường.

**Ghi chú (không chặn):** tổng từng ngày của `timeline` cộng từ giao dịch của **trang** hiện tại, nên một ngày bị cắt ngang giữa hai trang sẽ có tổng thiếu (lỗi sẵn có). Các trường mới tính cùng cách, nên bất biến vẫn đúng trên những giao dịch đang hiện, nhưng tổng của ngày đó chưa phải tổng thật. Đề xuất sửa trong Task 0b: tổng ngày lấy từ `GetDailySpendAsync` cho các ngày có trên trang, không cộng từ trang.

## Đã chốt (2026-11-23, lần 3): D1–D3 của Task 0b

Duyệt cả ba, gom vào object `cashFlow` (`netCents, loanOutflowCents, loanInflowCents, groupFundOutflowCents, groupFundInflowCents, transferOutflowCents, transferInflowCents, refundSurplusCents, reversalNetCents`) trong `monthly-summary` và từng ngày của `timeline`; `totalSpentCents` / `totalIncomeCents` giữ nguyên. Bất biến: `netCents = thu − chi − loanOut + loanIn − fundOut + fundIn − transferOut + transferIn + refundSurplus + reversalNet`. `refundSurplusCents` khác `crossPeriodRefundCents` (ghi trong `docs/reports-cash-flow-api.md`, kèm ví dụ). Ghi chú về tổng ngày của `timeline` được sửa cùng lúc.

## Báo MinhTuan

- **Test đổi kỳ vọng:** `ReportsControllerTests.LoanTransactions_CountAsRealCashFlow_ButStayOutOfTheCategoryBreakdown` đổi tên thành `LoanTransactions_StayOutOfSpendingAndIncome_ButShowInTheCashFlow`. Lý do: quyết định Q16 (2026-11-23). Tiền cho mượn / được trả nợ là tiền thật ra/vào ví, đúng như ý commit `3df2182`, nhưng nó không phải chi tiêu hay thu nhập. Vì vậy nó hiện ở `cashFlow.loanOutflowCents` / `loanInflowCents` và `cashFlow.netCents` (−3.300.000 trong test), còn `totalSpentCents` / `totalIncomeCents` giữ 300.000 / 0. Dòng thời gian cũng vậy.
- **8 test gợi ý quan hệ AI đỏ trên `main`:** đã sửa trên `feat/loan-book` bằng commit `fix:` riêng `814d54b` (thứ tự `ExtractionResult`, kèm lỗi thật ở đường OCR). Nếu cậu ấy sửa trên `main` theo cách khác thì gộp lại khi rebase. Test `Analyze_HighConfidence…AutoCommits` vẫn đỏ, chưa xem.
- **Trước Task 3** (sửa `LoanService`, `LoanRepository`, `LoanConfiguration`, `LoansController`, gỡ `LoanDueReminderJob`): báo trước.

## Trường app đang dùng (S5, đã kiểm 2026-11-23)

Đọc thẳng code app (`FinMate_FE_Mobile`, nhánh `develop` @ `27cb3eb`, 2026-11-21): `lib/features/planning/data/loans_repository.dart`, `loans_screen.dart`, `assistant_controller.dart`. Nhánh chưa push của nhóm Flutter thì không thấy được.

- **Endpoint dùng:** `GET /loans`, `POST /loans`, `PUT /loans/{id}`, `DELETE /loans/{id}`, `POST /loans/{id}/repayments`. `POST`, `PUT` và `repayments` đều đọc lại khoản vay trả về.
- **Trường đọc:** `id`, `direction` (`lend`, còn lại coi là `borrow`), `title`, `counterparty`, `principalCents`, `paidCents`, `remainingCents`, `interestRatePercentYear`, `dueDate`, `startDate`, `accruedInterestCents`. `totalDueCents` app tự tính (= còn lại + lãi tạm tính); `done` = `remainingCents == 0`.
- **Trường gửi:** `direction`, `title`, `counterparty`, `principalCents`, `interestRatePercentYear`, `dueDate?`, `startDate?`; `PUT` gửi đủ các trường (`startDate` vắng = giữ nguyên). `repayments`: `amountCents`, `financialAccountId?`.
- **Lỗi:** chỉ hiện `message` của server (`errorMessage(e)`), không rẽ nhánh theo mã. Mã mới phải có câu tiếng Việt đọc được.
- **Hệ quả cho Phase 90:**
  - Giữ nguyên mọi trường trên, đúng tên. **Không thêm `repaidCents`** vào API (bảng API ở dưới từng ghi vậy): cột DB là `repaid_cents`, API vẫn là `paidCents`, tránh hai tên cho một số.
  - Màn trả nợ chặn ở client số tiền > `remainingCents`, nên phần lãi S1 chỉ đi qua thông báo ngân hàng / gắn giao dịch, không qua nút "Trả" của app (báo Flutter nếu muốn cho trả kèm lãi).
  - Trợ lý chat tạo khoản bằng `POST /loans` rồi sửa số bằng `PUT principalCents` và hoàn tác bằng `DELETE`: S2 (dòng đầu không gắn giao dịch) và Q4 (xoá mềm, 204) giữ đúng hành vi đó.
  - App chưa biết `status`: khoản `written_off` có `remainingCents > 0` sẽ hiện như đang mở. Báo Flutter đọc `status`.
- **Báo cáo:** app chưa đọc `cashFlow`. Tab Ngày/Tuần/Năm cộng tổng ngày của `timeline` qua mọi trang (`getPeriodSummary`), nên tổng ngày phải giữ kiểu "theo trang". Task 0b ban đầu đổi sang tổng cả ngày (sẽ cộng trùng ngày bị cắt giữa hai trang); đã quay lại theo trang trong commit sửa, có test `Timeline_ADaySplitAcrossPages_SumsToTheMonthlyTotals_WithoutCountingTwice`.

## Bối cảnh

Hai bản sổ vay nợ được làm song song:

| | `loans` (MinhTuan, đã ở `main`, commit `f65f047` ngày 2026-11-20) | `debts` (Phase 86 cũ, nhánh `feat/debt-book`, chưa push) |
|---|---|---|
| API | `/api/v1/loans`: `GET`, `POST`, `PUT /{id}`, `DELETE /{id}`, `POST /{id}/repayments` | `/api/v1/debts`: 12 endpoint |
| Bảng | `loans`, `loan_repayments` | `debts`, `debt_entries` |
| Còn lại | Gốc − Σ trả, tính khi đọc; khoá `xmin` | Tổng lưu sẵn + `xmin`; CHECK ở DB |
| Lãi | Lãi đơn tạm tính theo ngày, `decimal` %/năm, chỉ hiển thị | Không |
| Tạo | Chỉ ghi sổ | Ba cách: kèm giao dịch từ ví, từ giao dịch có sẵn, nợ cũ |
| Trả | Có ví thì tạo giao dịch `loan` qua `CreateManualTransactionCommand` (lưu riêng, lỗi thì gỡ lần trả) | Một lượt lưu; giải ngân thêm; `clientRequestId` |
| Trạng thái | Không | `Open` / `Settled` / `WrittenOff` |
| Liên kết giao dịch có sẵn, gợi ý từ thông báo, push | Không | Có |
| Đồng bộ khi xoá / sửa / gộp / chuyển ví giao dịch | Không | Có (`IDebtLinkSync`) |
| Dự báo, spendable, Agent | Không | Có |
| Chat | "Bạn An trả tôi 500k" → tìm khoản khớp tên, app gọi `/repayments`; "Cho bạn An vay 2tr" → nháp tạo khoản | Gợi ý khoản cho nháp chat `loan` |
| Nhắc hạn | `LoanDueReminderJob` 08:05 VN, push thẳng, nhắc một lần mỗi hạn, nội dung có **tiêu đề khoản** | Qua `forecast_alerts`, trước hạn + 3 lần quá hạn, câu chung, công tắc riêng |
| Xoá khoản | Xoá cứng (cascade lần trả) | Xoá mềm |

**Cách A (đã chọn):** một sổ duy nhất. Giữ `/api/v1/loans`, các bảng `loans` / `loan_repayments` và mọi hành vi app đang dùng; đưa các phần của bản `debts` vào. Bỏ hẳn `debts` / `debt_entries` (chưa từng lên `main` hay chạy ở DB nào). Nhánh `feat/debt-book` chỉ là nguồn để chép code và test, không merge.

Các luật nghiệp vụ BR-DEBT-01…18 và S1–S5 đã duyệt ở `docs/superpowers/plans/2026-11-14-debt-book.md` vẫn giữ nguyên ý nghĩa; plan này chỉ nói chúng sống ở đâu trong mô hình `loans` và chỗ nào phải khác để không phá app.

---

## Kết quả kiểm `origin/main` (2026-11-23)

1. **Xung đột git khi merge thử** `feat/debt-book` vào `origin/main`: 16 file (`.context/ARCHITECTURE.md`, `.context/TASKS.md`, `Program.cs`, `ChatDto`, `TransactionDto`, `UserProfileDto`, `ForecastAlertDispatcher`, `AnalyzeNotificationCommandHandler`, `ChatCommandHandler`, `ConfirmTransactionCommandHandler`, `UpdateTransactionCommandHandler`, `GetTransactionDetailQueryHandler`, `ForecastAlertKind`, `ForecastAlertConfiguration`, `ForecastEnumColumns`, `FinMateDbContextModelSnapshot`). Không merge nhánh cũ, nên các xung đột này được tránh bằng cách làm lại trên `main` theo từng task.
2. **Số Phase:** `main` đã dùng Phase 86 (Văn bản pháp lý), 87 (để dành: rút dữ liệu khỏi nhóm), 88, 89. Plan này là **Phase 90**.
3. **Alembic ai-service:** `main` đã có `0039_user_item_memory`. Migration gợi ý trả nợ thành **`0040`**, `down_revision = '0039'`.
4. **`ForecastAlertKind` trên `main`** đã thêm `PurchaseReminder`, `WeeklyDigest`, `GroupDebtReminder`, `GroupApprovalRequested`, `GroupApprovalDecided`. Kind mới của plan này thêm sau chúng.
5. **Lỗi D23 vẫn còn** trên `main` (`ForecastDataRepository.cs:97` lọc `NotLoan` ở `GetRawTransactionsAsync`); **test nửa đêm** `ListenerHealthScenarioTests.D_…` vẫn dùng giờ thật.
6. **Chat trên `main`** (`ChatCommandHandler.TryRepayAsync`, ai-service `item_parser.py`) đã nhận câu trả nợ và tạo khoản vay. Plan giữ phần này, bỏ phần gợi ý chat của bản `debts` (xem Q9).
7. **Agent trên `main`** có thêm `ManagementTools` (ngân sách, mục tiêu, khoản sắp tới, đề xuất hạn mức) nhưng chưa có công cụ khoản vay.
8. **`categories.kind`** trên `main` (`expense` / `income`) không chặn danh mục `loan` dùng cho cả hai chiều; không ảnh hưởng.
9. Test hiện có của `loans`: `LoanServiceTests` (8), `LoanDueReminderJobTests` (1), `LoanExclusionTests`.
10. **Các chỗ của `loans` lệch luật dự án** (AGENTS.md, CLAUDE.md):
    - nhắc hạn đưa `loan.Title` (người dùng tự đặt, thường chứa tên người: "Cho Bạn An vay") vào nội dung push;
    - giao dịch trả nợ chép `loan.Counterparty` vào `merchant_name` và `loan.Title` vào `description`;
    - xoá khoản là xoá cứng (AGENTS.md §3.3);
    - `LoanService.Today()` dùng `DateTime.UtcNow` (AGENTS.md §3.3 cấm `DateTime`);
    - xoá giao dịch trả nợ thì lần trả vẫn còn trong sổ (sổ lệch với ví);
    - `loan_repayments.transaction_id` không có FK và không UNIQUE;
    - lãi suất `decimal(5,2)` (luật cấm `decimal` cho **tiền**; đây là tỉ lệ, xem Q2).

---

## Câu hỏi cần chốt (mỗi câu kèm đề xuất)

**Q1. App Flutter đã dùng `/loans` chưa, dùng trường nào?** Đề xuất: hỏi nhóm Flutter trước Task 1. Dù trả lời thế nào, plan **không đổi** đường dẫn, tên trường, giá trị enum (`borrow`/`lend`) hay mã lỗi đang có của `/loans`; mọi thứ mới là tuỳ chọn.

**Q2. Lãi suất kiểu `decimal`.** Đề xuất: **giữ** `decimal(5,2)` cho tỉ lệ %/năm, vì luật cấm `decimal` nói về số tiền; số tiền lãi (`accruedInterestCents`) vẫn là `long`. Ghi rõ ngoại lệ này trong ERD. Phương án khác: đổi sang `int` phần vạn (`interest_rate_bps`), phải đổi API (phá app nếu app đã gửi số thập phân).

**Q3. Bỏ tên người khỏi push và khỏi giao dịch (S2, D18 cũ).** Đề xuất: **có**.
- Nhắc hạn: câu chung "Bạn có một khoản nợ sắp đến hạn" / "… đã quá hạn", không tiêu đề khoản, không số tiền.
- Giao dịch trả nợ / giải ngân: `merchant_name = null`, `description` cố định ("Trả nợ" / "Thu nợ" / "Cho vay" / "Đi vay"). Tên người hiện qua `loanLink` trên DTO giao dịch.
- Giao dịch cũ đã tạo trước đây giữ nguyên (không sửa dữ liệu lịch sử).

**Q4. Xoá khoản vay.** Đề xuất: **xoá mềm** (`deleted_at`, cả các dòng), giao dịch đã gắn ở lại với danh mục `loan` và gắn lại được vào khoản khác. `DELETE /loans/{id}` vẫn trả 204 như cũ.

**Q5. Gốc là số cố định hay tổng các lần giải ngân?** `loans.principal_cents` hiện là số người dùng nhập và sửa được bằng `PUT`. Đề xuất:
- `principal_cents` thành **tổng lưu sẵn** = Σ dòng giải ngân còn sống (thêm cột `repaid_cents` lưu sẵn tương tự), giữ tên cột để không đổi API.
- Migration tạo cho mỗi khoản có sẵn một dòng giải ngân đầu tiên (`kind = principal`, số = `principal_cents`, ngày = `start_date`, không giao dịch).
- `PUT` đổi `principalCents`: được khi dòng giải ngân đầu **không** gắn giao dịch (sửa số của dòng đó); dòng đầu gắn giao dịch → 422 `LOAN_PRINCIPAL_LINKED` (sửa ở giao dịch). Vẫn giữ 422 `LOAN_PRINCIPAL_BELOW_PAID` khi gốc mới < đã trả.

**Q6. Tạo khoản kèm giao dịch.** `POST /loans` hiện "không đụng ví". Đề xuất: thêm hai trường **tuỳ chọn** `createTransaction {financialAccountId, transactedAt?}` hoặc `transactionId`; không gửi thì như cũ (nợ cũ, không đụng ví). Đúng BR-DEBT-03.

**Q7. Mã lỗi.** Đề xuất: giữ ba mã của `loans`, ánh xạ mã của `debts` sang tiền tố `LOAN_`:

| Bản `debts` | Phase 90 |
|---|---|
| `DEBT_OVERPAID` | `LOAN_REPAYMENT_EXCEEDS_REMAINING` (có sẵn) |
| `DEBT_HAS_PAYMENTS` (409) | `LOAN_PRINCIPAL_BELOW_PAID` (có sẵn, **422** để khớp mã cũ) |
| `DEBT_DIRECTION_MISMATCH` | `LOAN_DIRECTION_MISMATCH` |
| `DEBT_TRANSACTION_NOT_ELIGIBLE` | `LOAN_TRANSACTION_NOT_ELIGIBLE` |
| `DEBT_AMOUNT_MISMATCH` | `LOAN_AMOUNT_MISMATCH` |
| `DEBT_ALREADY_SETTLED` | `LOAN_ALREADY_SETTLED` |
| `TRANSACTION_DEBT_LINKED` | `TRANSACTION_LOAN_LINKED` |

Lỗi đầu vào của endpoint cũ giữ 422 `LOAN_INVALID` như hiện tại; endpoint mới dùng 400 `VALIDATION_ERROR` theo `CONVENTIONS.md`.

**Q8. Tên trong API và push.** Bản `debts` chưa từng phát hành nên đổi tên miễn phí. Đề xuất: mọi thứ phía app dùng chữ **loan**: `loanLink`, `loanSuggestions`, push action `choose_loan_link`, alert kind `loan_due`, công tắc `loanRemindersEnabled`. Phía AI giữ `relation_hint = debt_repayment` (mô tả nội dung câu, không phải tên bảng).

**Q9. Chat.** Đề xuất: giữ `TryRepayAsync` của `main` (khớp tên, đúng chiều, đúng một khoản → nháp "ghi lần trả"); **không** chép phần `debtSuggestions` cho nháp chat của bản `debts`. Chỉ sửa một chỗ: `SameName` / `FoldName` của chat và `DebtSuggestionRanker.NameMatches` dùng cùng một hàm (`CounterpartyKey`), để chat và thông báo khớp tên giống nhau.

**Q10. Thay `LoanDueReminderJob`.** Đề xuất: thay bằng job mới qua `forecast_alerts` (lịch D25: trước hạn 0..3 ngày một lần, quá hạn ngày 1, 8, 15), giữ giờ chạy 08:05 VN; `Program.cs` gọi `RecurringJob.RemoveIfExists("loan-due-reminder")` rồi đăng ký `loan-reminders`. Cột `due_notified_at` bỏ sau khi job mới chạy (migration xoá cột). Khoản đã được nhắc bằng job cũ trong hạn hiện tại có thể bị nhắc lại một lần ngay sau khi phát hành; chấp nhận (không đáng một bước chuyển dữ liệu riêng).

**Q11. Lãi trong dự báo và gợi ý.** Đề xuất: dự báo, spendable, gợi ý, Agent dùng **gốc còn lại** (`remainingCents`), không cộng lãi tạm tính, vì lãi chỉ để tham khảo và không phải số của bên cho vay. Trả hết gốc là `Settled` dù lãi tạm tính > 0. Ghi §13.4 nếu chủ sản phẩm muốn lãi vào sổ thật.

**Q12. `GET /loans` có phân trang không?** Hiện trả cả danh sách. Đề xuất: giữ nguyên (người dùng có ít khoản), thêm lọc tuỳ chọn `status`, `direction`; bỏ khoản đã xoá mềm. Thêm `GET /loans/{id}` (kèm các dòng) và `GET /loans/summary`.

**Q14. Tách một giao dịch ngân hàng có sẵn khi trả kèm lãi (S1).** Gắn giao dịch −2.060.000 vào khoản còn 2.000.000 nghĩa là giao dịch đó còn 2.000.000 (`loan`), cộng giao dịch lãi 60.000, số dư ví không đổi. Nhưng phép "cùng một sự kiện ngân hàng gửi lại" của Phase 82 so `(package, số tiền, số dư sau)`: sau khi tách, số tiền không còn khớp, nên thông báo gửi lại sẽ thành nháp **nghi trùng** (người dùng vẫn được hỏi, không bị cộng hai lần âm thầm), thay vì bị bỏ lặng lẽ. Đề xuất: khi so sự kiện gửi lại, nếu giao dịch có giao dịch lãi đi kèm (tìm theo id tất định), cộng số tiền hai giao dịch lại để so. Không thêm cột.

**Q15. Luật khớp tên (S3).** `CounterpartyKey` bỏ dấu, nên "Hoa" và "Hòa" thành cùng một chuỗi. Nhưng chat trên `main` coi "ban binh" và "Bạn Bình" là một người, và thông báo ngân hàng luôn viết hoa không dấu. Chỉ so hai cái tên thì không thể vừa "Hoa ≠ Hòa" vừa "ban binh = Bạn Bình", nên đề xuất xét theo **cả câu / cả nội dung**:
- Câu có chữ có dấu (người dùng gõ có dấu): so **giữ dấu**, nên "Hòa trả 500k" không khớp khoản "Hoa".
- Câu không có dấu nào (thông báo ngân hàng, hoặc gõ không dấu): so **bỏ dấu**, nên "HOA TRA NO" khớp cả "Hoa" lẫn "Hòa". Có hai khoản thì chat trả null như hiện tại, gợi ý thông báo liệt kê cả hai.
- So theo tập từ như `SameName` hiện tại (tên ngắn hơn ⊆ tên dài hơn), nên "Bạn An" ↔ "An" khớp mà không cần danh sách xưng hô. Với gợi ý từ thông báo: các từ của tên khoản, sau khi bỏ từ xưng hô đứng đầu ("bạn", "anh", "chị", "em", "cô", "chú", "bác", "ông", "bà", "dì", "cậu"), phải đều có trong nội dung. Danh sách xưng hô đặt ở `loans.honorifics` trong cấu hình.

**Q16. Cho vay/mượn có tính vào chi tiêu / thu nhập của báo cáo tháng không? (phát hiện ở Task 0, chưa có đề xuất chốt)** Phase 85 (S2) và Phase 86 dựa trên luật "khoản `loan` không phải chi tiêu hay thu nhập" (`LoanExclusion`, 20 chỗ). Test trên `main` (MinhTuan, 2026-11-17) ghi một yêu cầu ngày 2026-11-16: `monthly-summary` và `timeline` tính **dòng tiền thật** (cho mượn là chi, được trả là thu), chỉ `category-breakdown` loại `loan`; code chưa làm theo nên test đỏ. Hai luật không cùng đúng được. Ảnh hưởng tới Phase 90: kịch bản (b) ("gắn khoản trả vào sổ nợ thì tổng thu tháng giảm") và việc gắn giao dịch vào khoản vay có đổi tổng chi/thu tháng hay không. Cần chủ sản phẩm chốt trước Task 5.

**Q13. Nhánh `feat/debt-book`.** Đề xuất: không push, không merge; xoá nhánh cục bộ sau khi Phase 90 merge. Kế hoạch Phase 86 cũ chép vào `feat/loan-book` để giữ lại D1–D25, S1–S5 làm nguồn luật.

---

## Mô hình dữ liệu sau Phase 90

Migration EF mới `MergeDebtBookIntoLoans` (tạo sau khi rebase, trên snapshot của `main`):

```
loans (giữ mọi cột có sẵn)
  + status varchar(12) NOT NULL DEFAULT 'open'  CHECK IN ('open','settled','written_off')
  + repaid_cents bigint NOT NULL DEFAULT 0      -- lưu sẵn = Σ dòng trả còn sống
  ~ principal_cents                             -- từ nay = Σ dòng giải ngân còn sống (Q5)
  + note varchar(500) NULL
  + settled_at, written_off_at, deleted_at timestamptz NULL
  + client_request_id uuid NULL                 -- UNIQUE (user_id, client_request_id) WHERE NOT NULL
  - due_notified_at                             -- bỏ ở cuối Phase (Q10)
  CHECK repaid_cents >= 0 AND principal_cents >= repaid_cents
  CHECK (status = 'settled') = (settled_at IS NOT NULL)
  CHECK (status = 'written_off') = (written_off_at IS NOT NULL)
  ~ ck_loans_principal: principal_cents > 0 → principal_cents >= 0 (khoản chỉ còn dòng đã xoá vẫn hợp lệ trong lúc bị xoá theo, D5)
  INDEX (user_id, status, due_date) WHERE deleted_at IS NULL

loan_repayments (giữ tên bảng; giờ chứa cả dòng giải ngân)
  + interest_transaction_id uuid NULL           -- Q14: giao dịch lãi tách ra (S1); FK → transactions NO ACTION, UNIQUE khi khác null
  + kind varchar(10) NOT NULL DEFAULT 'repayment' CHECK IN ('principal','repayment')
  + deleted_at timestamptz NULL, client_request_id uuid NULL
  ~ transaction_id: thêm FK → transactions ON DELETE NO ACTION
  UNIQUE (transaction_id) WHERE transaction_id IS NOT NULL AND deleted_at IS NULL
  UNIQUE (loan_id, client_request_id) WHERE client_request_id IS NOT NULL

users.loan_reminders_enabled boolean NOT NULL DEFAULT true
forecast_alerts.kind CHECK thêm 'loan_due'
ai_results.relation_hint CHECK thêm 'debt_repayment'; + debt_hint_strength (strong|weak); cột ghi snake_case tường minh
```

Chuyển dữ liệu trong cùng migration (SQL):
1. Mỗi khoản: thêm một dòng `kind = 'principal'`, `amount_cents = principal_cents`, `paid_at = start_date 00:00 +07`, `transaction_id = NULL`.
2. `repaid_cents` = Σ dòng `repayment`; `status = 'settled'` + `settled_at = updated_at` khi bằng gốc.
3. Trước khi tạo UNIQUE trên `transaction_id`: nếu đã có hai dòng trỏ cùng giao dịch (không thể qua code cũ, nhưng kiểm cho chắc) thì migration dừng với thông báo, không tự sửa.

Cấu hình hệ thống nhóm `loans` (mới): `suggestWindowHours` 72, `maxSuggestions` 3, `reminderDaysBefore` 3, `overdueRepeatDays` 7, `maxOverdueReminders` 3, `interestToleranceCents` 10.000 (S1), `honorifics` (Q15).

`Loan.Title` giữ nguyên (tiêu đề do người dùng đặt). `Counterparty` là tên người: không log, không push, không LLM.

---

## API sau Phase 90 (`/api/v1/loans`)

| Method | Path | Thay đổi |
|---|---|---|
| GET | `/loans` | + lọc tuỳ chọn `status`, `direction`; bỏ khoản đã xoá; mỗi khoản thêm `status`… (trường cũ giữ nguyên, số đã trả vẫn là `paidCents`) |
| GET | `/loans/summary` | **mới** (D17) |
| GET | `/loans/{id}` | **mới**: khoản + các dòng |
| POST | `/loans` | + tuỳ chọn `createTransaction`, `transactionId`, `note`, `clientRequestId` (Q6); gửi lại → 200 |
| PUT | `/loans/{id}` | như cũ; luật đổi gốc theo Q5; + `note` |
| DELETE | `/loans/{id}` | xoá mềm (Q4) |
| POST | `/loans/{id}/repayments` | như cũ; + tuỳ chọn `clientRequestId`, hoặc `transactionId` thay cho số tiền để **gắn** một giao dịch đã xác nhận làm lần trả |
| POST | `/loans/{id}/principal` | **mới**: cho mượn thêm / vay thêm; cùng dạng với `repayments` |
| DELETE | `/loans/{id}/entries/{entryId}` | **mới**: xoá một dòng (gỡ liên kết) |
| POST | `/loans/{id}/write-off` | **mới** |
| POST | `/transactions/{id}/confirm` | + tuỳ chọn `loanId` (D4) |

DTO giao dịch: + `loanLink {loanId, title, counterparty, kind}`, + `loanSuggestions [...]`. Cashflow / spendable: `debt_outflow` / `debt_inflow` đổi tên thành `loan_outflow` / `loan_inflow`; `debtsDueCents` → `loansDueCents`; `undatedLoans`, `unlinkedLoanTransactions`. Push: `choose_loan_link`, kind `loan_due`.

---

## Tasks

Mỗi task: test trước, code, lớp test của tính năng; trước commit chạy unit + test liên quan. Một commit mỗi task. Không push, không mở PR khi chưa được đồng ý.

0. **Mốc và lỗi sẵn có.** (Task 0–2 không đụng `/loans`; cổng S5 nằm trước Task 3.) Xong 2026-11-23. Đo toàn bộ backend và ai-service trên `aba320c`, ghi vào Verification. Chép Task 0 của nhánh cũ: D23 (`GetRawTransactionsAsync` giữ dòng `loan`, cờ `IsLoan`; test `ForecastAccuracyJobTests.ALoanMovesTheActualBalance_…`) và đồng hồ giả cho `ListenerHealthScenarioTests.D_…`.
0b. **Dòng tiền riêng cho báo cáo (Q16)**, một commit. Xong 2026-11-23:
    1. `totalSpentCents` / `totalIncomeCents` của `monthly-summary`, `timeline` (từng ngày) và `daily_summaries` **giữ** loại `loan`. Ngân sách, hũ, spendable, dự báo, insight, chat, Agent, báo cáo nhóm không đổi.
    2. Trường tuỳ chọn mới cho `monthly-summary` và từng ngày của `timeline`: `loanOutflowCents` (Debit `loan`), `loanInflowCents` (Credit `loan`), `groupFundOutflowCents` / `groupFundInflowCents` (giao dịch cá nhân gắn quỹ, qua `TransferLegs`), `netCashFlowCents` = tổng thay đổi số dư của **mọi** ví do giao dịch đã xác nhận trong kỳ, tính **trực tiếp** từ tác động số dư (`TransferLegs`), không cộng từ các trường khác; chuyển ví nội bộ bù về 0.
    3. Bất biến kèm test: `netCashFlowCents = totalIncomeCents − totalSpentCents − loanOutflowCents + loanInflowCents − groupFundOutflowCents + groupFundInflowCents`, với hoàn tiền, cặp đảo, phí chuyển ví, chuyển ví nội bộ, góp/rút quỹ (giao dịch lãi S1 thêm vào test này ở Task 5). Loại giao dịch nào làm lệch → **dừng, báo thành mục D**, không thêm trường "khác".
    4. `daily_summaries` thêm các cột tương ứng (mặc định 0); migration backfill các ngày đã chốt trong 90 ngày gần nhất (hoặc đề xuất cách khác nếu nặng); `DailySummaryRefresher` / `DailySummaryJob` tính cả cột mới.
    5. Sửa và đổi tên test `ReportsControllerTests.LoanTransactions_CountAsRealCashFlow_ButStayOutOfTheCategoryBreakdown`: chi/thu 300.000 / 0; `loanOutflowCents` 5.000.000, `loanInflowCents` 2.000.000, `netCashFlowCents` −3.300.000; ngày của timeline cùng số; danh sách ngày vẫn 3 giao dịch; category-breakdown như cũ. Commit message nêu lý do và nhắc `3df2182`.
    6. Tài liệu: REQUIREMENTS FR-RPT-01 + §13.6 (quyết định 2026-11-23 kèm nguồn gốc); tài liệu API báo cáo; TASKS "Báo nhóm Flutter" (dòng "Số dư thay đổi" và phần vay nợ / quỹ); ARCHITECTURE nguyên tắc ở trên.
    7. Báo cáo Phase 90 có đoạn "Báo MinhTuan" giải thích vì sao test của cậu ấy đổi kỳ vọng.
1. **ai-service:** Xong 2026-11-23 (`4b095fc`). chép marker trả nợ (mạnh / yếu / loại trừ), cụm dài nhất thắng, `debt_hint_strength`; alembic **`0040`** sau `0039_user_item_memory`. Kiểm không đụng các nhóm từ vựng mới của `main` (`item_*`, `direction cues`).
2. **Backend nhận hint:** Xong 2026-11-23 (`7f8d164`; `debt_hint_strength` thêm ở cuối `ExtractionResult`, sau `ProviderReference`). `NotificationRelationHint.DebtRepayment`, `ai_results.debt_hint_strength`, cột `relation_hint` snake_case tường minh, `NotificationHints.Parse`, hợp đồng AI client.
3. **Migration `MergeDebtBookIntoLoans`** theo mục Mô hình dữ liệu, kèm chuyển dữ liệu; test schema (CHECK, UNIQUE, cascade khi xoá cứng user, dữ liệu cũ sau migration đúng gốc / đã trả / trạng thái).
4. **`LoanPolicy`** (chép `DebtPolicy`, đổi tên): chiều, điều kiện gắn, còn lại, trạng thái, xoá nợ. Unit test bảng.
5. **`LoanService` viết lại phần ghi** trên `LoanPolicy`: tạo ba cách, trả và giải ngân thêm trong **một** lượt lưu (thay cách lưu hai bước + gỡ hiện tại), gắn giao dịch, xoá dòng, sửa theo S2, xoá mềm (Q4), xoá nợ, trả kèm lãi theo S1 (+ Q14); `RetryingLoanService`. `LoanInterest.Accrued` đọc theo thứ tự thời gian cả dòng giải ngân (tăng dư nợ) lẫn dòng trả (giảm), bỏ dòng đã xoá; hôm nay theo `VietnamTime`. Giữ chữ ký `ILoanService` mà chat đang gọi (`ListAsync`). Sửa `Today()` sang `VietnamTime`. Bỏ tên người khỏi giao dịch tạo mới (Q3). `LoanServiceTests` cũ phải vẫn xanh, trừ test nào khẳng định hành vi Q3/Q4 cũ (sửa test theo quyết định, ghi rõ trong commit).
6. **Endpoint mới** của `/loans` + test integration (404 id người khác, 422, gửi lại, hai lần trả đồng thời).
7. **`ILoanLinkSync`** trên các handler có sẵn: xoá, sửa, gộp, chuyển ví giao dịch; `confirm` + `loanId`. Giải xung đột với các thay đổi của `main` ở `ConfirmTransactionCommandHandler`, `UpdateTransactionCommandHandler`.
8. **Gợi ý khi đọc** (`LoanSuggestionRanker`, `LoanTransactionView`), `loanLink` / `loanSuggestions` trên DTO giao dịch, push `choose_loan_link` trong luồng thông báo; ứng viên trong ngưỡng lãi S1. Hàm khớp tên dùng chung `PersonNameMatch` (S3, Q15) thay `FoldName` / `SameName` của chat; test chat cũ không đổi kỳ vọng, thêm test S3. DTO phía vận hành không kèm (S4 cũ của Phase 86). Test nhóm của S5.
9. **Dự báo:** `CashflowEvent.Band`, `LoanForecast` (đi vay cả ba đường, cho vay chỉ lạc quan), `spendable.loansDueCents`, `undatedLoans`, `unlinkedLoanTransactions` (S5); dùng gốc còn lại, không lãi (Q11).
10. **Nhắc hạn:** `LoanReminderJob` qua `forecast_alerts` kind `loan_due`, câu chung (S2), `loanRemindersEnabled`, cổng bỏ nhắc đã hoãn; gỡ `LoanDueReminderJob` (+ `RemoveIfExists`), xoá cột `due_notified_at`. Thay `LoanDueReminderJobTests` bằng test của job mới.
11. **Agent:** `get_loans` chỉ đọc, không tên người, không tiêu đề khoản (tiêu đề thường chứa tên).
12. **Kịch bản** `LoanBookScenarioTests`: (a)–(f) của Phase 86 cũ chạy qua `/loans` ((b) thêm assert Q16: thu tháng giảm, `loanInflowCents` tăng đúng bằng số đó, `netCashFlowCents` không đổi); (g) khoản vay tạo như app đang làm (`POST /loans` không ví) rồi trả qua thông báo ngân hàng có gợi ý; (h) trả có ví rồi thông báo ngân hàng cùng số về → nghi trùng → gộp, liên kết ở bản giữ lại, `paidCents` và số dư ví đúng (S4); (i) trả kèm lãi 2.060.000 (S1).
13. **Tài liệu:** `docs/loans-api.md` (mới, thay `debts-api.md` của nhánh cũ), `REQUIREMENTS.md` (FR-LOAN, §13.4: trả góp, thẻ tín dụng, cách tính khoản mất trắng, lãi có vào sổ thật không), `ERD.md`, `ARCHITECTURE.md` (quyết định "nợ là sổ phụ gắn với giao dịch `loan`"), `TASKS.md` Phase 90 + **Báo nhóm Flutter** (liệt kê mọi trường và endpoint mới, mọi hành vi đổi: Q3, Q4, Q5, Q10), `agent-api.md`, `CLAUDE.md`.
14. **Toàn bộ test** backend và ai-service; không thêm test đỏ so với mốc Task 0.

Thứ tự: 0 → 0b → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12 → 13 → 14 (9–11 làm song song được sau 6).

## Báo trước cho MinhTuan

Code của cậu ấy bị đổi ở: `LoanService` (phần ghi), `LoanRepository`, `LoanConfiguration`, `Loan` / `LoanRepayment`, `LoansController` (thêm endpoint, giữ endpoint cũ), `LoanDueReminderJob` (gỡ), `ChatCommandHandler.TryRepayAsync` (chỉ đổi hàm khớp tên). Nên báo trước khi bắt đầu Task 3 để tránh hai người cùng sửa.

## Verification

- **Mốc (đo 2026-11-23 trên worktree tách riêng tại `aba320c`, trước mọi thay đổi):**
  - ai-service: **2234 passed, 483 skipped** (không Postgres).
  - backend: **3628/3639, 11 test đỏ có sẵn trên `main`** (21 phút):
    - `MethodSelectionOnSeedDataTests…GetsCashFlowBudget…` (có từ `b6f1c42`);
    - `ReportsControllerTests.LoanTransactions_CountAsRealCashFlow_ButStayOutOfTheCategoryBreakdown`: test (MinhTuan, 2026-11-17) đòi báo cáo tháng và dòng thời gian **tính** cho vay/mượn vào chi/thu "theo yêu cầu 2026-11-16", code vẫn loại `loan` → xem Q16;
    - 7 test cùng một nguyên nhân: gợi ý quan hệ từ AI không còn được lưu (`RelationHint` null): `RefundNotificationTests` ×3, `RefundScenarioTests` ×3, `AnalyzeNotificationDuplicateTests.TheAiTransferHint_IsStoredForBothDirections`, cộng `TransferScenarioTests.B_AtmWithdrawal_…` (NullReference, có vẻ cùng gốc);
    - `NotificationsControllerTests.Analyze_HighConfidenceFinancialNotification_AutoCommitsAndUpdatesBalance` (nháp không còn tự xác nhận).
  - Phase 90 dựa vào gợi ý quan hệ của AI (Task 2, 8): lỗi "hint không được lưu" phải được sửa (hoặc hiểu rõ) trước Task 2.
  - **Đã sửa trước Task 2 (commit `fix:` riêng `814d54b`, 2026-11-23), vì nằm đúng phần hợp đồng AI client:** `ExtractionResult` bị chèn `ProviderReference` vào trước `RelationHint`, nên mọi chỗ tạo record theo vị trí lệch một ô. 8 test đỏ vì vậy, và **đường OCR thật** (`AIServiceClient.ScanReceiptAsync`) ghi `relation_hint` vào `ProviderReference`. `ProviderReference` về cuối record; test hồi quy `AiClientExtractionMappingTests`. Mốc đỏ sẵn còn lại: `MethodSelectionOnSeedDataTests…` và `NotificationsControllerTests.Analyze_HighConfidenceFinancialNotification_AutoCommitsAndUpdatesBalance` (nháp không tự xác nhận, ngoài phạm vi Task 2, để MinhTuan).
- Mốc cũ: đo ở Task 0, trên `aba320c`.
- Trước khi xin merge: toàn bộ backend và ai-service, không thêm test đỏ; `dotnet build` không cảnh báo mới; `alembic upgrade head` trên DB trống chạy qua `0040`.
- Test riêng cho chuyển dữ liệu: dựng DB ở migration trước `MergeDebtBookIntoLoans`, chèn khoản vay và lần trả theo dạng cũ, chạy migration, kiểm gốc / đã trả / trạng thái / dòng giải ngân đầu.

## Ngoài phạm vi

- Cộng lãi vào số còn nợ (S1 chỉ ghi phần lãi khi người ta trả kèm), trả góp, lịch trả nhiều kỳ, thẻ tín dụng (§13.4).
- Nợ giữa thành viên nhóm (`GroupDebtService` của `main`), không đổi.
- Sửa dữ liệu lịch sử: giao dịch cũ đã chép tên người vào `merchant_name` giữ nguyên.
- Client Flutter.
