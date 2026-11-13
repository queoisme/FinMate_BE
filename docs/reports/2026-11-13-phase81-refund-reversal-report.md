# Báo cáo thực hiện Phase 81 — Hoàn tiền (Refund) và Đảo giao dịch (Reversal)

| | |
|---|---|
| Ngày | 2026-11-13 |
| Nhánh | `feat/refund-reversal` (tách từ `main` @ `485dd12`), 15 commit, **chưa push, chưa mở PR** |
| Yêu cầu | FR-TXN-18, quy tắc BR-RF-01…13, quyết định D1–D19 và bổ sung R1–R4 đã được chủ dự án duyệt |
| Kế hoạch | `docs/superpowers/plans/2026-11-13-refund-reversal.md` |
| Tài liệu API cho app | `docs/transactions-refund-api.md` |
| Kết quả test | Backend 2717/2718 (chỉ FR-FIN-07 đỏ, như mốc); ai-service 2003 qua / 453 bỏ qua khi không có Postgres, tích hợp 453/453 trên Postgres |

---

## 1. Vấn đề và mục tiêu

Trước Phase 81, khoản "hoàn tiền" bị ghi như một khoản **thu nhập độc lập**: `TransactionType` chỉ có `Debit`, `Credit`, `Transfer`, và không có gì nối khoản hoàn với khoản chi gốc. Hệ quả:

- chi tiêu và thu nhập đều bị phóng đại (mua 900k, hoàn 900k → chi 900k và thu 900k);
- `spent_cents` của ngân sách không giảm, nên cảnh báo T1/T2/vượt vẫn đúng như chưa hoàn;
- báo cáo, dự báo (Flow 6), phát hiện bất thường, nhận diện định kỳ và AI Agent đều lệch số.

Mục tiêu: khoản hoàn liên kết được với khoản chi gốc và làm giảm **chi ròng** của đúng danh mục đó. Mọi nơi đang cộng dồn chi hoặc thu phải xử lý nó **giống nhau**. Hệ thống chỉ **gợi ý** liên kết, người dùng mới là người quyết định.

## 2. Cách tiếp cận

### 2.1 Mô hình dữ liệu

Khoản hoàn là một giao dịch `Credit` (tiền vào, số dư +) có thêm:

- `relation` ∈ {`refund`, `reversal`};
- `related_transaction_id` trỏ tới khoản chi gốc (null với hoàn "mồ côi");
- `is_reversed` trên khoản chi gốc khi nó bị đảo.

Không thêm `TransactionType` mới, vì mọi nhánh số dư hiện có (`Credit` → +) vẫn đúng và `Transfer` không bị đụng tới.

### 2.2 Ba chốt chặn thay cho việc rải quy tắc khắp nơi

| Chốt chặn | Dùng cho | Quy tắc |
|---|---|---|
| `TransactionBudgetDelta.Spend` + `BudgetSpendScope` | Ngân sách cá nhân và nhóm; backfill khi tạo ngân sách; tính lại nhóm khi xoá cứng tài khoản | Chi chưa đảo: +; hoàn: −; đảo: 0; cho vay/mượn: 0. Viết dạng `Expression`: EF dịch sang SQL, còn bộ nhớ thì `Compile()` để xét các giao dịch đang sửa, nên chỉ có **một** định nghĩa |
| `NetFlow` | Báo cáo cộng trong bộ nhớ (theo ngày, timeline, báo cáo nhóm) | Chi = chi − hoàn; thu không gồm hoàn; hoàn đếm riêng; cặp đảo bị loại khỏi mọi con số kể cả số lượng |
| `RefundNetting.ForHabits` | Mọi bộ tính dự báo (qua `IForecastDataRepository.GetTransactionsAsync`) | Bỏ cặp đảo; bỏ khoản chi hoàn 100% cùng khoản hoàn của nó; hoàn một phần dùng số ròng; khoản hoàn không bao giờ là thu nhập. Đối chiếu số dư dùng `GetRawTransactionsAsync` (dữ liệu thô) |

### 2.3 Hai lựa chọn kỹ thuật quan trọng

**Tính lại kỳ thay vì cộng delta (D2).** Trước đây ngân sách cộng delta rồi chặn ở 0. Với hoàn tiền thì cách đó làm số lệch:

1. Mua 900k ở tháng 10, hoàn ở tháng 11 khi tháng 11 chưa chi gì: −900k bị chặn, tháng 11 vẫn là 0.
2. Sau đó xoá khoản hoàn: +900k → tháng 11 thành 900k, một khoản chi không có thật.

