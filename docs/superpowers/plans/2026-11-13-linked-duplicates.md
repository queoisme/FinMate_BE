# Phase 82 — Bản trùng có liên kết (linked duplicates)

## Context

Hiện có hai lớp chống trùng tách rời nhau và cả hai đều sai theo một kiểu:
- ai-service `pipeline_repo.find_duplicate` chỉ so **thông báo với thông báo**: cùng user hash, cùng số tiền, ±5 phút. Không xét chiều, provider hay số dư.
- `AnalyzeNotificationCommandHandler.cs:159-162`: khi `IsPotentialDuplicate` thì không tạo nháp, không push và không trả gì cho client. Log vẫn ghi `Processed`. `RetryFailedNotificationJob.cs:76-104` làm y như vậy.
- Chat (`ChatCommandHandler.cs:178-182`, ±10 phút) và hoá đơn (`ScanReceiptCommandHandler.cs:424`, ±3 giờ) chỉ trả `PossibleDuplicateTransactionId` qua `FindPossibleDuplicateAsync` (`TransactionRepository.cs:27-48`: chỉ Debit, không lọc trạng thái, nguồn hay `Relation`). Không có thao tác gộp.

Hậu quả: (A) chat 50k rồi thông báo −50k thì khoản chi bị tính hai lần; (B) hai lần mua 35k cách nhau 3 phút thì lần thứ hai mất âm thầm. Đây là FR-TXN-17 [Đổi] và FR-AI-04 [Một phần].

**Kiến trúc:** backend quyết định trùng qua `DuplicateMatcher`, một hàm thuần trong Application. Cờ AI chỉ là tín hiệu, không bao giờ làm mất thông báo. Giao dịch nghi trùng vẫn được tạo, ở dạng nháp `Suspected` trỏ tới ứng viên hạng 1. User chọn Gộp hoặc Không trùng. Gộp xoá mềm bản bị gộp (đi đúng đường hoàn tác của xoá) rồi tính lại survivor bằng chính bộ máy của Phase 81 (`RefundLedger.RecomputeAsync`, `RefundAfterSave.RunAsync`).

> **Trạng thái:** chủ dự án đã duyệt kế hoạch và D0–D19 theo đề xuất (2026-11-13). Phase 81 đã gộp vào `main` qua PR #30 (`22f0b84`); nhánh `feat/linked-duplicates` tạo từ đó. Mốc test lấy lại từ Phase 81 vì cây mã giống hệt `6cf0097`: backend 2717/2718 (chỉ FR-FIN-07 đỏ), ai-service 2003 qua / 453 bỏ qua khi không có Postgres.

---

## Mọi bảng/cột tham chiếu `transactions.id` (đã kiểm trên code)

**Có FK**
| # | Bảng.cột | Null | ON DELETE | Gộp xử lý thế nào |
|---|---|---|---|---|
| 1 | `transactions.related_transaction_id` (tự tham chiếu, Phase 81) | có | NO ACTION | Bản bị gộp **là khoản gốc** có hoàn đã xác nhận → 422 (BR-DUP-12). Nháp hoàn trỏ tới nó → chuyển sang survivor (D7). Bản bị gộp **là khoản hoàn** → không phải ứng viên (BR-DUP-03) |
| 2 | `transaction_anomalies.transaction_id` (UNIQUE) | không | CASCADE | Xoá dòng của bản bị gộp (xoá mềm không kích hoạt cascade, query filter chỉ ẩn đi). Survivor quét lại (D10) |
| 3 | `receipt_scan_links.transaction_id` (PK) | không | CASCADE | Survivor chưa có link thì chuyển link sang survivor; survivor đã có thì giữ nguyên link của bản bị gộp (D8) |
| 4 | `qr_transfer_intents.matched_transaction_id` | có | SET NULL | Xem D6 (chuyển sang survivor thay vì từ chối) |

