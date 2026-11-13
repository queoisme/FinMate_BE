# Phase 83 — Ghép cặp chuyển tiền giữa các ví của chính mình thành Transfer

## Context

**Nạp ví.** Nạp MoMo từ Vietcombank sinh hai thông báo, VCB −500.000 và MoMo +500.000. Hệ thống đang ghi thành **chi 500k + thu 500k**, trong khi thật ra chỉ là tiền chuyển giữa hai ví của cùng một người.

**Rút ATM.** Trường hợp này còn phổ biến hơn và chỉ có MỘT thông báo (VCB −2.000.000 "RUT TIEN ATM"). Phía tiền mặt không có thông báo nào, nên khoản rút bị tính là chi tiêu.

**Hệ quả:** ngân sách, tổng thu, tỉ lệ tiết kiệm, dự báo thu nhập, thói quen chi, phát hiện bất thường và Agent đều lệch. Đây là phần "chuyển khoản nội bộ, nạp ví" của FR-TXN-18.

**Điều kiện bắt đầu:** đã kiểm.
- Phase 81 (`6cf0097`) và Phase 82 (`9df8ef5`) đều nằm trong `main` @ `1c45c09` (PR #30, #31).
- Nhánh `feat/self-transfer-pairing` tạo từ đó.

**Kiến trúc** (dùng lại, không viết lại):
- **Nhận diện:** hàm thuần `TransferMatcher` cạnh `DuplicateMatcher`, gợi ý tính khi đọc.
- **Chuyển đổi:** handler mới. Khoá dòng bằng `RunWithTransactionsLockedAsync`, tính lại ngân sách bằng `RefundLedger.RecomputeAsync`, việc sau khi lưu qua `RefundAfterSave`.
- **Ghép hai chiều:** gộp như Phase 82 (xoá mềm kèm `merged_into_transaction_id`, `merged_sources`).
- **Thông báo về sau một Transfer đã ghi tay** (BR-TRF-06): mở rộng `DuplicateMatcher` và `MergePolicy` của Phase 82.

---

## Danh sách 1 — Mọi chỗ đang loại `Transfer` khỏi tổng hợp (đã kiểm trên code)

**Loại tường minh**
1. `TransactionConfiguration`, các CHECK:
   - `chk_transactions_transfer_shape`: transfer có ví đích khác ví nguồn và không có danh mục;
   - `chk_transactions_group_shareable`: transfer không chia sẻ nhóm được.
2. `ForecastDataRepository.GetRawTransactionsAsync`: `TransactionType != Transfer`. Đây là nguồn của **mọi** bộ tính dự báo, kể cả `ForecastActuals`.
3. `GroupLedger` (dòng 184): transfer không vào sổ nhóm.
4. Phase 82:
   - `DuplicateMatcher.CanHaveDuplicates`;
   - `MergePolicy.EnsureMergeable`: từ chối transfer;
   - `TransactionRepository.FindPossibleDuplicateAsync`: `CounterAccountId == null`.
5. `CreateManualTransactionCommandValidator`: `POST /transactions` không tạo được transfer.
6. `UpdateTransactionCommandHandler`: cấm đổi giữa Transfer và loại khác (422).

**Loại ngầm** (chỉ cộng `Debit`/`Credit`)

7. `BudgetSpendScope.Counts`/`Signed`, dùng cho:
   - ngân sách cá nhân, ngân sách nhóm (`GroupFinanceRepository.SumSharedNetSpendAsync`);
   - backfill FR-BUD-01, `UserHardDeleter`;
   - `ReportRepository.GetCategorySpendAsync`, nguồn của hũ (`JarService`).
8. `TransactionBudgetDelta.Spend` → 0.
9. `NetFlow.Of` → `(0, 0, 0)`. Lưu ý: vẫn tính vào `transactionCount`. Dùng cho:
   - `ReportRepository.GetDailySpendAsync` → báo cáo tháng, timeline, cơ cấu danh mục;
   - `DailySummaryJob`, `DailySummaryRefresher`, `InsightGeneratorJob`;
   - Agent (`RepositoryFinancialDataSource`), `GroupReportService`, phần tóm tắt của chat.
10. `FinanceSnapshotRepository.GetDebitTotalAsync` → `/me/spendable`, Agent.
11. `ReportRepository.GetConfirmedDebitsAsync` → `MarketSpendProfile`. `SpentHereVerifier` và `CommunitySharePromptService` cũng chỉ lọc `Debit`.
12. Các bộ tính dự báo đều lọc thêm `Debit`/`Credit` (dư thừa vì đã có mục 2):
    - `SpendingBaseline`, `IncomeEstimator`;
    - `AnomalyDetector`/`AnomalyService`;
    - `RecurringDetector`/`RecurringMatcher`;
    - `CategoryForecastCalculator`, `BudgetRiskCalculator`;
    - `ForecastService` (phần đã chi hôm nay).
13. Phase 81: `RefundPolicy` (khoản gốc phải là `Debit`) và `RefundSuggestions`.

Hệ quả cho phase này: survivor đổi thành `Transfer` là tự rời khỏi **mọi** tổng hợp ở trên. Không phải sửa từng chỗ, chỉ cần tính lại ngân sách và chốt lại các ngày đã tóm tắt.

## Danh sách 2 — Mọi chỗ giả định một `Transfer` luôn có đủ hai ví đều được tính vào dự báo

| # | Chỗ | Giả định | Đúng khi một ví bị loại (FR-FCST-07)? |
|---|---|---|---|
| A | `ForecastDataRepository.GetRawTransactionsAsync` + `ForecastActuals.NetFlow` (số thực tế của độ chính xác dự báo) | Transfer luôn bù trừ về 0 trong tập ví được tính | **Sai.** Chuyển từ ví được tính sang ví bị loại làm số dư tập được tính giảm, nhưng "số thực tế" không thấy. Độ chính xác dự báo bị chấm sai |
| B | `RecurringDetector` / `RecurringMatcher` / `RecurringTracker` (đọc mục A) | Transfer không bao giờ là khoản định kỳ | **Sai.** Nạp 2 triệu hằng tháng vào ví tiết kiệm bị loại là tiền ra đều đặn khỏi tập được tính, nhưng không bao giờ được phát hiện hay chiếu vào `CashflowEngine.RecurringEvents`. Sau phase này, mọi khoản "nạp ví" đã chuyển đổi rơi vào đúng lỗ này |
| C | `ForecastService.ProjectAsync` khi xem một ví (`accountId`) | Lịch sử ví không có transfer | Đúng: số dư đầu là số thật; chi linh hoạt chỉ từ `Debit`, transfer không nên vào đó |
| D | `SpendableCalculator`, `PurchaseCheckService` | Dùng số dư hiện tại của các ví được tính | Đúng: số dư thật đã phản ánh transfer |
| E | `SpendingBaseline`, `IncomeEstimator`, `AnomalyDetector`, `CategoryForecastCalculator` | Transfer không phải thói quen chi / thu nhập | Đúng, và phải giữ như vậy (BR-TRF-11: không vào thói quen chi linh hoạt) |

Chỉ có **A** và **B** cần sửa (D16).

---

## Đã duyệt (2026-11-13)

D1–D22 làm theo đề xuất: D3 thêm `atm_deposit`; D7 chọn (a); D21 không thêm 4 số đuôi. Kèm các điều chỉnh S1–S6 của chủ dự án. Chỗ nào S mâu thuẫn với D thì S thắng.

- **S1 (D2).** Thêm nhóm `transfer_exclusion_markers`: nạp tiền điện thoại, nạp data, nạp thẻ, nạp game, VETC, ePass, thanh toán hoá đơn…
  - Câu khớp nhóm này thì **không** có `relation_hint = transfer`; nhóm này thắng mọi nhóm transfer.
  - Thứ tự ưu tiên: cashback > reversal > refund > transfer_exclusion > transfer (cụm dài nhất).
  - pytest bắt buộc: "Nap tien dien thoai Viettel 50,000" → không hint; "Nap tien tu Vietcombank" → `wallet_topup`.
- **S2 (D7, sửa BR-TRF-04).** `transfer_dismissed_at` chỉ tắt **gợi ý**.
  - `convert-to-transfer` gọi tay trên giao dịch đã bác vẫn hợp lệ.
  - Chuyển đổi thành công thì xoá cờ ở mọi chiều liên quan.
  - Có test cho trường hợp này.
- **S3 (D14).** Khi chuyển đổi, nếu một chiều đã khớp `recurring_items` **và** transfer nằm trọn trong tập ví được tính:
  - khoản định kỳ đó vào luồng hỏi "còn dùng không?" của FR-FCST-12, tức là cảnh báo `bill_status_check` qua `ForecastAlertDispatcher`, khoá dedup `bill_status_check:{itemId}:transfer:{transactionId}`;
  - **không** đổi trạng thái (khác luồng quá hạn, vốn chuyển sang `paused`), không huỷ;
  - người dùng trả lời bằng `resume`/`DELETE` có sẵn.

  Transfer vượt ranh giới thì giữ khớp. Test cả hai nhánh.
- **S4 (D16).** **Không** đổi ngữ nghĩa `GetRawTransactionsAsync`.
  - Thêm `IForecastDataRepository.GetBoundaryTransfersAsync(userId, countedAccountIds, from, to)`: chỉ transfer có đúng một ví thuộc tập được tính, trả tiền ra/vào có dấu theo góc nhìn tập đó.
  - Chỉ dùng ở `ForecastActuals` và các bộ định kỳ (`RecurringDetector`/`Matcher`/`Tracker`).
  - Mọi test dự báo hiện có phải pass mà **không sửa kỳ vọng**; buộc phải sửa thì dừng lại báo.
  - Ghi chỉ số độ chính xác dự báo trước và sau vào báo cáo phase. Bộ backtest có sẵn (`python -m agent_eval.backtest`) chấm dải dự báo của Agent trên hồ sơ giả lập, không đi qua `ForecastActuals`. Vì vậy có hai việc:
    - chạy lại nó trước/sau để chứng minh không đổi;
    - dựng một bộ dữ liệu cố định (bản chụp dự báo + giao dịch, có chuyển sang ví bị loại) và chạy `ForecastAccuracyCalculator` trước/sau sửa, ghi MAE và coverage vào báo cáo.
- **S5 (D17).** `transaction_merge_snapshots` có hạn lưu `snapshots.retentionDays` (nhóm cấu hình mới `snapshots`, mặc định 365, 30–3650).
  - Xoá trong `NotificationBodyPurgeJob` (03:15, job dọn dữ liệu cá nhân đã có), thêm một bước, đếm số dòng, không log nội dung.
  - Ghi lý do tối thiểu hoá dữ liệu vào REQUIREMENTS.
- **S6.** Khi chuyển đổi, xoá `exclude_from_forecast` và `forecast_exclusion_reason` của survivor.
  - Hệ thống hiện **không chặn** số dư âm: không có guard nào trong Application; `CreateTransfer`/`CreateManual` trừ thẳng.
  - `atm_deposit` một chiều làm ví Cash âm thì vẫn cho phép, response thêm `warnings: ["CASH_BALANCE_NEGATIVE"]` (mảng mã, tuỳ chọn), ghi vào tài liệu API.

## Quyết định đã duyệt (theo đề xuất, trừ chỗ S1–S6 sửa)

- **D1 — Hint cho cả khoản chi.** Phase 81 chỉ trả `relation_hint` cho tiền VÀO. "RUT TIEN ATM" và "chuyen tien den vi MoMo" lại là khoản chi.
  **Đề xuất:** `transfer` được trả cho cả hai chiều; `refund`/`reversal` vẫn chỉ cho tiền vào.
- **D2 — Hình dạng cấu hình marker.** Tài liệu `notification_vocab` chỉ chứa danh sách phẳng, mà yêu cầu cần ra được `transfer_kind`.
  **Đề xuất:** bốn nhóm danh sách, tài liệu gọi chung là "transfer_markers":
  - `transfer_atm_markers`;
  - `transfer_topup_markers`;
  - `transfer_withdrawal_markers`;
  - `transfer_own_account_markers`.

  Validator từ chối một cụm nằm ở hai nhóm. Thứ tự ưu tiên: cashback > reversal > refund > transfer, nên "hoàn tiền nạp ví" vẫn là refund. Trong các nhóm transfer thì **cụm khớp dài nhất** quyết định `transfer_kind`. Danh sách mặc định không có cụm trơn "chuyen tien" / "chuyển tiền", để (d) "chuyển cho bạn" không trúng.
- **D3 — Thêm `atm_deposit`.** "NOP TIEN TAI ATM" là tiền mặt vào ngân hàng: chiều Credit, ví nguồn là Cash. Bốn loại đã chốt không có trường hợp này.
  **Đề xuất:** thêm `atm_deposit`, gợi ý ví Cash làm ví nguồn. Nếu anh muốn giữ đúng bốn loại thì xếp nó vào `own_account`.
- **D4 — Kiểu của `ai_results.relation_hint`.** Cột đang dùng enum `TransactionRelation`, cũng là kiểu của `transactions.relation`. Thêm `Transfer` vào đó thì `transactions.relation = transfer` sẽ biên dịch được, chỉ còn CHECK ở DB chặn.
  **Đề xuất:** enum riêng `NotificationRelationHint {Refund, Reversal, Transfer}` cho `ai_results`, kèm cột mới `ai_results.transfer_kind` có CHECK. `TransactionRelation` giữ nguyên.
- **D4b — Phía ai-service.** Thêm `relation_hint = 'transfer'` và cột `transfer_kind` vào `ops_analyze_log` (alembic `0038`). Response `/analyze` thêm trường tuỳ chọn `transfer_kind`.
- **D5 — Tín hiệu "nhắc tên provider của ví bên kia".** Có ba phương án: đọc `notification_body` lúc đọc danh sách (nặng, và body bị xoá sau 90 ngày), lưu tên đã khớp lúc analyze, hoặc chỉ so trên dữ liệu đã có.
  **Đề xuất:** chỉ so `MerchantName` + `Description` của giao dịch (đã có sẵn) với bí danh của provider ví kia, chuẩn hoá bằng `CounterpartyKey`. Bí danh nằm trong cấu hình hệ thống `transfers.providerAliases`, mặc định:
  - `vietcombank: [vcb, vietcombank]`;
  - `momo: [momo]`;
  - `zalopay: [zalopay, zalo pay]`;
  - `vnpay: [vnpay]`;
  - và theo `provider_key`/tên hiển thị.

  Không đọc body, không lưu thêm chữ. Giới hạn: AI bóc mô tả thiếu tên ví thì không có tín hiệu này.
- **D6 — Thông báo tiền vào đến trước tiền ra.** Hai push có thể về lệch nhau vài giây.
  **Đề xuất:** cho Credit sớm hơn Debit tối đa `transfers.clockSkewMinutes` = 2 (cùng ý với QR).
- **D7 — Nơi lưu "không phải chuyển ví" (BR-TRF-12).** Hai phương án:
  - (a) Cột `transactions.transfer_dismissed_at`. `not-transfer` đặt cột này cho giao dịch được gọi **và** cho chiều đang được gợi ý ghép cùng, nếu có. Từ đó không gợi ý gì có dính tới hai giao dịch này nữa, cả ghép lẫn một chiều.
  - (b) Bảng `transfer_dismissals(user_id, transaction_id, paired_transaction_id?)` lưu từng cặp.

  **Đề xuất (a).** Ở (f), nếu chỉ bác chiều Debit, chiều MoMo +500k vẫn mang hint `wallet_topup` và sẽ bật gợi ý một chiều "chọn ví nguồn", tức là người dùng vừa bác xong lại bị hỏi. Bác theo cặp mà vẫn hỏi lẻ từng chiều thì hiếm khi đúng ý, không đáng thêm một bảng.
- **D8 — Phí trong BR-TRF-06.** Quy tắc vừa đòi "cùng số tiền" vừa nói phí xử lý theo BR-TRF-08.
  **Đề xuất:**
  - thông báo **Debit** khớp khi số tiền = số của Transfer + phí trong giới hạn `maxFeeCents`/`maxFeePercent`; gộp xong thì tạo khoản phí như BR-TRF-08;
  - thông báo **Credit** phải bằng đúng số tiền.
- **D9 — Gộp với một Transfer** (mở rộng `MergePolicy` của Phase 82).
  - **Transfer luôn là survivor**, không theo BR-DUP-09: cả hai đã xác nhận mà thông báo tạo trước thì luật cũ sẽ giữ thông báo và làm mất Transfer.
  - Chỉ ghép được Debit ở **ví nguồn** hoặc Credit ở **ví đích**.
  - Giờ ngân hàng lấy từ chiều Debit; chiều Credit chỉ đặt giờ khi chưa có thông báo nào gộp vào.
  - `balance_after_cents` chỉ lấy từ chiều Debit, vì cột này nói về ví nguồn.
  - Danh mục luôn null; `categoryId` trong body bị bỏ qua.
- **D10 — Ai mang `transferSuggestion`.** **Đề xuất:**
  - chỉ giao dịch **nháp**, hoặc giao dịch đã xác nhận nhưng tạo trong `pairWindowMinutes` gần nhất, để app vừa lưu xong vẫn thấy gợi ý;
  - chiều được ghép cùng có thể ở bất kỳ trạng thái nào;
  - danh sách lớn chỉ tính theo lô, như Phase 82.
- **D11 — Response của `convert-to-transfer`.** **Đề xuất:** DTO mới `{transfer: TransactionDto, fee: TransactionDto?}`; `/preview` trả cùng dạng kèm `balanceChanges[]`.
- **D12 — Gọi lại và mã lỗi.**
  - Gọi lại với giao dịch đã là Transfer cùng ví đích (và cùng chiều ghép đã gộp vào nó) → 200 như cũ.
  - Khác input → 422 `TRANSFER_NOT_ALLOWED`, lý do trong message.
  - Mã mới:
    - `TRANSFER_SHARED_NOT_ALLOWED` (422, BR-TRF-04);
    - `TRANSFER_NOT_ALLOWED` (422, các lý do còn lại);
    - `TRANSFER_PAIR_MISMATCH` (422: `counterAccountId` khác ví chiều kia, khác chiều, phí ngoài giới hạn).
  - Dùng lại `TRANSACTION_TRANSFER_SAME_ACCOUNT`.
  - Ví đích của người khác → 404.
- **D13 — Hình dạng khoản phí** (BR-TRF-08).
  - Nguồn = nguồn của chiều Debit.
  - Mô tả "Phí giao dịch"; danh mục hệ thống `other`; `Confirmed`.
  - `clientRequestId` = UUID v5(id survivor, "fee"). Backend chưa có hàm UUID v5 nên viết một hàm nhỏ `DeterministicGuid`.
  - `balance_after_cents` = số dư ngân hàng báo của chiều Debit; Transfer khi đó không mang số dư sau.
  - Khoản phí là giao dịch độc lập: xoá Transfer không xoá phí.
- **D14 — Khoản định kỳ (BR-TRF-10).** *(S3 bổ sung: transfer nằm trọn trong tập ví được tính → hỏi "còn dùng không?".)* Thực tế trong code:
  - `RecurringTracker` đã đẩy `NextDueDate` khi thấy khoản chi khớp;
  - chuyển nó thành Transfer thì khoản đó biến khỏi dữ liệu dự báo, nhưng job đêm **không** lùi lịch (giống R1 của Phase 81 nếu không làm gì).

  **Đề xuất:** giữ khớp, không lùi lịch, không tự tạm dừng. Kết hợp với D16 (transfer vượt ranh giới ví được tính vẫn được bộ định kỳ thấy), một khoản "nạp tiết kiệm hằng tháng" vẫn được chiếu đúng. Chỉ trường hợp cả hai ví đều được tính mới còn chiếu dư một khoản tiền ra; ghi vào §13.1 thay vì tự tạm dừng khoản của người dùng.
- **D15 — Đánh dấu bất thường.** Thêm `AnomalyResolution.Transfer` và mở rộng CHECK `chk_transaction_anomalies_resolution`. Đánh dấu đang mở của các chiều được đóng với lý do `transfer`, không thông báo. Client không gửi được giá trị này (giống `refunded`/`reversed`).
- **D16 — Dự báo (BR-TRF-11) và sửa quyết định #1 của `ARCHITECTURE.md`.** *(Phần "cách làm" dưới đây đã được S4 thay: không đổi `GetRawTransactionsAsync`, dùng truy vấn riêng `GetBoundaryTransfersAsync`.)* Quyết định #1 đang ghi transfer "**KHÔNG** vào forecast".
  **Đề xuất sửa thành:** transfer giữa hai ví cùng được tính → không vào đâu cả (như cũ). Transfer **vượt ranh giới** tập ví được tính → là tiền ra/vào của tập đó:
  - vào `ForecastActuals` (sửa A);
  - vào `RecurringDetector`/`Matcher`/`Tracker` dưới dạng tiền ra/vào (sửa B);
  - **không bao giờ** vào `SpendingBaseline`, `IncomeEstimator`, `AnomalyDetector`, `CategoryForecastCalculator`.

  Cách làm:
  - `ForecastTransaction` thêm `IsTransfer` và `CounterAccountId`;
  - `GetRawTransactionsAsync` trả cả transfer;
  - một hàm thuần `TransferFlow.ForCountedSet(transactions, countedIds)` biến transfer vượt ranh giới thành một dòng có dấu đúng và bỏ transfer nội bộ;
  - `GetTransactionsAsync` (thói quen) loại mọi transfer như cũ.

  Hệ quả phụ cần anh biết: người đang chuyển tay đều đặn vào ví tiết kiệm bị loại sẽ bắt đầu được gợi ý khoản định kỳ. Em cho đây là đúng.
- **D17 — Bảng ảnh chụp (phần B).** `transaction_merge_snapshots`:
  - FK `user_id → users` ON DELETE CASCADE. Xoá cứng đi theo đường `UserHardDeleter` xoá dòng `users`; có test qua `DataDeletionJob`;
  - `survivor_id`, `merged_id` **không** FK: đây là lịch sử, và FK sẽ chặn xoá cứng theo thứ tự;
  - thêm cột `transfer_kind` (nullable) cho thống kê;
  - `before` jsonb gồm:
    - các trường đã đổi của từng dòng (loại, ví, ví đích, số tiền, danh mục, trạng thái, thời điểm, số dư sau, các cột trùng/gộp, `notification_log_id`);
    - danh sách tham chiếu đã chuyển (ý định QR, liên kết hoá đơn, nháp hoàn, liên kết trùng, đánh dấu bất thường đã xoá kèm nội dung để khôi phục);
    - id khoản phí đã tạo;
    - thay đổi số dư từng ví.

  Đủ để làm unmerge sau này. Không log nội dung.
- **D18 — Xem trước.** `merge/preview` và `convert-to-transfer/preview` nhận đúng body như lệnh thật và chạy cùng bộ quyết định thuần. Merge của Phase 82 tách thành "lập kế hoạch" (thuần) và "thực hiện". Không khoá, không ghi. Trả 404 và 422 y như lệnh thật.
- **D19 — Thống kê admin.** Gợi ý chỉ tính khi đọc nên không đếm được "số lần đã gợi ý".
  **Đề xuất:**
  - "gợi ý" = số kết quả AI có `relation_hint = transfer` theo `transfer_kind`;
  - "chấp nhận" = số snapshot `transfer_pair`/`transfer_convert` theo `transfer_kind`; ghép chỉ nhờ tên provider thì vào nhóm `unhinted`;
  - "từ chối" = số giao dịch có `transfer_dismissed_at` theo `transfer_kind`;
  - chỉ số đếm, không id, không theo user.
- **D20 — Thứ tự action của push.** `review_duplicate` > `review_transfer` > `choose_refund_link` > `confirm_one_tap` > `choose_category`. Nháp đang `Suspected` (kể cả khi nghi trùng với một Transfer, BR-TRF-06) không mang `transferSuggestion` (BR-TRF-04).
- **D21 — 4 số đuôi tài khoản.** Giúp ghép `own_account` (VCB → TCB của chính mình) nhưng không giúp nạp/rút ví hay ATM, là phần lớn trường hợp.
  **Đề xuất:** không thêm trong phase này; ghi §13.4 để đo tỉ lệ `own_account` bị bỏ lỡ trước khi quyết.
- **D22 — Chat `transfer_money`.** Giữ như hiện nay: bản nháp → `POST /transactions/transfer`. Các thông báo về sau đi BR-TRF-06.

---

## Thiết kế

**ai-service**
- Thêm nhóm vocab (D2) và `relation_hint_for` cho cả hai chiều (D1). `transfer_kind` theo cụm dài nhất.
- `ExtractionResult.transfer_kind`; alembic `0038` cho `ops_analyze_log`.
- Validator từ chối từ trùng giữa các nhóm.

**Backend — dữ liệu (migration `AddSelfTransferPairing`)**
- `ai_results`:
  - CHECK `relation_hint` thêm `transfer`;
  - cột `transfer_kind` + CHECK `atm_withdrawal|atm_deposit|wallet_topup|wallet_withdrawal|own_account`.
- `transactions.transfer_dismissed_at`.
- CHECK `chk_transaction_anomalies_resolution` thêm `transfer`.
- Bảng `transaction_merge_snapshots` (D17).

**Backend — cấu hình** `TransferSettings` (nhóm `transfers`):

| Khoá | Mặc định | Khoảng |
|---|---|---|
| `pairWindowMinutes` | 30 | 1–240 |
| `clockSkewMinutes` | 2 | 0–10 |
| `maxFeeCents` | 20.000 | 0–200.000 |
| `maxFeePercent` | 2 | 0–10 |
| `providerAliases` | xem D5 | mỗi bí danh ≥ 2 ký tự |

**Backend — `Application/Transactions/Transfers/`**
- `TransferMatcher` (thuần): BR-TRF-02/03/04, D5, D6. Trả `TransferSuggestion{kind, paired?, candidateAccounts, feeCents?}`.
- `TransferConversion` (thuần): BR-TRF-07/08/09. Từ trạng thái hai chiều ra:
  - ví nguồn/đích, số tiền, khoản phí;
  - **thay đổi số dư từng ví**, tính bằng: hoàn tác tác động hiện có của mỗi chiều đã xác nhận, rồi áp transfer + phí.

  Bất biến BR-TRF-07 được kiểm trên chính hàm này cho cả 4 tổ hợp trạng thái.
- `ITransferSuggestions`: tính khi đọc theo lô, dùng cho DTO, analyze, push.
- `ConvertToTransferCommandHandler` + `Retrying…` + `NotTransferCommandHandler`:
  1. khoá các dòng theo id tăng dần;
  2. kiểm tra BR-TRF-04;
  3. áp `TransferConversion`;
  4. survivor đổi loại, bản kia xoá mềm kèm `merged_into`; tạo khoản phí;
  5. đóng đánh dấu bất thường (D15);
  6. ghi snapshot;
  7. `RefundLedger.RecomputeAsync`, lưu **một lần**, rồi `RefundAfterSave` (cache, cảnh báo, `daily_summaries`, dự báo).
- `MergePlanner` (tách từ handler gộp của Phase 82 cho D18). Mở rộng `DuplicateMatcher`/`MergePolicy` cho BR-TRF-06 (D8, D9). Snapshot `duplicate_merge` ghi ở mọi lượt gộp.
- `TransferFlow.ForCountedSet` (D16).

**Nối vào các luồng**
- `AnalyzeNotificationCommandHandler`, `RetryFailedNotificationJob`: lưu hint/kind cho cả hai chiều; push `review_transfer` (D20).
- `RefundSuggestions` bỏ qua hint `transfer`.
- DTO thêm `transferSuggestion`.
- `/admin/ai-stats` (D19).
- API:
  - `POST /transactions/{id}/convert-to-transfer` và `/preview`;
  - `POST /transactions/{id}/not-transfer`;
  - `POST /transactions/{id}/merge/preview`.

---

## Task (TDD; mỗi task một commit nhỏ; không đụng `docs/diagrams/*.drawio`)

0. **Bắt đầu.** Chép quyết định đã duyệt vào kế hoạch này. Thêm vào `.context/ARCHITECTURE.md`: quyết định #1 sửa theo D16, bất biến số dư BR-TRF-07, quy tắc ghép cặp. Mốc test: Phase 82 (2862/2863; ai-service 2003/453). Commit.
1. **ai-service.**
   - Nhóm marker, `relation_hint = transfer` hai chiều, `transfer_kind`, alembic `0038`, validator.
   - pytest cho mỗi kind với câu thật:
     - "RUT TIEN ATM";
     - "chuyen tien den vi MoMo";
     - "Nap tien tu Vietcombank";
     - "rut ve ngan hang";
     - "NOP TIEN TAI ATM";
     - "CK den TK chinh chu".
   - Thêm: "hoàn tiền nạp ví" → refund; "chuyen cho ban" → không hint; tài liệu DB cũ thiếu nhóm mới vẫn chạy. `black/ruff/isort`.
2. **Migration + domain** (D4, D7, D15, D17). Test lược đồ: mọi CHECK; xoá cứng user có snapshot.
3. **`TransferSettings`** + validator + audit.
4. **`TransferMatcher` (unit).** Mọi nhánh BR-TRF-02/03/04:
   - (d) không có tín hiệu → không gợi ý;
   - phí ở biên;
   - Credit đến trước;
   - ATM với 0, 1 hoặc nhiều ví Cash;
   - đã bác, đã chia sẻ, đang nghi trùng, đã khớp QR, có `relation`.
5. **`TransferConversion` (unit).** Bất biến BR-TRF-07 cho Draft/Draft, Draft/Confirmed, Confirmed/Draft, Confirmed/Confirmed, có và không có phí, và cho một chiều. Survivor theo BR-TRF-08.
6. **Repository + `ITransferSuggestions` + DTO + analyze/push/retry.**
7. **`convert-to-transfer` + `not-transfer`.**
   - Test controller: 200, gọi lại 200, 404, 422 cho từng mã.
   - **Test đồng thời:** hai request chuyển đổi cùng một cặp → một Transfer, số dư đúng.
8. **Phần B.** `MergePlanner`, hai `/preview`, snapshot ở mọi đường gộp/chuyển đổi, hạn lưu (S5).
   - Test: preview không ghi gì và khớp đúng kết quả của lệnh thật.
   - Snapshot đủ để dựng lại trạng thái trước.
9. **BR-TRF-06** (D8, D9): `DuplicateMatcher` + `MergePolicy` + phí khi gộp với Transfer.
10. **Dự báo** (S3, S4): `GetBoundaryTransfersAsync`, `ForecastActuals`, các bộ định kỳ, câu hỏi "còn dùng không?".
    - Test: ví bị loại, ví được tính, chuyển nội bộ, không vào thói quen chi.
    - Mọi test dự báo cũ pass không sửa.
    - Đo độ chính xác trước/sau.
11. **Tổng hợp sau chuyển đổi** (`Integration/Transactions/TransferAggregatesTests.cs`):
    - ngân sách;
    - báo cáo tháng/timeline/cơ cấu danh mục;
    - `daily_summaries`;
    - hũ;
    - `/me/spendable`;
    - cashflow (gồm ví bị loại);
    - thu nhập dự báo;
    - bất thường;
    - công cụ Agent (`SpendingSummaryTool`/`PlanBudgetTool`/`RunwayTool`).
12. **Thống kê admin** (D19).
13. **Kịch bản đầu-cuối** `Integration/Scenarios/TransferScenarioTests.cs` (ngân sách tổng 5.000.000đ): (a)–(f) đúng như yêu cầu.
14. **Chạy toàn bộ** backend và ai-service. Không có test đỏ hay cảnh báo build mới so với mốc.
15. **Tài liệu.**
    - `REQUIREMENTS.md`:
      - FR-TXN-18;
      - FR mới cho chuyển đổi, từ chối, xem trước;
      - §13.1: chưa có hoàn tác (đã có snapshot) và định kỳ khi cả hai ví được tính (D14);
      - §13.4: danh mục phí riêng, 4 số đuôi tài khoản.
    - `ERD.md`.
    - `TASKS.md` (Phase 83 + "Báo nhóm Flutter").
    - `docs/transactions-transfer-pairing-api.md`.
    - `CLAUDE.md`.

Thứ tự: 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12 → 13 → 14 → 15. Task 1 làm song song được với 2–5. Không push, không mở PR khi chưa được đồng ý.

## Verification

- Trong lúc code: chạy lớp test của task. Trước mỗi commit: unit + các lớp tích hợp liên quan. Chạy toàn bộ khi đụng migration, `Program.cs` hoặc đường tiền (Task 2, 7, 9, 10, 14).
- Bắt buộc phải pass:
  - bất biến BR-TRF-07 cho mọi tổ hợp;
  - kịch bản (a)–(f);
  - test đồng thời;
  - pytest cho từng `transfer_kind`;
  - mỗi BR-TRF-01…13 có ít nhất một test (bảng BR → test thêm vào cuối kế hoạch này khi xong).
- `dotnet ef migrations script` áp được lên DB thử; `alembic upgrade head` chạy được trên Postgres thử.