Vì vậy, mọi thao tác dính tới hoàn/đảo đều **tính lại** kỳ:

```
spent = max(0, chi ròng trong DB (bỏ các giao dịch đang sửa) + phần đóng góp của chúng tính trong bộ nhớ)
```

Cảnh báo chỉ được xét khi số mới lớn hơn số cũ, và cờ `alert_*_sent_at` không bao giờ bị reset (BR-RF-05).

**Khoá dòng khoản gốc (D19).** Hai khoản hoàn gửi đồng thời vào **hai ví khác nhau** sẽ lọt qua khoá xmin của ví. Cách xử lý: `RunWithOriginalLockedAsync` chạy `SELECT … FOR UPDATE` trên dòng khoản gốc, lọc theo `user_id`, trong một DB transaction tường minh. Thứ tự khoá cố định: **khoản gốc → ví → kỳ ngân sách**. Mọi việc bên ngoài (push, AI, chốt tóm tắt, dự báo) chạy sau khi đã commit. Các handler mới đều có wrapper `Retrying*`.

## 3. Các quyết định đã chốt

| Mã | Nội dung đã làm |
|---|---|
| D1/R4 | Đảo giao dịch hoàn tác khoản gốc **ở kỳ của khoản gốc**, cả ngân sách cá nhân lẫn ngân sách nhóm. BR-RF-04 (ghi nhận ở kỳ của ngày hoàn) chỉ áp cho hoàn tiền |
| D2 | Tính lại kỳ bằng predicate dùng chung với backfill FR-BUD-01, áp cho cả `user_id` lẫn `group_id` |
| D3 | Tổng chi kỳ = max(0, chi − hoàn); danh mục ròng ≤ 0 không có dòng; thêm `crossPeriodRefundCents` để app giải thích chỗ chênh |
| D4 | Lệnh đảo phải bằng đúng số gốc và khoản gốc chưa được hoàn phần nào |
| D5 | Hoàn mồ côi tạo qua `POST /transactions` với `relation`, hoặc `PUT /related` với `relatedTransactionId = null` |
| D6 | Hint lưu ở `ai_results.relation_hint`; danh sách gợi ý tính khi đọc, không lưu |
| D7 | Thêm cột `daily_summaries.total_refunded_cents`; các đường hoàn/đảo chốt lại ngày đã chốt |
| D8/D9 | Giữ "hoàn tiền" trong `credit_verbs` (từ này quyết định chiều tiền, và hoàn đúng là tiền vào). Đường thông báo dùng `relation_hint` mới, chat dùng `income_type` có sẵn |
| D10(a) | `cashback`, `hoan tien khuyen mai`, `tien thuong hoan` chuyển sang `other_income`; ghi lý do vào `dictionaries/NOTES.md` |
| D11 | Thao tác của khách không ghi `audit_logs`, như hiện nay |
| D12 | Điều kiện khoản gốc hợp lệ. Cửa sổ 60 ngày (hoàn) / 3 ngày (đảo) **chỉ** giới hạn gợi ý; liên kết tay không giới hạn thời gian |
| D13 | Khoản hoàn đã liên kết: khoá danh mục, luôn là tiền vào, đổi số tiền thì kiểm lại BR-RF-02 |
| D14 | Dự báo trừ hoàn một phần vào ngày của khoản chi (báo cáo thì trừ vào ngày hoàn); hoàn mồ côi bị loại khỏi dự báo |
| D15 | `.context/ARCHITECTURE.md` thêm nguyên tắc hoàn tiền, đặt cạnh quyết định #1 về Transfer |
| D16 | Nháp có hint luôn push `choose_refund_link`, kể cả khi tin cậy cao hay không có ứng viên nào |
| D17 | Chat trả `relationHint` và gợi ý; không thêm đường lưu mới |
| D18 | Xoá khoản gốc thì các nháp đang trỏ tới nó mất liên kết |
| D19 | Khoá `FOR UPDATE`, thứ tự cố định, ghi trong comment của repository |
| R1 | Đảo khoản trả hoá đơn → khoản định kỳ lùi một chu kỳ (`RecurringSchedule.Previous`), lấy lại lần trả trước. Hoàn tiền (kể cả hoàn đủ) không đổi trạng thái đã trả |
| R2 | Bất thường còn mở của khoản chi hoàn 100% hoặc bị đảo tự đóng với lý do `refunded`/`reversed`, không thông báo; app không gửi được hai giá trị này |
| R3 | Ý định QR đã khớp vẫn giữ `Matched` khi khoản bị đảo, nên không bị khớp lần hai. Cơ chế có sẵn; đã thêm test và ghi vào tài liệu |