**Không có FK**
| Bảng.cột | Nội dung | Xử lý |
|---|---|---|
| `group_activities.subject_id` | id giao dịch cho `TransactionShared/Unshared/Reclassified` | Bản bị gộp đã chia sẻ thì bị từ chối, nên không phát sinh. Lịch sử cũ giữ nguyên |
| `forecast_alerts.dedup_key` = `anomaly:{txId}`, `forecast_alerts.data.transactionId` | Cảnh báo bất thường đã gửi | Giữ nguyên (lịch sử). Survivor có dedup key riêng |
| `audit_logs.metadata.transactionId` | Nháp do Support tạo | Giữ nguyên (nhật ký) |
| ai-service `ml_user_feedback.backend_transaction_id_hash` | SHA-256 của id | Giữ nguyên. Feedback danh mục khi gộp gửi theo id survivor |

**Chiều ngược lại (cột trên `transactions` cần chuyển):** `notification_log_id` (→ `ai_results` nối qua đây), `support_ticket_id`, `client_request_id`.

**Cột mới của phase này:** `duplicate_of_transaction_id` và `merged_into_transaction_id`, đều tự tham chiếu, NO ACTION, giống `related_transaction_id`.

**Kiểm global query filter:** `TransactionConfiguration.cs:135` có `HasQueryFilter(t => t.DeletedAt == null)`. Không có chỗ nào gọi `IgnoreQueryFilters()` trên `Transactions`, và repo không dùng Dapper. Có **một chỗ không lọc**: `ReceiptScanRepository.cs:38-45`, một câu `INSERT … JOIN transactions` viết bằng SQL thô, không có `deleted_at IS NULL`, nên vẫn link được hoá đơn vào giao dịch đã xoá hoặc đã bị gộp. Sửa ở Task 1. `FOR UPDATE` ở `TransactionRepository.cs:218` có lọc.

**Xoá cứng:** `UserHardDeleter` xoá `users` và để cascade lo phần `transactions`. FK NO ACTION được kiểm ở cuối câu lệnh, nên một cặp tự tham chiếu bị xoá cùng lúc vẫn qua; Phase 81 đã có test `DeletingUser_RemovesLinkedPairWithoutForeignKeyError`. Hai FK mới cũng vậy; thêm test.

---

## Quyết định đã chốt (duyệt theo đề xuất)

