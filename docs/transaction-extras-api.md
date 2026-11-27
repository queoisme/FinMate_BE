# API bổ sung cho màn giao dịch, bản nháp và chat (gửi đội FE)

Mọi API cần `Authorization: Bearer <accessToken>`. Phản hồi theo `ApiResponse`: `{ success, data, error: { code, message, details }, meta }`. Tiền là `*Cents` (đồng VND, số nguyên). Thời gian ISO-8601; ngày dạng `yyyy-MM-dd` là ngày theo giờ Việt Nam. Ví dụ dưới đây lấy từ lần chạy thật của backend (rút bớt trường lặp).

**Đã bỏ khỏi phạm vi theo yêu cầu của chủ dự án:** phí và hạn mức chuyển ví (`GET /transactions/transfer-rules`, mã `TRANSFER_LIMIT_EXCEEDED`). Backend không làm, FE bỏ phần này trên màn hình. `POST /transactions/transfer` giữ nguyên như cũ.

## Bảng đối chiếu yêu cầu

| # | Yêu cầu FE | Tình trạng |
|---|---|---|
| 1 | transfer-rules, `TRANSFER_LIMIT_EXCEEDED` | **Bỏ khỏi phạm vi** |
| 2a | Chi tiết giao dịch: `goalId`, `goalName`, `receiptScanId`, `aiClassification`, `sourceDetail` | Đã làm (mục 1) |
| 2b | `PUT/DELETE /transactions/{id}/goal` | Đã làm (mục 2) |
| 2c | `GET /transactions/{id}/receipt-image` | **Không làm**: ảnh hóa đơn không được lưu (xem dưới). FE bỏ nút xem ảnh |
| 2d | `POST /transactions/{id}/share-receipt` | Đã làm, trả **văn bản** để mở share sheet (mục 3). Không có link công khai |
| 2e | `POST /recurring-items/from-transaction/{id}` | Đã làm (mục 4) |
| 3 | Bản nháp: `sourceDetail`, `aiExplanation`, `confidence` | Đã làm (mục 5) |
| 4 | `GET /assistant/suggestions` | Đã làm (mục 6) |
| 5 | `reports/timeline`, `reports/monthly-summary` | Đã có sẵn, xác nhận ở mục 7 |

**Vì sao không có ảnh hóa đơn:** ảnh hóa đơn được xử lý trong bộ nhớ rồi bỏ, không lưu ở đâu (quyết định riêng tư có từ trước). Chi tiết giao dịch chỉ trả `receiptScanId` (mã lần quét) và `aiClassification`. Nếu sau này muốn có ảnh thì phải lưu file và sửa Terms/Privacy, nên là một quyết định riêng.

---

## 1. `GET /api/v1/transactions/{id}` (mở rộng)

Thêm các trường vào `TransactionDto` (các API danh sách cũng có `goalId`, `sourceDetail`; `goalName`, `receiptScanId`, `aiClassification` **chỉ có ở chi tiết**, ở danh sách là `null`).

| Trường | Ý nghĩa |
|---|---|
| `goalId`, `goalName` | Mục tiêu tiết kiệm đã gắn, hoặc `null` |
| `receiptScanId` | Mã lần quét hóa đơn (chỉ giao dịch `source = Receipt`), hoặc `null` |
| `aiClassification` | `{ confidence 0-1, reason, model }` hoặc `null` |
| `sourceDetail` | `{ channel, label }` |

`sourceDetail.channel`: `Notification` (Thông báo ngân hàng) · `Voice` (Nhập bằng giọng nói) · `Receipt` (Quét hóa đơn) · `Manual` (Nhập tay) · `Support` (Nhân viên hỗ trợ tạo hộ). **Không có giá trị `Chat`** trên giao dịch đã lưu: giao dịch ghi từ chat được lưu với nguồn `Manual`. `Chat` chỉ xuất hiện ở **bản nháp** (mục 5).