## 4. Thay đổi chi tiết

### 4.1 Cơ sở dữ liệu

**Backend — migration `AddTransactionRefundRelation`:**

- `transactions`: thêm `related_transaction_id` (FK tự tham chiếu `ON DELETE NO ACTION`, index một phần), `relation`, `is_reversed`. Thêm 6 CHECK:
  - giá trị `relation` hợp lệ;
  - có `relation` thì phải là tiền vào;
  - có `related_transaction_id` thì phải có `relation`;
  - không tự trỏ vào chính nó;
  - đảo bắt buộc có khoản gốc;
  - `is_reversed` chỉ đặt trên khoản chi.
- `ai_results.relation_hint` (có CHECK).
- `daily_summaries.total_refunded_cents`.
- `transaction_anomalies.resolution` chấp nhận thêm `refunded`, `reversed`.

FK dùng `NO ACTION` vì Postgres kiểm nó ở cuối câu lệnh, nên xoá cứng cả user trong một câu không vướng thứ tự giữa khoản gốc và khoản hoàn. Điều này đã có test.

**ai-service — alembic `0037`:** thêm `ops_analyze_log.relation_hint`. Đây là cột ngoài danh sách ban đầu; cần vì response gửi backend luôn được dựng lại từ dòng đã lưu.

### 4.2 API (trường mới đều tuỳ chọn, client cũ không vỡ)

| Endpoint | Việc |
|---|---|
| `POST /transactions/{id}/refunds` | Ghi khoản hoàn/đảo từ khoản gốc. Mặc định: phần còn lại, ví gốc, ngày hiện tại. 201 khi tạo, 200 khi gửi lại cùng `clientRequestId` |
| `PUT /transactions/{id}/related` | Biến một khoản tiền vào (nháp hoặc đã xác nhận) thành hoàn/đảo trong một lượt lưu |
| `DELETE /transactions/{id}/related` | Gỡ liên kết, trở lại thu nhập thường |
| `POST /transactions/{id}/confirm` | Nhận thêm `relatedTransactionId?`, `relation?` |
| `POST /transactions` | Nhận `relation: "Refund"` cho hoàn mồ côi |
| `GET /transactions` | Thêm bộ lọc `relation` |
| `TransactionDto` | Thêm `relation`, `relatedTransactionId`, `isReversed`, `refundedCents`, `remainingRefundableCents`, `suggestedRelatedTransactions` |
| `POST /notifications/analyze` | Kết quả thêm `relationHint`, `suggestedRelatedTransactions` |
| Báo cáo tháng / báo cáo nhóm | Thêm `refundedCents`, `crossPeriodRefundCents` (riêng báo cáo nhóm chỉ thêm `refundedCents`) |
| Push | Action mới `choose_refund_link` (kèm `relationHint`, `suggestionCount`) |

Mã lỗi mới: 409 `HAS_REFUNDS`; 422 `REFUND_EXCEEDS_ORIGINAL`, `REFUND_ORIGINAL_NOT_ELIGIBLE`, `REVERSAL_AMOUNT_MISMATCH`, `REFUND_CATEGORY_LOCKED`, `REFUND_NOT_LINKABLE`, `REFUND_FOLLOWS_ORIGINAL`.

### 4.3 Lớp ứng dụng (backend)

- `Transactions/Refunds/RefundPolicy`: quy tắc thuần BR-RF-01/02/08, D4, D12, điều kiện hoàn mồ côi.
- `RefundLedger`: một chỗ duy nhất để gắn/gỡ quan hệ và tính lại ngân sách cá nhân + nhóm. Được dùng bởi tạo hoàn, liên kết, xác nhận, gỡ, sửa và xoá.
- `RefundFollowUps`: R1 và R2.
- `RefundSuggestions`: tìm và xếp hạng ứng viên (cùng người nhận > cùng ví/nhà cung cấp > đúng số tiền > gần nhất), tối đa 3.
- `RefundAfterSave`: việc sau khi lưu (bỏ cache, push, chốt lại tóm tắt, báo dự báo).
- Handler sửa giao dịch tách thành pha lưu (chạy dưới khoá khoản gốc khi sửa khoản hoàn) và pha sau khi commit.
- `GroupTransactionService`: khoản hoàn đi theo khoản gốc khi chia sẻ, bỏ chia sẻ, phân loại lại hoặc gỡ khỏi nhóm.
- `IDailySummaryRefresher` + `DailySummaryJob.SummariseUserAsync`: chốt lại một ngày của một user.
- Cấu hình hệ thống thêm nhóm `refunds` (`matchWindowDays` 60, `reversalMatchWindowDays` 3, `maxSuggestions` 3), có kiểm khoảng hợp lệ.

