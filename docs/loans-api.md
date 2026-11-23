# Sổ vay — API (`/api/v1/loans`, Phase 90)

Một sổ duy nhất cho mọi khoản đi vay / cho vay. Phase 90 hợp nhất sổ nợ của Phase 86 cũ (nhánh `feat/debt-book`, chưa từng phát hành) vào `loans` có sẵn của `main`. Kế hoạch, quyết định Q1–Q16, S1–S5, D1–D3: `docs/superpowers/plans/2026-11-23-loan-book-merge.md`.

**Tương thích app:** mọi đường dẫn, tên trường và mã lỗi có từ trước Phase 90 giữ nguyên (app đang đọc, xem mục S5 của kế hoạch). Mọi thứ mới là tuỳ chọn hoặc thêm ở cuối.

## Mô hình

- Một khoản có nhiều **dòng**: `principal` (giải ngân: tăng gốc) và `repayment` (trả: giảm còn lại).
- `principalCents` = Σ dòng giải ngân còn sống; `paidCents` = Σ dòng trả còn sống; `remainingCents` = gốc − đã trả.
- `status`: `open` | `settled` (trả hết gốc) | `written_off` (người dùng xoá nợ). Mọi thay đổi dòng trên khoản `written_off` mở lại nó.
- Dòng có thể gắn một giao dịch ví (`transactionId`, danh mục "Cho vay / Mượn", không phải chi tiêu hay thu nhập — Q16). Một giao dịch gắn tối đa một dòng còn sống.
- **Lãi tạm tính** (`accruedInterestCents`): lãi đơn theo ngày trên gốc còn lại, chỉ để tham khảo; khoản đã đóng trả 0. Dự báo, gợi ý, nhắc hạn, Agent dùng gốc còn lại, không cộng lãi (Q11).

## Endpoint

| Method | Path | Ghi chú |
|---|---|---|
| GET | `/loans` | Khoản còn sống, mới tạo trước. Lọc tuỳ chọn `status`, `direction` (giá trị sai → 400) |
| GET | `/loans/summary` | `owedToMeCents`, `iOweCents`, `overdueCount`, `overdueOwedToMeCents`, `overdueIOweCents` (khoản `open`, gốc còn lại) |
| GET | `/loans/{id}` | `{ loan, entries[] }`; dòng: `id, kind, amountCents, at, transactionId, interestTransactionId` |
| POST | `/loans` | Tạo. 201; gửi lại cùng `clientRequestId` → 200 với khoản đã tạo |
| PUT | `/loans/{id}` | Sửa; `principalCents` là **tổng gốc mới** (S2) |
| DELETE | `/loans/{id}` | Xoá mềm (Q4), 204. Giao dịch đã gắn ở lại với danh mục "Cho vay / Mượn" và gắn lại được |
| POST | `/loans/{id}/repayments` | Một lần trả / thu nợ (200). Hoặc `transactionId` để gắn giao dịch có sẵn |
| POST | `/loans/{id}/principal` | Cho mượn thêm / vay thêm, cùng dạng với `repayments` |
| DELETE | `/loans/{id}/entries/{entryId}` | Xoá một dòng (gỡ liên kết; giao dịch ở lại). 200 với khoản, hoặc 204 khi khoản thành rỗng và bị xoá theo |
| POST | `/loans/{id}/write-off` | Xoá nợ; không tạo giao dịch, không đổi số dư. Lần hai không đổi gì |
| POST | `/transactions/{id}/confirm` | + `loanId`: xác nhận nháp là một dòng của khoản đó (D4) |

Khoản của người khác là 404 ở mọi đường.

### Tạo khoản (`POST /loans`)

Trường cũ: `direction` (`borrow` | `lend`), `title`, `counterparty`, `principalCents`, `interestRatePercentYear`, `dueDate?`, `startDate?`. Mới, tuỳ chọn: `note`, `clientRequestId`, và **một trong hai** (Q6):