`aiClassification` chỉ có khi giao dịch do AI sinh ra (`Notification` hoặc `Receipt`); giao dịch nhập tay thì `null` (FE ẩn khối này).
- `Notification`: `confidence` = số nhỏ hơn giữa độ tin cậy bóc số tiền và phân loại danh mục; `model` = phiên bản bộ phân loại danh mục.
- `Receipt`: `confidence` = độ tin cậy của lần quét; `model` = bộ đọc ảnh (`google_vision` hoặc `local`).
- `reason` là câu tiếng Việt dựng từ tín hiệu có thật (nguồn danh mục: người dùng từng chọn, thương hiệu, từ khóa, mô hình...), không gọi thêm LLM.

```json
{
  "success": true,
  "data": {
    "id": "0f7ffefd-9b4c-480b-af4e-3a2565bf1ecc",
    "financialAccountId": "0fe006b8-34bb-4504-ada9-3accbd01f16e",
    "categoryId": null, "categoryName": null,
    "amountCents": 260000,
    "transactionType": "Debit", "source": "Manual", "status": "Confirmed",
    "merchantName": "Netflix", "description": "Gói tháng",
    "transactedAt": "2026-11-14T07:08:04.102656+00:00",
    "balanceAfterCents": 9740000,
    "createdAt": "2026-11-16T07:08:04.21622+00:00",
    "groupId": null,
    "goalId": "7948e575-95d7-4151-8f75-5df2009126ed",
    "goalName": "Mua laptop",
    "receiptScanId": null,
    "aiClassification": null,
    "sourceDetail": { "channel": "Manual", "label": "Nhập tay" }
  },
  "error": null, "meta": null
}
```

Ví dụ `aiClassification` (giao dịch từ thông báo; minh họa hình dạng): `{ "confidence": 0.7, "reason": "Bóc số tiền từ thông báo ngân hàng bằng luật. Mô hình phân loại đoán danh mục này từ nội dung.", "model": "1.2.0" }`.

Lỗi: `404 NOT_FOUND` (giao dịch không phải của mình hoặc không tồn tại).

## 2. Gắn / bỏ mục tiêu

`PUT /api/v1/transactions/{id}/goal` body `{ "goalId": "<guid>" }` · `DELETE /api/v1/transactions/{id}/goal`.
Trả `200` với giao dịch đã cập nhật (cùng dạng mục 1). Đây chỉ là **nhãn**: không đổi số dư ví hay tiến độ mục tiêu (nạp tiền vào mục tiêu vẫn dùng `POST /saving-goals/{id}/contribute`).

Ví dụ lỗi (mục tiêu không phải của mình): `404`
```json
{ "success": false, "data": null, "error": { "code": "NOT_FOUND", "message": "SavingGoal với id 'e7d819b7-…' không tồn tại.", "details": null }, "meta": null }
```

| Mã | HTTP | Khi nào |
|---|---|---|
| `NOT_FOUND` | 404 | Giao dịch hoặc mục tiêu không tồn tại / không phải của mình |
| `TRANSACTION_GOAL_LINK_NOT_ALLOWED` | 422 | Giao dịch là chuyển ví giữa 2 ví (`Transfer`) |

## 3. `POST /api/v1/transactions/{id}/share-receipt`

Không body. Trả văn bản để FE mở khung chia sẻ của hệ điều hành (không tạo link công khai vì giao dịch là dữ liệu riêng tư).

```json
{ "success": true, "data": {
  "title": "Biên lai Netflix",
  "text": "Chi: Netflix\nSố tiền: 260.000 ₫\nThời gian: 14/11/2026 14:08\nVí: Ví tiền mặt\nGhi chú: Gói tháng"
}, "error": null, "meta": null }
```
Thời gian trong `text` theo giờ Việt Nam. Lỗi: `404 NOT_FOUND`.

## 4. `POST /api/v1/recurring-items/from-transaction/{transactionId}`

Body **không bắt buộc**: `{ "cadence": "weekly" | "monthly" | "yearly" }`, mặc định `monthly`. Tên khoản = tên nơi nhận (hoặc mô tả nếu không có), số tiền, danh mục và ví lấy từ giao dịch; `nextDueDate` = ngày giao dịch cộng một chu kỳ, lặp tới khi sau hôm nay. Thu (`Credit`) thành `inflow`, chi (`Debit`) thành `outflow`. Trả `201` với khoản định kỳ.