### 4.4 AI Service

- Tài liệu `notification_vocab` (nằm trong DB, có bản seed) thêm ba nhóm từ khoá: `refund_markers`, `reversal_markers`, `cashback_markers`. Tài liệu đã lưu từ trước Phase 81 được bổ sung các nhóm này từ bản mặc định. Validator từ chối một cụm nằm ở hai nhóm.
- Extractor trả `extraction.relation_hint` cho khoản tiền vào, theo thứ tự ưu tiên cashback → đảo → hoàn.
- Từ điển chat: `cashback` chuyển từ `refund` sang `other_income`.

### 4.5 Danh sách điểm chạm đã xử lý

**Ghi tiền:**
- handler confirm, tạo tay, sửa, xoá;
- `BudgetPeriodService`, `GroupLedger`, `GroupTransactionService`;
- backfill khi tạo ngân sách;
- `GroupFinanceRepository.SumSharedSpendAsync`, `UserHardDeleter`.

**Báo cáo và Agent:**
- `ReportRepository` (`GetDailySpendAsync`, `GetCategorySpendAsync`, `GetConfirmedDebitsAsync`, `GetCrossPeriodRefundsAsync`);
- `DailySummaryJob`, báo cáo tháng, timeline, `FinanceSnapshotRepository` (`/me/spendable`);
- báo cáo nhóm, `MarketAlertService`, `SpentHereVerifier`;
- `RepositoryFinancialDataSource` của Agent.

**Hưởng gián tiếp (có test):** `InsightGeneratorJob`, `JarService`, chat tóm tắt, `StatisticalSpendingForecaster`.

**Dự báo:** `SpendingBaseline`, `IncomeEstimator`, `AnomalyDetector`/`AnomalyService`, `CategoryForecastCalculator`, `RecurringDetector`/`Matcher`/`Tracker`, `BudgetRiskCalculator`, `ForecastService`. Riêng `ForecastAccuracy` chuyển sang đọc dữ liệu thô.

**Thông báo:** `AnalyzeNotificationCommandHandler`, `RetryFailedNotificationJob`, `TransactionReviewPush`, hợp đồng AI.

**Không đổi:** `CreateTransferCommandHandler` (Transfer giữ nguyên hành vi), `SupportAccessService`, `FindPossibleDuplicateAsync`.

## 5. Kiểm thử

### 5.1 Số liệu

| | Mốc (trước Phase 81) | Sau Phase 81 |
|---|---|---|
| Backend | 2597 qua, 1 đỏ (FR-FIN-07), tổng 2598 | **2717 qua, 1 đỏ (FR-FIN-07), tổng 2718**; build 0 cảnh báo |
| ai-service (không Postgres) | 1984 qua, 449 bỏ qua | **2003 qua, 453 bỏ qua** |
| ai-service tích hợp (Postgres pgvector) | (bị bỏ qua) | **453/453 qua** |
| Alembic `0037` | — | lên / xuống / lên lại sạch trên DB trống |

Thêm **120 test backend** và **19 test ai-service**. Không có test nào đỏ thêm so với mốc.

### 5.2 Các bộ test mới

