# API bổ sung: mục tiêu tiết kiệm (G3) và báo cáo (F1, F2) — gửi đội FE

Quy ước chung như `docs/transaction-extras-api.md`: `Authorization: Bearer`, phản hồi `ApiResponse`, tiền là `*Cents` (đồng, số nguyên), ngày dạng `yyyy-MM-dd` là ngày giờ Việt Nam. Ví dụ dưới đây minh họa **hình dạng** (số liệu mẫu); mỗi nhóm đều có test tích hợp chạy thật kiểm tra giá trị.

## Bảng đối chiếu

| # | Yêu cầu | Tình trạng |
|---|---|---|
| 1 | `icon` ở create, PATCH, DTO | Đã làm (mục 1) |
| 2 | `initialSavedCents` | Đã làm (mục 1) |
| 3 | `contributionFrequency` | Đã làm (mục 1) |
| 4 | Trạng thái `Draft` | Đã làm: trạng thái `draft` + `POST /saving-goals/{id}/activate` (mục 2) |
| 5 | Mở rộng `POST saving-goals/feasibility` | Đã làm (mục 3) |
| 6 | `monthly-summary` thêm `prevMonthIncomeCents`, `incomeChangePercent` | Đã làm (mục 4) |
| 7 | Kỳ trước cho tab Ngày/Tuần/Năm | Đã làm ở `GET /reports/period-summary` (mục 5) |
| 8 | Xuất PDF theo tháng và loại | **Backend không làm theo quyết định của chủ dự án**: app tự dựng PDF từ số liệu mục 6 |
| 9 | `category-breakdown`: `prevSpentCents` / `savedCents` | Đã làm (mục 6) |
| 10 | `category-breakdown`: `transactionCount` | Đã làm (mục 6) |
| 11 | `category-breakdown`: `budgetLimitCents`, `budgetSpentCents` | Đã làm (mục 6) |

## 1. Tạo / sửa mục tiêu

`POST /api/v1/saving-goals` (thêm các trường tuỳ chọn, client cũ vẫn chạy):

| Trường | Ý nghĩa |
|---|---|
| `icon` | Tên biểu tượng app chọn, tối đa 40 ký tự. Backend chỉ lưu, không diễn giải |
| `initialSavedCents` | Số đã có sẵn lúc đặt mục tiêu: từ 0 và **nhỏ hơn** `targetCents`. Không phải khoản góp: không có dòng trong lịch sử góp và không tính vào tiền dành ra trong tháng |
| `contributionFrequency` | `weekly` \| `monthly` \| `custom` |
| `isDraft` | `true` = lưu bản nháp (mục 2) |

`PATCH /api/v1/saving-goals/{id}` nhận thêm `icon` (null = giữ nguyên, chuỗi rỗng = xoá) và `contributionFrequency` (null = giữ nguyên). Sửa được khi mục tiêu `active` hoặc `draft`. `initialSavedCents` không sửa được sau khi tạo.

`SavingGoalDto` thêm `icon`, `initialSavedCents`, `contributionFrequency` (chữ thường). `savedCents` = `initialSavedCents` + các khoản góp.

```json
{ "success": true, "data": {
  "id": "…", "name": "Du lịch", "targetCents": 10000000, "savedCents": 2000000, "remainingCents": 8000000,
  "percentComplete": 20, "status": "active", "deadline": "2027-03-17T…", "completedAt": null,
  "icon": "plane", "initialSavedCents": 2000000, "contributionFrequency": "weekly"
}, "error": null, "meta": null }
```

Lỗi: số đã có không nhỏ hơn mục tiêu hoặc âm, biểu tượng quá 40 ký tự → `400` mã `VALIDATION_ERROR` (`details` chứa thông điệp tiếng Việt, ví dụ "Số tiền đã có phải từ 0 và nhỏ hơn số tiền mục tiêu."). `contributionFrequency` sai giá trị → `400`.

## 2. Bản nháp

- `POST /saving-goals` với `"isDraft": true` → trạng thái `draft`. Vẫn cần tên và số tiền mục tiêu.
- Bản nháp **không** tính vào kế hoạch, dự báo hay nhắc hạn, và **không nạp tiền được**.
- `GET /saving-goals?status=draft` liệt kê bản nháp (không truyền `status` thì trả mọi trạng thái, FE lọc theo `status`).
- `POST /saving-goals/{id}/activate` (không body) → `active`. `PATCH` sửa nháp trước khi kích hoạt. `POST /saving-goals/{id}/cancel` huỷ nháp.

| Mã | HTTP | Khi nào |
|---|---|---|
| `SAVING_GOAL_NOT_ACTIVE` | 422 | Nạp tiền vào mục tiêu chưa `active` (kể cả nháp) |
| `SAVING_GOAL_NOT_DRAFT` | 422 | Kích hoạt mục tiêu không phải nháp |
| `SAVING_GOAL_DEADLINE_PASSED` | 422 | Kích hoạt nháp có hạn chót đã qua: sửa hạn trước |
| `NOT_FOUND` | 404 | Mục tiêu không tồn tại / không phải của mình |

## 3. `POST /api/v1/saving-goals/feasibility` (mở rộng)

Body thêm tuỳ chọn `contributionFrequency` (`weekly` | `monthly` | `custom`): chỉ quyết định nhịp của `autoSaveSuggestion`. Phản hồi (`data` vẫn là `null` khi không còn gì phải góp) thêm:

| Trường | Ý nghĩa |
|---|---|
| `feasibilityPercent` | 0–100: số có thể dành ra mỗi tháng so với số cần góp (100 = đủ). `null` khi người dùng chưa khai thu nhập |
| `requiredPerWeekCents`, `requiredPerDayCents` | Số cần góp mỗi tuần / mỗi ngày để kịp hạn (làm tròn lên, luôn có) |
| `monthlyIncomeCents` | Thu nhập tháng người dùng đã khai; `null` nếu chưa |
| `autoSaveSuggestion` | `{ frequency: "weekly"\|"monthly", amountCents, coversTarget }` hoặc `null` |

`autoSaveSuggestion`: số tiền = phần nhỏ hơn giữa số cần góp và số có thể dành ra (thu nhập trừ tổng hạn mức ngân sách); `coversTarget = false` nghĩa là số dành ra được không đủ để kịp hạn (khi đó `isFeasible = false`). Nhịp tuần quy từ tháng theo tháng 30 ngày. `null` khi chưa khai thu nhập hoặc không còn dư đồng nào. Các trường cũ (`requiredPerMonthCents`, `availablePerMonthCents`, `isFeasible`, `suggestedDeadline`, `suggestedTargetCents`) giữ nguyên; `GET /saving-goals/{id}/progress` có cùng các trường mới trong `feasibility`.

```json
{ "success": true, "data": {
  "requiredPerMonthCents": 2000000, "availablePerMonthCents": 20000000, "isFeasible": true,
  "suggestedDeadline": null, "suggestedTargetCents": null,
  "feasibilityPercent": 100, "requiredPerWeekCents": 462000, "requiredPerDayCents": 67000,
  "monthlyIncomeCents": 20000000,
  "autoSaveSuggestion": { "frequency": "monthly", "amountCents": 2000000, "coversTarget": true }
}, "error": null, "meta": null }
```

## 4. `GET /api/v1/reports/monthly-summary`

Thêm `prevMonthIncomeCents` (tổng thu tháng liền trước) và `incomeChangePercent` (% đổi của tổng thu, làm tròn 1 chữ số; `null` khi tháng trước không có thu), cùng kiểu với `prevMonthSpentCents` / `changePercent` đã có.

## 5. `GET /api/v1/reports/period-summary` (kỳ trước cho Ngày / Tuần / Năm)

Query: `fromDate`, `toDate` (`yyyy-MM-dd`, gồm cả hai đầu), `accountId` tuỳ chọn. Tối đa 1100 ngày. Trả kỳ này và **kỳ liền trước dài bằng kỳ này** (kết thúc ngày hôm trước `from`): tab Ngày gửi 1 ngày, Tuần gửi 7 ngày, Năm gửi cả năm.

```json
{ "success": true, "data": {
  "from": "2026-11-11", "to": "2026-11-17", "totalSpentCents": 100000, "totalIncomeCents": 0, "netCents": -100000, "transactionCount": 1,
  "prevFrom": "2026-11-04", "prevTo": "2026-11-10", "prevTotalSpentCents": 200000, "prevTotalIncomeCents": 0,
  "spentChangePercent": -50.0, "incomeChangePercent": null
}, "error": null, "meta": null }
```
Lỗi: `REPORT_INVALID_RANGE` (422, `fromDate` sau `toDate`), `REPORT_RANGE_TOO_LONG` (422, quá 1100 ngày).

## 6. `GET /api/v1/reports/category-breakdown` (F2)

Mỗi phần tử `items[]` thêm:

| Trường | Ý nghĩa |
|---|---|
| `prevSpentCents` | Cùng danh mục ở tháng liền trước (0 nếu không có) |
| `savedCents` | "Tiết kiệm X" = `max(0, prevSpentCents − spentCents)`. Tab thu (`type=income`) luôn 0 |
| `transactionCount` | "N giao dịch" của danh mục trong tháng |
| `budgetLimitCents` | Hạn mức ngân sách **tháng** của danh mục đúng tháng được hỏi (tháng cũ lấy ảnh chụp hạn mức của tháng đó nếu có, không có thì hạn mức hiện tại nếu ngân sách đã tồn tại từ trước). `null` khi không có ngân sách hoặc ở tab thu |
| `budgetSpentCents` | Số đã chi tính cho ngân sách đó (cùng số với `spentCents`); `null` khi `budgetLimitCents` là `null`. Vượt/còn lại = `budgetLimitCents − budgetSpentCents` |

```json
{ "categoryId": "…", "categoryName": "Ăn uống", "categorySlug": "food", "spentCents": 300000, "percent": 100,
  "prevSpentCents": 500000, "savedCents": 200000, "transactionCount": 2,
  "budgetLimitCents": 1000000, "budgetSpentCents": 300000 }
```

**Sửa lỗi kèm theo:** tab Thu (`type=income`) trước đây luôn trả danh sách rỗng vì dùng nhầm bộ lọc của chi tiêu. Giờ trả thu nhập thật theo danh mục (khoản thu đã xác nhận, chưa bị đảo, không phải hoàn tiền và không phải cho vay/mượn) kèm `transactionCount` và `prevSpentCents` (tên trường giữ nguyên cho cả hai tab; ở tab thu nó là thu của tháng trước).

## 7. Xuất PDF (mục 8)

Backend **không** có endpoint xuất PDF. Backend không có font tiếng Việt nên PDF dựng ở server sẽ mất dấu; chủ dự án chọn để app tự dựng PDF trên máy từ `category-breakdown` (mục 6) và `monthly-summary` (mục 4), theo `year`, `month`, `type`.