**Phase 92:** body nhận thêm, đều tuỳ chọn, `totalOccurrences`, `endDate`, `anchorDay`, `prepaidOccurrences` (ý nghĩa và lỗi 400 như `POST /recurring-items`, xem `docs/recurring-items-api.md`). Không gửi thì như cũ. Ngày gốc trong tháng (`anchorDay`) mặc định là ngày của giao dịch, nên giao dịch ngày 31 cho `nextDueDate` về ngày cuối tháng ở tháng ngắn rồi quay lại 31, không trôi thành 28. DTO trả về có thêm các trường kỳ cuối ở cuối.

```json
{ "success": true, "data": {
  "id": "3afad09c-a967-4663-ac1c-75b23e5dde81",
  "direction": "outflow", "name": "Netflix", "counterpartyKey": "netflix",
  "categoryId": null, "financialAccountId": "0fe006b8-34bb-4504-ada9-3accbd01f16e",
  "amountCents": 260000, "cadence": "monthly", "nextDueDate": "2026-12-14",
  "status": "active", "source": "manual", "occurrences": 0,
  "lastSeenAt": null, "lastSeenAmountCents": null,
  "createdAt": "2026-11-16T07:08:04.6946017+00:00"
}, "error": null, "meta": null }
```

| Mã | HTTP | Khi nào |
|---|---|---|
| `NOT_FOUND` | 404 | Giao dịch không tồn tại / không phải của mình |
| `TRANSACTION_RECURRING_NOT_ALLOWED` | 422 | Giao dịch chưa xác nhận (nháp), là chuyển ví, hoặc không có tên nơi nhận lẫn mô tả |
| `RECURRING_ITEM_DUPLICATE` | 409 | "Bạn đã có khoản định kỳ cho người nhận này." |

## 5. Bản nháp (chat, parse, quét hóa đơn)

Ba nơi trả bản nháp đều có thêm **ba trường mới, đều nullable** (client cũ bỏ qua được):

| Trường | Ý nghĩa |
|---|---|
| `sourceDetail` | `{ channel, label }`. Chat: `Chat` · Voice (`POST /transactions/voice`): `Voice` · quét hóa đơn: `Receipt` · `POST /transactions/parse`: `Chat` |
| `aiExplanation` | Câu tiếng Việt ngắn vì sao chọn số tiền/danh mục |
| `confidence` | `{ overall, amount, category, merchant, occurredAt }`, mỗi số 0–1 hoặc `null` |

Vị trí: `data.draft` và từng phần tử `data.drafts[]` của `POST /transactions/chat` và `/transactions/voice` (trường `confidence`); `POST /transactions/parse` (ở gốc `data`, trường `confidence`); `POST /transactions/scan-receipt` (ở gốc `data`, trường **`fieldConfidence`** vì `confidence` ở đây là số tổng sẵn có từ trước).

Quy tắc điền độ tin cậy (không bịa số): trường nào hệ thống không có căn cứ thì `null`.
- **Chat**: `amount` = 1.0 khi có số tiền (số do luật bóc từ chính câu, không phải mô hình đoán). `category` quy đổi từ nguồn danh mục AI Service báo theo bảng **admin chỉnh được** (cấu hình hệ thống, nhóm `assistant.categorySourceConfidence`; mặc định `memory` 0.95 · `brand` 0.9 · `keyword` 0.85 · `product` 0.8 · `model` 0.7). Không có danh mục thì `category = null`. `overall` = số nhỏ nhất trong các trường có số. `merchant` luôn `null`; `occurredAt` = 1.0 chỉ khi câu có nói thời điểm.
- **Parse** (`/transactions/parse`): `amount` và `category` lấy điểm số AI Service trả về; `overall` là số nhỏ hơn.
- **Quét hóa đơn**: `overall` và `amount` = độ tin cậy lần quét; các trường còn lại `null`.

Ví dụ (bản nháp chat, thương hiệu quen):
```json
{
  "sourceDetail": { "channel": "Chat", "label": "Nhập bằng chat" },
  "aiExplanation": "Số tiền lấy trực tiếp từ câu bạn nhập. Mình nhận ra thương hiệu và biết nó thuộc danh mục này.",
  "confidence": { "overall": 0.9, "amount": 1, "category": 0.9, "merchant": null, "occurredAt": null }
}
```
`aiExplanation` ở chat khi chưa có danh mục: "… Chưa đủ thông tin để chọn danh mục, bạn chọn giúp mình nhé." (FE vẫn dùng `missingFields` có sẵn để biết phải hỏi gì).