| Bộ test | Nội dung |
|---|---|
| `TransactionRelationSchemaTests` | Mỗi CHECK có một ca bị từ chối và một ca hợp lệ; xoá cứng user có cặp gốc–hoàn |
| `RefundPolicyTests`, `TransactionBudgetDeltaRefundTests` | Mọi nhánh BR-RF-01/02/08, D4, D12 |
| `TransactionRefundQueriesTests` | Tổng đã hoàn; khoá (luồng thứ hai phải chờ); lọc và xếp hạng ứng viên; chi ròng; không bao giờ lấy dữ liệu của user khác |
| `BudgetRecomputeTests`, `BudgetAlertJobTests` | Hoàn đủ → 0, không cảnh báo, giữ cờ; chuỗi chéo tháng không lệch; đảo ở kỳ khoản gốc; R4 phía nhóm |
| `TransactionRefundsControllerTests` | 201/200/401/404/422; liên kết khoản gốc quá 60 ngày; **test đồng thời**: 10 khoản hoàn 200k vào hai ví cho khoản gốc 900k → đúng 4 khoản thành công, số dư khớp |
| `TransactionRelatedLinkControllerTests` | Liên kết/gỡ; confirm có và không có khoản gốc; 404 ở cả hai đầu; hoàn mồ côi |
| `RefundEditDeleteShareTests` | 409 `HAS_REFUNDS`; danh mục và nhóm đi theo khoản gốc; khoá danh mục; xoá hoàn/đảo; D18; R4 qua HTTP |
| `RefundReportsTests` | Báo cáo tháng, cơ cấu, timeline, chéo tháng, chốt lại daily summary, spendable, hồ sơ thị trường, báo cáo nhóm |
| `RefundNettingTests`, `RefundForecastTests` | Dữ liệu thói quen và dữ liệu thô; R1 (đảo / hoàn); R2 (đóng / không đóng; client không gửi được) |
| `RefundNotificationTests`, `RefundSuggestionWiringTests` | Gợi ý đúng thứ tự; cửa sổ 3 ngày cho đảo; push `choose_refund_link`; AI cũ không có hint; bộ lọc `relation`; R3; cấu hình; chat |
| `RefundAgentDataTests` | Agent thấy chi ròng, thu không gồm hoàn, không có tháng chi âm |
| `RefundScenarioTests` | Kịch bản đầu-cuối bắt buộc (mục 5.3) |
| ai-service `test_notification_vocab`, `test_chat_parser`, `test_analyze_api` | Hint refund/reversal/cashback; tài liệu cũ được nâng cấp; validator; hợp đồng response |

### 5.3 Kịch bản đầu-cuối bắt buộc

Bối cảnh: ngân sách Mua sắm 1.000.000đ; mua giày 900.000đ → đúng **một** push cảnh báo T2. Sau đó thông báo "+900,000 HOAN TIEN DON HANG" → nháp có gợi ý đầu tiên đúng là đôi giày, push là `choose_refund_link` → người dùng xác nhận liên kết.

| Kỳ vọng | Hoàn đủ 900k | Hoàn một phần 300k | Đảo giao dịch |
|---|---|---|---|
| Số dư | Về như trước khi mua ✓ | −600k ✓ | Về như trước khi mua ✓ |
| `spent_cents` | 0 ✓ | 600.000, cờ T2 giữ nguyên ✓ | 0 ✓ |
| Tổng thu tháng | Không đổi ✓ | Không đổi ✓ | Không đổi ✓ |
| `refundedCents` | 900.000 ✓ | 300.000 ✓ | 0 (đảo không phải hoàn) ✓ |
| Push cảnh báo mới | Không ✓ | Không ✓ | Không ✓ |
| Baseline dự báo | Không còn đôi giày ✓ | Thấy 600.000 ✓ | Không còn đôi giày ✓ |
| Bất thường | Tự đóng `refunded` ✓ | Vẫn mở ✓ | Tự đóng `reversed` ✓ |
| Lịch sử | — | — | Cả cặp vẫn hiện trong `GET /transactions` ✓ |

## 6. Phát hiện trong quá trình làm

| Phát hiện | Xử lý |
|---|---|
| Tiền đề "hoàn tiền nằm trong `credit_verbs` là sai" không đúng hẳn: từ đó chỉ quyết chiều tiền | Giữ nguyên, thêm nhóm từ khoá riêng (D8) |
| Tiền đề "AI trả `income_type = refund`" chỉ đúng với chat, đường thông báo không có | Thêm `relation_hint` cho đường thông báo (D9) |
| Đưa `cashback` vào `other_income` trái với ghi chú cũ của từ điển | Chọn phương án (a), sửa ghi chú (D10) |
| Chặn `spent_cents` ở 0 làm số lệch khi có hoàn chéo tháng | Tính lại kỳ (D2) |
| `daily_summaries` không được chốt lại khi sửa/xoá giao dịch của ngày đã chốt (lỗi có sẵn) | Chỉ sửa cho đường hoàn/đảo; ghi lỗ hổng chung vào §13.1 (D7) |
| Chuỗi tháng của Agent có thể ra chi âm ở tháng chỉ có hoàn | Chặn ở 0 ngay tại nguồn |
| Timeline từ trước vẫn cộng cả giao dịch nháp vào tổng ngày | Giữ nguyên, ngoài phạm vi |
| Một biểu thức lọc khoản chi (thị trường) mắc lỗi thứ tự toán tử khi đang viết | Sửa trước khi commit |
| Lệnh `black` chạy nhầm trên cả thư mục ai-service, định dạng lại 131 tệp không liên quan | Trả các tệp đó về HEAD trước khi commit, chỉ giữ thay đổi của task |