- **D0 — Điều kiện bắt đầu.** Đã gộp Phase 81 vào `main` (PR #30) trước khi tạo nhánh.
- **D1 — `Suspected` trên giao dịch đã `Confirmed`.** Chat, hoá đơn và `POST /transactions` tạo bản `Confirmed`. Theo yêu cầu "sau khi lưu mà có ứng viên → mang `Suspected`", BR-DUP-06 ("vẫn được tạo ở `Draft`") chỉ áp cho **thông báo**. **Đề xuất:** cho phép `Suspected` trên bản `Confirmed`. Bản đó vẫn tính vào số dư và ngân sách như thường cho tới khi gộp. `not-duplicate` dùng được cho cả hai trạng thái.
- **D2 — Chiều liên kết.** Bản **mới hơn** (bản đang được tạo) trỏ tới bản cũ. Chat trước, thông báo sau → nháp thông báo trỏ tới khoản chat. Ngược lại → khoản chat trỏ tới nháp thông báo. Gộp thì survivor xác định theo BR-DUP-09, không phụ thuộc chiều liên kết.
- **D3 — Chat: giữ điều kiện ±10 phút.** "Nhập tay ↔ nhập tay không so" nếu áp thẳng sẽ bỏ điều kiện tự lưu của FR-CHAT-02 (không có khoản Debit cùng số tiền trong ±10 phút). **Đề xuất:** giữ ±10 phút chỉ để **chặn tự lưu** (`action = confirm`, giữ `possibleDuplicateTransactionId` như cũ, không gắn `Suspected`). Ứng viên từ `DuplicateMatcher` (với thông báo, 48 giờ) cũng chuyển sang `confirm` và thêm `suggestedDuplicates`.
- **D4 — Hoá đơn ↔ nhập tay.** FR-SCAN-07 đang cảnh báo trùng giữa hoá đơn và mọi khoản Debit trong ±3 giờ. Bảng cửa sổ chỉ có `Receipt ↔ Notification`. **Đề xuất:** thêm cặp `Receipt ↔ Manual/Voice/Support` chỉ để **cảnh báo** (`possibleDuplicateTransactionId` + `suggestedDuplicates`, không gắn `Suspected`), dùng `receiptWindowHours`, để không lùi tính năng. Nếu anh muốn bỏ hẳn thì FR-SCAN-07 đổi theo.
- **D5 — BR-DUP-05 (so số dư) cần thêm cửa sổ và cách nhận diện provider.** Số dư sau giao dịch có thể lặp lại thật, ví dụ chi 50k, nhận 50k, rồi chi 50k. **Đề xuất:**
  - chỉ so trong `notificationWindowMinutes`;
  - "cùng provider" = cùng `notification_logs.package_name`;
  - số dư chỉ tin được ở các dòng có `source = Notification` hoặc có `notification` trong `merged_sources`, vì cột `balance_after_cents` ở giao dịch nhập tay là số dư do app tự cộng, khác nghĩa.
  - Khi trùng thì `analyze` trả `status = "Duplicate"`, `draftId = null`, kèm trường mới tuỳ chọn `duplicateOfTransactionId`.
- **D6 — Bản bị gộp đã khớp QR.** `AnalyzeNotification` khớp ý định QR **ngay lúc tạo nháp**. Trường hợp hay gặp: user quét QR, gõ chat, rồi thông báo về. Survivor là khoản chat (đã `Confirmed`), bản bị gộp là nháp thông báo đã khớp QR. Áp BR-DUP-12 nguyên văn thì kịch bản này luôn 422. **Đề xuất:** nếu survivor chưa khớp QR nào thì chuyển `matched_transaction_id` sang survivor, đồng thời áp danh mục của ý định nếu user chưa đặt. Chỉ trả 422 khi **cả hai** đã khớp QR.
- **D7 — Nháp hoàn (Phase 81) trỏ tới bản bị gộp.** **Đề xuất:** chuyển `related_transaction_id` của các nháp đó sang survivor. Phase 81 D18 (nháp mất liên kết khi khoản gốc bị xoá) chỉ áp cho xoá thật.
- **D8 — Hai bản cùng có tham chiếu một-một.**
  - `notification_log_id`: survivor giữ cái của mình; nếu survivor không có thì nhận cái của bản bị gộp.
  - `receipt_scan_links`: tương tự.
  - Hai thông báo gộp với nhau: log của bản bị gộp vẫn nằm trên dòng đã xoá mềm. `merged_sources` ghi lại cả hai.
  - `source` của survivor **không đổi** (vẫn là kênh đã tạo ra nó).
- **D9 — Liên kết trỏ tới bản bị gộp.** Các bản khác đang `Suspected/Dismissed` trỏ tới bản bị gộp → trỏ lại sang survivor. Nếu như vậy thành tự trỏ vào mình thì xoá cả liên kết lẫn trạng thái. BR-DUP-08 (ứng viên bị xoá) chỉ xoá `Suspected`. `Dismissed` giữ nguyên vì không hại gì, và CHECK cho phép.
- **D10 — Bất thường.**
  - Xoá các dòng `transaction_anomalies` của bản bị gộp.
  - Survivor đổi ví hoặc ngày: xoá anomaly **đang mở** của nó để lượt quét sau xét lại theo FR-FCST-14. Anomaly đã được user trả lời thì giữ.
  - `AnomalyDetector` bỏ qua loại `Duplicate` cho cặp đang có liên kết trùng, cả `Dismissed` (BR-DUP-13) lẫn `Suspected`, vì luồng review đã hỏi user rồi, tránh hai tín hiệu cho cùng một việc.
- **D11 — Đồng thời với confirm, sửa, xoá.** `transactions` không có `xmin`. Nếu gộp chạy đúng lúc user xác nhận bản bị gộp, lệnh UPDATE của confirm không đụng tới `deleted_at`, nên dòng có thể kết thúc ở trạng thái **vừa bị xoá vừa đã xác nhận, ví đã bị trừ**. **Đề xuất:** thêm `RunWithTransactionsLockedAsync(userId, ids[])` (FOR UPDATE theo id tăng dần, tổng quát hoá `RunWithOriginalLockedAsync`) và cho `Confirm`, `Delete`, `Update`, `not-duplicate` khoá dòng của mình. Thứ tự khoá giữ nguyên: dòng giao dịch → ví → kỳ ngân sách.
- **D12 — Gộp thủ công ngoài cặp đã liên kết.** `merge` nhận mọi cặp thoả điều kiện của BR-DUP-12: cùng user, cùng chiều, cùng số tiền, cả hai chưa xoá hoặc gộp, không phải `Transfer`, không có `Relation`. **Không** đòi trong cửa sổ thời gian và **không** đòi đang `Suspected`, vì ứng viên hạng 2–3 không được lưu. Cặp `Dismissed` vẫn gộp được (user đổi ý).
- **D13 — `clientRequestId` của `merge`.** Idempotency theo cặp (BR-DUP-14) đã đủ, và `client_request_id` của survivor đã có giá trị riêng. **Đề xuất:** nhận trường này để không vỡ hợp đồng nhưng không lưu. Gửi lại cùng cặp → 200 với survivor, kể cả khi bản kia đã bị xoá mềm (đọc bằng `IgnoreQueryFilters` có lọc `userId`). Một trong hai đã bị gộp vào bản **thứ ba** → 422 `MERGE_NOT_ALLOWED`.
- **D14 — Tie-break và tín hiệu AI.**
  - Hai nháp đều từ `Notification` → giữ bản tạo trước.
  - Cờ `is_potential_duplicate` của AI dùng làm tie-break thứ ba: sau "cùng ví" và "cùng merchant", trước "gần thời gian nhất". Ứng viên được nâng là giao dịch có `notification_log_id` = `duplicate_request_id`.
- **D15 — Thứ tự action của push.** `review_duplicate` > `choose_refund_link` > `confirm_one_tap` > `choose_category`. `RetryFailedNotificationJob` vẫn không push như hiện tại, nhưng có tạo nháp `Suspected`.
- **D16 — Danh mục khi gộp.** `categoryId?` trong body thắng mọi nguồn. Danh mục cuối khác danh mục AI đoán cho phần thông báo → gửi `category_correction` giống luồng confirm. Survivor đổi ngày hoặc ví trong khi có khoản hoàn hay đã bị đảo: vẫn cho gộp, vì `RefundLedger.RecomputeAsync` tính lại cả kỳ cũ lẫn kỳ mới.
- **D17 — Thống kê admin.**
  - `Suspected`, `Dismissed`: đếm theo trạng thái hiện tại, cặp = (nguồn bản trỏ, nguồn bản được trỏ).
  - `Merged`: đếm bản bị gộp, cặp = (nguồn bản bị gộp, nguồn survivor).
  - Lọc thời gian theo `created_at` của bản trỏ (với `Merged` là theo `deleted_at`).
  - Không thêm cột thời điểm.
- **D18 — Lỗ hổng phát hiện thêm (sửa luôn hay ghi lại).**
  - (a) `ReceiptScanRepository` link được giao dịch đã xoá → sửa trong phase này.
  - (b) Nhật ký audit của system config không ghi tên nhóm `forecast`/`refunds` khi đổi → thêm `duplicates` và sửa luôn hai nhóm kia (vài dòng).
  - (c) Giọng nói tự lưu với `source = Manual` chứ không phải `Voice` → **chỉ ghi vào §13.1**, cửa sổ thời gian như nhau nên không ảnh hưởng phase này.
- **D19 — `RecurringTracker`.** Khi survivor đổi ngày, `last_seen_at` của khoản định kỳ có thể đang trỏ tới thời điểm của khoản chat. **Đề xuất:** để `RecurringDetectionJob` chạy đêm tự sửa, không đẩy hay lùi lịch trong lượt gộp. Ghi vào tài liệu.

---

## Thiết kế

**Domain và dữ liệu (migration `AddLinkedDuplicates`)**
- `Transaction`: thêm `DuplicateOfTransactionId?`, `DuplicateStatus?` (enum mới `DuplicateStatus { Suspected, Dismissed }`), `MergedIntoTransactionId?`, `MergedSources` (`string[]?`), kèm điều hướng.
- CHECK:
  - `duplicate_status IN ('suspected','dismissed')`;
  - `(duplicate_status IS NULL) = (duplicate_of_transaction_id IS NULL)`;
  - `duplicate_of <> id`, `merged_into <> id`;
  - `merged_into IS NULL OR deleted_at IS NOT NULL`;
  - `merged_sources <@ ARRAY['notification','manual','voice','receipt','support']`.
- Partial index trên `duplicate_of_transaction_id` và `merged_into_transaction_id`.
- `NotificationLogStatus.Duplicate`, sửa CHECK `chk_notification_logs_status`.
- ai-service: `balance_after_cents` đã có từ đầu đến cuối (regex group `balance` → `ExtractionResult` → `transactions.balance_after_cents`), nên **không cần đổi gì**. `duplicate_detector` giữ nguyên.

**Cấu hình:** `DuplicateSettings` trong `SystemConfigDocument` (nhóm `duplicates`) gồm `NotificationWindowMinutes = 10` (1–60), `ManualWindowHours = 48` (1–168), `ReceiptWindowHours = 12` (1–72). Thêm validator và nhật ký audit (D18b). Đọc qua `ISystemConfigProvider`.

**`Application/Transactions/Duplicates/`**
- `DuplicateMatcher`, hàm thuần: `Match(subject, candidates, dismissedPairs, settings, aiHintLogId?) → DuplicateMatch { SameEvent?, Ranked[≤3] }`. Áp BR-DUP-03/04/05 và D4/D5/D14. Có `PairKind` (NotifNotif, ManualNotif, ReceiptNotif, ReceiptManual, None).
- Repo `ListDuplicateCandidatesAsync(userId, type, amount, from, to, excludeId)`: lọc bằng SQL theo cùng user, chiều, số tiền; chưa xoá; `relation IS NULL`; không phải transfer; trong cửa sổ lớn nhất. Trả kèm `package_name` và cặp `Dismissed`. `CounterpartyKey.From` tính trong bộ nhớ, như Phase 81.
- `IDuplicateService`:
  - `FindForNewAsync(tx)`: dùng ở analyze, retry, chat, receipt, create manual.
  - `SuggestAsync(drafts)`: chạy theo lô cho `GET /transactions`.
  - `ReleaseLinksToAsync(deletedId)`: BR-DUP-08 / D9, gọi trong lượt lưu của xoá.
- `MergeTransactionsCommandHandler` + `RetryingMergeTransactionsCommandHandler`:
  1. Khoá hai dòng theo id tăng dần (D11).
  2. Kiểm tra idempotent (D13).
  3. `MergePolicy`: chọn survivor theo BR-DUP-09, xét điều kiện từ chối theo BR-DUP-12 và D6.
  4. Chụp `RefundScopeSnapshot` của trạng thái trước.
  5. Hoàn tác bản bị gộp nếu nó `Confirmed`: đi đường ví và nhóm của `DeleteTransactionCommandHandler`, phần ngân sách để cho bước tính lại lo.
  6. Áp trường lên survivor theo BR-DUP-10 và chuyển số dư (ví cũ +, ví mới −; nếu survivor là nháp thì xác nhận).
  7. Chuyển các tham chiếu (bảng ở trên, D6–D9) và xoá anomaly (D10).
  8. `RefundLedger.RecomputeAsync(before, pending)` cho ngân sách cá nhân và nhóm. Bước này chỉ cảnh báo khi số sau lớn hơn số trước.
  9. Lưu **một lần**, rồi gọi `RefundAfterSave.RunAsync`: xoá cache, gửi cảnh báo, refresh `daily_summaries` các ngày bị ảnh hưởng, trigger dự báo (anomaly quét lại survivor).
- `MarkNotDuplicateCommandHandler`: `Dismissed`. Gọi lại khi đã `Dismissed` → 200. Không có liên kết → 422 `NOT_SUSPECTED_DUPLICATE`.
- Mã lỗi mới trong `TransactionErrorCodes`: `MERGE_NOT_ALLOWED` (kèm lý do trong message), `NOT_SUSPECTED_DUPLICATE`.

**Nối vào các luồng**
- `AnalyzeNotificationCommandHandler` và `RetryFailedNotificationJob`: **bỏ** điều kiện `!IsPotentialDuplicate`. `SameEvent` → log `Duplicate`, không tạo nháp. Có ứng viên → nháp `Suspected` và push `review_duplicate` (`TransactionReviewPush`, payload thêm `duplicateOfTransactionId`, `suggestionCount`, không log).
- `ChatCommandHandler` (D3), `ScanReceiptCommandHandler` (D4, kể cả đường order-list và đường OCR cũ), `CreateManualTransactionCommandHandler`: sau khi lưu mà có ứng viên → gắn `Suspected`.
- `DeleteTransactionCommandHandler`: gọi `ReleaseLinksToAsync`.
- `AnomalyDetector`: thêm D10.
- `GetAiStatsQueryHandler` / `AiStatsRepository`: thêm D17.
- API: `TransactionsController` thêm `POST {id}/merge`, `POST {id}/not-duplicate` và tham số lọc `duplicateStatus`. `TransactionDto` thêm `duplicateStatus`, `duplicateOf`, `suggestedDuplicates`, `mergedSources`. `ChatDraftDto`, `ScannedReceiptDto`, `ReceiptGroup` thêm `suggestedDuplicates`.

---

## Task (TDD; mỗi task một commit nhỏ, `git add` từng đường dẫn, không đụng `docs/diagrams/*.drawio`)

0. **Nhánh và mốc test.**
   - `git switch -c feat/linked-duplicates main` (sau PR #30).
   - Chép kế hoạch này vào `docs/superpowers/plans/2026-11-13-linked-duplicates.md`.
   - Mốc test: dùng lại số của Phase 81 vì cây mã giống hệt `6cf0097` (xem Trạng thái ở trên).
   - Thêm quyết định "backend quyết trùng, AI chỉ là tín hiệu" vào `.context/ARCHITECTURE.md` (duyệt cùng kế hoạch).
   - Commit `docs(plan): linked duplicates (Phase 82)`.
1. **Domain + migration + sửa `ReceiptScanRepository`.**
   - Test `Integration/Persistence/DuplicateLinkSchemaTests.cs`: mỗi CHECK có một ca bị từ chối và một ca hợp lệ; xoá cứng user có chuỗi `duplicate_of`/`merged_into` không lỗi FK; link hoá đơn vào giao dịch đã xoá → không chèn.
   - Đọc lại SQL migration trước khi commit.
2. **`DuplicateSettings`** + validator + audit (D18b). Unit test các khoảng giá trị.
3. **`DuplicateMatcher` (unit, hàm thuần).** Mỗi BR-DUP-03/04/05 có test riêng:
   - khác chiều, khác số tiền, đã xoá, transfer, có `relation`, cặp `Dismissed` → không phải ứng viên;
   - manual↔manual → không so;
   - từng cửa sổ ở biên trong và biên ngoài;
   - xếp hạng: ví > merchant > AI > thời gian, tối đa 3;
   - khác ví vẫn là ứng viên;
   - số dư bằng → `SameEvent`; khác → bị loại; thiếu → theo BR-DUP-03;
   - khác `package_name` hoặc quá cửa sổ → không `SameEvent`;
   - số dư của dòng nhập tay bị bỏ qua.
4. **Repository**: `ListDuplicateCandidatesAsync` và `RunWithTransactionsLockedAsync` (D11). Test tích hợp: id của người khác, dòng đã xoá mềm, thứ tự khoá.
5. **Thông báo**: analyze + retry + push. Test cho kịch bản (b), (c), (d) phía tạo nháp; cờ AI = true mà không có ứng viên ở backend → vẫn tạo nháp thường; action của push theo D15.
6. **Chat, hoá đơn, tạo tay**: D1, D3, D4. Giữ `possibleDuplicateTransactionId` cũ. Test nhánh `saved` → `confirm` khi có ứng viên 48 giờ.
7. **`not-duplicate`** + lọc `duplicateStatus` + trường DTO (`duplicateOf`, `suggestedDuplicates` theo lô). Test controller: 200, gọi lại 200, 404 (người khác hoặc không tồn tại), 422.
8. **`MergePolicy` (unit)**: chọn survivor theo BR-DUP-09 (một bản Confirmed / hai bản Confirmed / hai bản Draft / tie-break), lấy trường theo BR-DUP-10, mọi nhánh từ chối của BR-DUP-12, D6, D12.
9. **`merge` handler + controller.**
   - Test controller: 200; gọi lại 200 trả survivor cũ; 404; 422 cho từng lý do; gộp vào bản thứ ba → 422.
   - **Test đồng thời** `Integration/Concurrency/MergeConcurrencyTests.cs`:
     - hai request gộp cùng cặp chạy song song → một survivor, số dư đúng;
     - merge(A,B) chạy song song với merge(B,C);
     - merge chạy song song với confirm bản bị gộp → không có dòng nào vừa xoá vừa đã xác nhận, ví đúng.
10. **Tổng hợp sau gộp** (`Integration/Transactions/MergeAggregatesTests.cs`):
    - số dư (đổi ví);
    - `spent_cents` cá nhân và nhóm (survivor đã chia sẻ, đổi tháng);
    - không có cảnh báo khi số không tăng;
    - báo cáo tháng, timeline, cơ cấu danh mục;
    - `daily_summaries` của các ngày cũ và mới;
    - baseline dự báo không đếm hai lần;
    - bất thường (D10, BR-DUP-13);
    - nháp hoàn được chuyển sang survivor (D7);
    - QR được chuyển (D6);
    - receipt link được chuyển.
11. **Xoá + BR-DUP-08 + D9**; `AnomalyDetector` (D10). Unit + integration.
12. **Thống kê admin** (D17). Test: không có trường theo user.
13. **Kịch bản đầu-cuối** `Integration/Scenarios/DuplicateScenarioTests.cs` (ngân sách Ăn uống 1.000.000đ), gồm (a), (b), (c), (d) đúng như yêu cầu.
14. **Chạy toàn bộ** backend + ai-service (`black/ruff/isort` nếu có đụng Python). Không thêm test đỏ hay cảnh báo build so với mốc.
15. **Tài liệu:**
    - `docs/REQUIREMENTS.md`: FR-TXN-17 bỏ [Đổi], sơ đồ trạng thái; FR-AI-04; FR mới cho merge/not-duplicate; FR-CHAT-02, FR-SCAN-07 theo D3/D4; §13.1 thêm "chưa có unmerge", "ghép cặp chuyển ví VCB→MoMo thành Transfer: phase sau" và D18c.
    - `docs/ERD.md`.
    - `.context/TASKS.md`: Phase 82 + mục "Báo nhóm Flutter" (API, DTO, push `review_duplicate`, `status = Duplicate` của analyze).
    - `docs/transactions-duplicates-api.md`.
    - Một đoạn ngắn trong `CLAUDE.md`.

Thứ tự: 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12 → 13 → 14 → 15. Không push, không mở PR khi chưa được đồng ý.

## Verification

- Trong lúc code: chạy lớp test của task (`dotnet test --filter FullyQualifiedName~<Class>` trong container SDK 9 qua podman). Trước mỗi commit: unit + các test liên quan.
- Trước khi xin gộp: toàn bộ `dotnet test` và `pytest`, so với mốc ở Task 0.
- Bắt buộc phải pass: kịch bản (a)–(d) ở Task 13, ba test đồng thời ở Task 9, mỗi BR-DUP-01…14 có ít nhất một test (ghi bảng BR → test vào file kế hoạch trong repo).
- `dotnet ef migrations script` áp được lên DB thử của Testcontainers.

## Bảng BR → test (sau khi làm)

| BR / D | Test |
|---|---|
| BR-DUP-01 (contentHash giữ nguyên) | `AnalyzeNotificationCommandHandlerTests.HandleAsync_DuplicateWithinWindow_*` (có sẵn, vẫn qua) |
| BR-DUP-02 (cờ AI không bỏ thông báo) | `AnalyzeNotificationDuplicateTests.AiFlagAlone_NoLongerDropsTheNotification`, `RetryFailedNotificationDuplicateTests.AiFlagAlone_StillCreatesTheDraft` |
| BR-DUP-03 (ứng viên, cửa sổ theo cặp) | `DuplicateMatcherTests` (khác chiều/số tiền, đã xoá/gộp, hoàn/đảo, đã bác, nhập tay↔nhập tay, `Window_DependsOnTheSourcePair`), `TransactionDuplicateQueriesTests` |
| BR-DUP-04 (xếp hạng, tối đa 3) | `DuplicateMatcherTests.Ranking_*`, `OtherWallet_IsStillACandidate`, `EqualRank_PrefersTheEarlierCreated` |
| BR-DUP-05 (số dư sau) | `DuplicateMatcherTests.SameProvider*`, `MissingBalance_*`, `SameBalance_*`, `BalanceOfAHandEnteredRow_IsIgnored`; kịch bản (b), (c) |
| BR-DUP-06 (nháp nghi trùng, push, chat) | `AnalyzeNotificationDuplicateTests.Candidate_*`, `TransactionReviewPushBuilderTests.SuspectedDuplicate_*`, `InputDuplicateTests.Chat_*`, `Manual_WithACandidate_*`; kịch bản (a) |
| BR-DUP-07 (không trùng) | `TransactionDuplicateControllerTests.NotDuplicate_*`; kịch bản (d) |
| BR-DUP-08 (ứng viên bị xoá) | `TransactionDuplicateControllerTests.DeletingTheTransactionADraftPointsAt_*`, `DeletingTheOtherHalfOfADismissedPair_*` |
| BR-DUP-09/10 (survivor, trường) | `MergePolicyTests`, `TransactionMergeControllerTests.ChatAndBank_*`, `TwoDrafts_*` |
| BR-DUP-11 (xoá mềm, số dư, tính lại, daily summary) | `TransactionMergeControllerTests.ChatAndBank_*`, `MergeAggregatesTests` (báo cáo, đổi tháng, nhóm, dự báo) |
| BR-DUP-12 (từ chối 422, 404) | `MergePolicyTests.*Refused`, `TransactionMergeControllerTests.DifferentAmount_Is422`, `ConfirmedLoserWithARefund_Is422`, `AlreadyMergedIntoAThirdTransaction_Is422`, `SomeoneElsesOrUnknown_Is404` |
| BR-DUP-13 (bất thường) | `AnomalyDetectorTests.APairTheUserAlreadyReviewed*`, `MergeAggregatesTests.TheLosersAnomaly_IsRemoved`; kịch bản (d) 350k |
| BR-DUP-14 (khoá, idempotent) | `TransactionMergeControllerTests.MergingTheSamePairAgain_*`, `TwoMergesOfTheSamePairAtOnce_*`, `CrossedMerges_*`, `MergeRacingAConfirmOfTheLoser_*`, `TransactionDuplicateQueriesTests.Lock_*` |
| D6/D7/D8/D9 (tham chiếu đi theo survivor) | `MergeAggregatesTests.QrIntentReceiptLinkAndDraftRefunds_FollowTheSurvivor`, `TransactionMergeControllerTests.OtherSuspectedDraftsPointingAtTheLoser_*` |
| D17 (thống kê admin) | `AiStatsDuplicateTests` |
| Schema, xoá cứng | `DuplicateLinkSchemaTests` |