## 6. `GET /api/v1/assistant/suggestions`

Danh sách câu gợi ý cho khung chat, có thể rỗng. Mẫu câu và số lượng tối đa (mặc định 4) nằm trong cấu hình hệ thống (`assistant.suggestions`, `assistant.maxSuggestions`), admin sửa được; backend lọc theo ngữ cảnh của chính người dùng: `always` · `noTransactionToday` (chưa có giao dịch hôm nay) · `hasBudget` · `hasGoal`.

```json
{ "success": true, "data": [
  { "key": "add_coffee", "text": "Hôm nay mình uống cà phê 35k" },
  { "key": "spent_month", "text": "Tháng này mình đã chi bao nhiêu?" },
  { "key": "spent_today", "text": "Chi tiêu hôm nay của mình" },
  { "key": "goal_progress", "text": "Mục tiêu tiết kiệm của mình đến đâu rồi?" }
], "error": null, "meta": null }
```
Khi bấm một gợi ý, FE gửi `text` vào `POST /transactions/chat` như người dùng gõ.

## 7. Xác nhận các API báo cáo đã có sẵn (D1)

- `GET /api/v1/reports/timeline?fromDate=&toDate=` trả theo từng ngày `{ date, totalSpentCents, totalIncomeCents, transactions[] }` (phân trang bằng `cursor` trong `meta.nextCursor`). Ví dụ thật: `{"date":"2026-11-14","totalSpentCents":260000,"totalIncomeCents":0,"transactions":[…]}`.
- `GET /api/v1/reports/monthly-summary?year=&month=` dùng được cho **mọi tháng cũ**. Tháng không có dữ liệu trả toàn số 0, không lỗi. Ví dụ thật (12/2025):

```json
{ "success": true, "data": {
  "periodStart": "2025-12-01T00:00:00+07:00", "periodEnd": "2026-01-01T00:00:00+07:00",
  "totalSpentCents": 0, "totalIncomeCents": 0, "netCents": 0,
  "transactionCount": 0, "prevMonthSpentCents": 0, "changePercent": null
}, "error": null, "meta": null }
```
Có thể thêm `accountId` để lọc theo một ví.

## 8. `GET /api/v1/reports/period-summary` (mới, cho bộ chọn khoảng ngày)

Tổng chi / tổng thu cho khoảng ngày bất kỳ (gồm cả hai đầu, giờ VN), kể cả năm trước. Query: `fromDate`, `toDate` dạng `yyyy-MM-dd` (bắt buộc), `accountId` (tuỳ chọn). Cùng nguồn dữ liệu với `monthly-summary` và `timeline` nên ba màn luôn khớp số. Tối đa 1100 ngày một lần gọi. Danh sách giao dịch của khoảng đó vẫn lấy từ `GET /transactions?fromDate=&toDate=`.

```json
{ "success": true, "data": {
  "from": "2026-11-01", "to": "2026-11-16",
  "totalSpentCents": 2145000, "totalIncomeCents": 2000000, "netCents": -145000, "transactionCount": 7
}, "error": null, "meta": null }
```
(Số trong ví dụ minh họa hình dạng; test tích hợp kiểm tra số thật.)

| Mã | HTTP | Khi nào |
|---|---|---|
| `REPORT_INVALID_RANGE` | 422 | `fromDate` sau `toDate` |
| `REPORT_RANGE_TOO_LONG` | 422 | Khoảng dài hơn 1100 ngày |

## Lưu ý cho FE

- Giao dịch lưu từ chat có `source = "Manual"`, nên `sourceDetail.channel` trên **chi tiết** là `Manual`, không phải `Chat`. Nếu Figma cần nhãn "Chat" cho giao dịch đã lưu thì cần một quyết định riêng (thêm nguồn mới `Chat` trong CSDL), hiện chưa làm.
- Mã lỗi chung vẫn như cũ: `401` chưa đăng nhập, `404 NOT_FOUND`, `422` lỗi nghiệp vụ, `409` xung đột.