- `createTransaction { financialAccountId, transactedAt? }`: tạo giao dịch giải ngân mới từ ví (cho vay = tiền ra, đi vay = tiền vào).
- `transactionId`: giao dịch đã xác nhận có sẵn thành lần giải ngân đầu; `principalCents` phải bằng số tiền của nó (422 `LOAN_AMOUNT_MISMATCH`), chiều phải khớp.

Không gửi gì = nợ cũ, không đụng ví (hành vi trước Phase 90).

### Sửa gốc (S2)

`PUT principalCents` là tổng gốc mới; dòng giải ngân đầu đổi đúng bằng chênh lệch. Dòng đầu gắn giao dịch → 422 `LOAN_PRINCIPAL_LINKED` (sửa ở giao dịch); dòng đầu sau khi đổi ≤ 0 → 422 `LOAN_INVALID`; gốc mới < đã trả → 422 `LOAN_PRINCIPAL_BELOW_PAID`.

### Trả kèm lãi (S1)

Khoản có `interestRatePercentYear > 0`: số trả (hoặc giao dịch gắn / xác nhận) được vượt gốc còn lại nếu phần vượt ≤ lãi tạm tính + `loans.interestToleranceCents` (mặc định 10.000đ). Phần gốc gắn vào khoản; phần vượt thành **giao dịch riêng**, không thuộc "Cho vay / Mượn":

- đi vay → khoản chi danh mục `other`, mô tả "Lãi vay";
- cho vay → khoản thu danh mục `income`, mô tả "Tiền lãi".

Một lần lưu; giao dịch lãi có khoá idempotent UUID v5 từ id dòng; dòng ghi `interestTransactionId`. Trả có ví thì ví trừ/cộng cả hai; gắn giao dịch ngân hàng có sẵn thì giao dịch đó rút về phần gốc và số dư ví không đổi. Trả không ví: chỉ phần gốc vào sổ. Ngoài ngưỡng → 422 `LOAN_REPAYMENT_EXCEEDS_REMAINING`.

Ví dụ (kịch bản i): 2.000.000 lãi 12%/năm, sau 90 ngày, thông báo −2.060.000 → khoản `settled`, giao dịch lãi 60.000 tính vào chi tiêu tháng.

## Giao dịch và sổ vay

- **DTO giao dịch:** `loanLink { loanId, title, counterparty, kind }` (`principal` | `repayment` | `interest`) khi đã gắn; `loanSuggestions[]` (tính khi đọc, tối đa `loans.maxSuggestions`) cho nháp và giao dịch đã xác nhận trong `loans.suggestWindowHours` giờ. Gợi ý: `loanId, title, counterparty, direction, remainingCents, dueDate, nameMatched, interestCents`. Không bao giờ tự liên kết. Phía vận hành không thấy hai trường này.
- **Gợi ý:** khoản `open`, đúng chiều trả, số tiền ≤ còn lại hoặc trong ngưỡng lãi (S1). Cần tên người khớp nội dung giao dịch HOẶC gợi ý mạnh của AI (`relation_hint = debt_repayment`, `strong`). Nhiều khoản cùng khớp thì trả tất cả (Q15).
- **Khớp tên (S3, Q15):** một hàm cho cả chat và thông báo. Câu có dấu thì so giữ dấu ("Hòa trả" không khớp "Hoa"); câu không dấu thì so bỏ dấu ("HOA TRA NO" khớp cả "Hoa" lẫn "Hòa"). Bỏ từ xưng hô đứng đầu tên khoản (`loans.honorifics`) khi tìm trong thông báo.
- **Push:** nháp từ thông báo có gợi ý → `choose_loan_link` (`loanId`, `suggestionCount`); nội dung không có tên người hay số tiền.
- **Đường giao dịch có sẵn:** xoá giao dịch đã gắn → dòng đi theo (làm gốc < đã trả → 422 `LOAN_PRINCIPAL_BELOW_PAID`; dòng giải ngân cuối → khoản bị xoá theo); sửa → giữ danh mục và loại (422 `TRANSACTION_LOAN_LINKED`), số tiền và ngày đi theo; chuyển thành chuyển ví → 422; gộp trùng → liên kết sang bản giữ lại (hai bên cùng gắn → 422). Giao dịch lãi đã tách ở lại khi xoá giao dịch phần gốc.
- **Trùng (Q14):** giao dịch đã tách lãi vẫn là một sự kiện ngân hàng với số gốc + lãi; thông báo gửi lại cùng số đó thành nghi trùng và gộp được, ví chỉ trừ một lần.