## 7. Việc còn lại và rủi ro

- [ ] Lỗ hổng có sẵn (D7): sửa/xoá giao dịch **thường** ở một ngày đã chốt không chốt lại `daily_summaries`. Đề xuất: gọi `IDailySummaryRefresher` từ handler sửa/xoá thường (việc nhỏ).
- [ ] Câu chat "nhận cashback 20k" ra `add_expense`, do bộ phân loại ý định, lỗi có sẵn. BR-RF-12 giữ nguyên hành vi nên không sửa ở đây.
- [ ] Báo nhóm Flutter: danh sách API, DTO và action push mới nằm ở `.context/TASKS.md` (Phase 81) và `docs/transactions-refund-api.md`.
- **Rủi ro nhỏ (đã ghi trong tài liệu):**
  - Khi hoàn chéo tháng, tổng các dòng danh mục có thể lớn hơn tổng chi tháng (D3).
  - Dự báo và báo cáo có thể lệch nhẹ khi hoàn một phần chéo tháng (D14).
  - Một cảnh báo bất thường đã nằm sẵn trong hàng đợi trước khi khoản chi được hoàn vẫn có thể được gửi.
- **Downgrade migration:** sẽ lỗi nếu đã có dòng `transaction_anomalies` mang `refunded`/`reversed`. Đây là chuyện bình thường của việc thu hẹp CHECK.

## 8. Danh sách commit

| # | Commit | Task |
|---|---|---|
| 1 | `181b0fc` docs(plan): refunds and reversals (Phase 81) | 0 — kế hoạch, mốc test |
| 2 | `37b1de0` feat(transactions): relation, related transaction and reversed flag columns | 1 — domain + migration |
| 3 | `bea6ca5` feat(transactions): refund policy and signed budget spend | 2 — quy tắc thuần |
| 4 | `4713733` feat(ai): relation_hint for refund and reversal notifications; cashback stays income | 10 — ai-service |
| 5 | `562e75e` feat(transactions): refund sums, candidate search and original row lock | 3 — repository, khoá |
| 6 | `039acbf` feat(budgets): net refunds into personal and group spent without drift | 4 — ngân sách |
| 7 | `741ce87` feat(transactions): create refund or reversal from the original | 5 — `POST /refunds` |
| 8 | `34f2e22` feat(transactions): link, unlink and confirm with a related transaction | 6 — liên kết |
| 9 | `8204083` feat(transactions): guard edits and deletes of refunded originals | 7 — sửa/xoá/nhóm |
| 10 | `7c8c948` feat(reports): net refunds in reports, summaries, jars, spendable and group reports | 8 — báo cáo |
| 11 | `32d7a7b` feat(forecast): exclude refunded and reversed spending from habits and income | 9 — dự báo, R1, R2 |
| 12 | `629aba7` feat(notifications): suggest refund originals and ask the user to link | 11 — thông báo, chat, R3 |
| 13 | `e642775` test(agent): tools see net spending and income without refunds | 12 — Agent |
| 14 | `e013a89` test(transactions): end-to-end refund, partial refund and reversal scenarios | 13 — đầu-cuối |
| 15 | `faf0872` docs: refunds and reversals (Phase 81) | 15 — tài liệu |

Task 14 (chạy toàn bộ test) không có commit riêng; số liệu được ghi vào commit tài liệu và `NFR-MNT-04`.

Quy mô thay đổi: 119 tệp, khoảng 11.900 dòng thêm và 276 dòng xoá. Trong đó khoảng 2.650 dòng là test và một phần lớn là tệp Designer do EF sinh ra cho migration.

**Tài liệu đã cập nhật:**
- `docs/REQUIREMENTS.md`: FR-TXN-18 bỏ nhãn [Mới]; thêm FR-TXN-27…30; cập nhật FR-AI-04 và §13.1;
- `docs/ERD.md`;
- `.context/ARCHITECTURE.md`;
- `.context/TASKS.md`;
- `CLAUDE.md`;
- `docs/transactions-refund-api.md` (mới).