## Dự báo

- Đi vay có hạn trong kỳ → `loan_outflow` ở cả ba đường (quá hạn: ngày mai); cho vay có hạn → `loan_inflow` chỉ đường lạc quan (`optimisticOnly`), quá hạn thì bỏ. Nhãn cố định ("Trả khoản vay", "Thu khoản cho vay"); app tra khoản bằng `loanId`.
- `spendable.loansDueCents` và `spendable.loans[]`: khoản đi vay đến hạn trong kỳ (kể cả quá hạn), tách khỏi `upcomingBillsCents`.
- `undatedLoans { count, iOweCents, owedToMeCents }` khi có khoản không hạn; kiểm mua thêm cảnh báo `undated_loan`.
- `unlinkedLoanTransactions { count }`: giao dịch "Cho vay / Mượn" gần đây chưa gắn khoản nào (S5). Không đổi con số dự báo.
- Dự báo của một ví không có khoản vay.

## Nhắc hạn

Job `loan-reminders` 08:05 VN qua `forecast_alerts` (kind `loan_due`), thay `loan-due-reminder` (gỡ bằng `RemoveIfExists` khi khởi động). Trước hạn 0..`reminderDaysBefore` ngày một lần; quá hạn ngày 1, 1+`overdueRepeatDays`, … tối đa `maxOverdueReminders` lần. Câu chung, không tên người, không tiêu đề khoản, không số tiền. Công tắc riêng `loanRemindersEnabled` (`PATCH /users/me/notification-prefs`, `GET /users/me`). Nhắc bị hoãn vì giờ yên lặng mà khoản đã đóng hoặc đổi hạn thì không gửi.

## Agent

`get_loans` chỉ đọc: tổng đang nợ, tổng người khác nợ, số khoản quá hạn, tối đa 10 khoản (chiều, gốc còn lại, hạn). Không tên người, không tiêu đề khoản.

## Mã lỗi

| Mã | HTTP | Khi |
|---|---|---|
| `LOAN_INVALID` | 422 | Đầu vào sai ở endpoint có từ trước |
| `LOAN_REPAYMENT_EXCEEDS_REMAINING` | 422 | Trả vượt còn lại (ngoài ngưỡng lãi S1) |
| `LOAN_PRINCIPAL_BELOW_PAID` | 422 | Gốc mới / sau khi xoá < đã trả |
| `LOAN_PRINCIPAL_LINKED` | 422 | Đổi gốc khi dòng giải ngân đầu gắn giao dịch |
| `LOAN_DIRECTION_MISMATCH` | 422 | Chiều giao dịch không khớp chiều khoản |
| `LOAN_TRANSACTION_NOT_ELIGIBLE` | 422 | Giao dịch không gắn được (nháp, chuyển ví, hoàn/đảo, nhóm, quỹ, nghi trùng, đã gắn chỗ khác) |
| `LOAN_AMOUNT_MISMATCH` | 422 | Tạo từ giao dịch mà gốc khác số tiền giao dịch |
| `LOAN_ALREADY_SETTLED` | 422 | Xoá nợ một khoản đã trả xong |
| `TRANSACTION_LOAN_LINKED` | 422 | Đổi danh mục / loại, chuyển ví hay gộp đôi một giao dịch đã gắn |
| `VALIDATION_ERROR` | 400 | Đầu vào sai ở endpoint mới |

## Cấu hình (`loans`)

`suggestWindowHours` 72, `maxSuggestions` 3, `reminderDaysBefore` 3, `overdueRepeatDays` 7, `maxOverdueReminders` 3, `interestToleranceCents` 10.000, `honorifics` (bạn, anh, chị, em, cô, chú, bác, ông, bà, dì, cậu).
