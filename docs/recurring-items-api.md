# Khoản định kỳ — API (`/api/v1/recurring-items`)

Hoá đơn định kỳ và thu nhập đều đặn trong một bảng (`recurring_items`, chiều `inflow`/`outflow`). Có từ Phase 54 (dự báo đợt 1); Phase 92 thêm **kỳ cuối** cho trả góp (điện thoại, xe, vay tiêu dùng, học bổng 10 tháng…). Kế hoạch, D1–D20, S1–S3: `docs/superpowers/plans/2026-11-26-recurring-installments.md`. Yêu cầu: FR-FCST-09, 09a, 12, 18 trong `docs/REQUIREMENTS.md`.

**Tương thích app:** mọi đường dẫn, trường và mã lỗi cũ giữ nguyên. Trường mới đều tuỳ chọn và nằm ở cuối DTO; khoản không có kỳ cuối chạy như trước. Hai thay đổi hành vi có chủ đích: lịch tháng theo `anchorDay` (xem dưới), và danh sách mặc định không còn trả khoản `completed`.

## Mô hình

- `status`: `suggested` | `active` | `paused` | `rejected` | `cancelled` | `completed` (Phase 92). Chỉ `active` được chiếu vào dự báo.
- Duy nhất theo (user, chiều, người nhận đã chuẩn hoá): trùng → 409 `RECURRING_ITEM_DUPLICATE`. Dòng `rejected`/`cancelled`/`suggested` của cùng người nhận được dùng lại khi tạo tay. Dòng `completed` **không** giữ chỗ: tạo được khoản mới cùng người nhận (mua trả góp món mới cùng công ty).
- **Kỳ cuối** (một trong hai, cả hai → 400):
  - `totalOccurrences` (1–600): tổng số kỳ. Số kỳ đã trả = số **kỳ đã trả** còn sống (bảng `recurring_item_payments`) + `prepaidOccurrences` (kỳ đã trả trước khi dùng app). Đã trả ≥ tổng → `completed`.
  - `endDate`: không chiếu kỳ nào sau ngày này; kỳ kế tiếp đã qua ngày đó, hoặc hôm nay đã qua ngày đó → `completed`.
- **`anchorDay`** (1–31, chỉ chu kỳ tháng): ngày gốc trong tháng; tháng ngắn hơn về ngày cuối tháng rồi quay lại (31/01 → 28/02 → 31/03 → 30/04). Không gửi thì lấy ngày của `nextDueDate`. Khoản cũ được migration suy ra (xem REQUIREMENTS §13.6); sửa bằng `PATCH`.
- **Kỳ đã trả** có ba loại: `matched` (hệ thống tự khớp một giao dịch, tối đa một giao dịch mỗi kỳ), `manual` (người dùng đánh dấu, có hoặc không có giao dịch), `settlement` (tất toán). Một giao dịch chỉ phủ một kỳ của một khoản. Giao dịch bị xoá, đảo, chuyển thành chuyển ví hoặc chi quỹ nhóm, hay đổi chiều tiền → kỳ đó mất (ngày đến hạn lùi một kỳ, khoản mở lại nếu thiếu kỳ). Gộp hai giao dịch → kỳ sang bản giữ lại. Sửa số tiền, ngày, người nhận, danh mục → giữ nguyên.
- Hệ thống khớp kỳ ngay sau khi một giao dịch được xác nhận (xác nhận nháp, tạo tay, gộp), qua một job nền theo user; và mỗi đêm lúc 01:30.

## Endpoint

| Method | Path | Ghi chú |
|---|---|---|
| GET | `/recurring-items?status=` | Không truyền `status` → mọi khoản trừ `cancelled`, `rejected`, `completed`. `status=Completed` để xem khoản đã xong |
| POST | `/recurring-items` | Tạo tay (201). Trường mới tuỳ chọn: `totalOccurrences`, `endDate`, `anchorDay`, `prepaidOccurrences` |
| POST | `/recurring-items/from-transaction/{transactionId}` | Tạo từ giao dịch có sẵn (201). Body tuỳ chọn: `cadence` + bốn trường trên. Ngày gốc = ngày của giao dịch |
| PATCH | `/recurring-items/{id}` | Trường null = giữ nguyên. Thêm: bốn trường trên, `clearEnd: true` (bỏ kỳ cuối), `status: "Active"` (chỉ để mở lại khoản đã tất toán) |
| POST | `/recurring-items/{id}/confirm` | Xác nhận đề xuất. Body tuỳ chọn mang bốn trường trên; không có body như cũ |
| POST | `/recurring-items/{id}/reject` · `/pause` · `/resume` | Như cũ |
| DELETE | `/recurring-items/{id}` | Huỷ (204), như cũ |
| GET | `/recurring-items/{id}/payments` | Kỳ đã trả còn sống, mới nhất trước: `id, transactionId, dueDate, kind, createdAt` |
| POST | `/recurring-items/{id}/payments` | Đánh dấu kỳ đang đến hạn là đã trả (201). Body `{ transactionId? }`; bỏ trống = trả tiền mặt không ghi sổ. Đẩy `nextDueDate` sang kỳ sau. Trả `{ item, payment }` |
| DELETE | `/recurring-items/{id}/payments/{paymentId}` | Gỡ một kỳ (204). Gọi lại với kỳ đã gỡ → 204 |
| POST | `/recurring-items/{id}/settle` | Tất toán trước hạn (200). Body `{ transactionId? }` (gắn giao dịch tất toán, tính là một kỳ, không ép số tiền). Đã `completed` → 200 như cũ |

Khoản hoặc kỳ của người khác là 404 ở mọi đường; `transactionId` của người khác cũng 404.

## DTO (`RecurringItemDto`)

Trường cũ: `id, direction, name, counterpartyKey, categoryId, financialAccountId, amountCents, cadence, nextDueDate, status, source, occurrences, lastSeenAt, lastSeenAmountCents, createdAt`. `occurrences` vẫn là số lần bộ phát hiện thấy giao dịch khớp (dùng để tự ghi nhận đề xuất), **khác** `paidOccurrences`.

Thêm ở cuối (Phase 92):

| Trường | Ý nghĩa |
|---|---|
| `totalOccurrences` | Tổng số kỳ, hoặc `null` |
| `prepaidOccurrences` | Kỳ đã trả trước khi dùng app (mặc định 0) |
| `paidOccurrences` | Kỳ đã trả = kỳ đã trả còn sống + `prepaidOccurrences` |
| `remainingOccurrences` | Kỳ còn lại (theo tổng, hoặc số kỳ đến `endDate`); `null` khi không có kỳ cuối |
| `remainingAmountCents` | **Ước tính** = còn lại × `amountCents` (không tính lãi, phạt, đổi giá); `null` khi không có kỳ cuối |
| `endDate` | Ngày kết thúc, hoặc `null` |
| `anchorDay` | Ngày gốc trong tháng (chỉ chu kỳ tháng) |
| `finalDueDate` | Ngày đến hạn của kỳ cuối cùng còn phải trả; `null` khi không có kỳ cuối hoặc đã hết kỳ. Khoản theo số kỳ còn kỳ quá hạn thì ngày này lùi theo (không trước ngày mai) |
| `completedAt` | Lúc hoàn thành, hoặc `null` |
| `settledEarly` | `true` khi hoàn thành do tất toán |

```json
{ "success": true, "data": {
  "id": "…", "direction": "outflow", "name": "Trả góp điện thoại", "counterpartyKey": "fe credit",
  "amountCents": 1200000, "cadence": "monthly", "nextDueDate": "2026-12-15", "status": "active", "source": "manual",
  "occurrences": 0, "lastSeenAt": null, "lastSeenAmountCents": null, "createdAt": "2026-11-26T03:00:00+00:00",
  "totalOccurrences": 12, "prepaidOccurrences": 9, "paidOccurrences": 9, "remainingOccurrences": 3,
  "remainingAmountCents": 3600000, "endDate": null, "anchorDay": 15, "finalDueDate": "2027-02-15",
  "completedAt": null, "settledEarly": false
}, "error": null, "meta": null }
```

## Dự báo

- Chỉ khoản `active` được chiếu, và chỉ đúng số kỳ còn lại / tới `endDate`: dòng tiền, số tiền có thể chi, đánh giá khoản mua, rủi ro ngân sách, dự báo thu nhập, công cụ Agent `upcoming`. Chiều thu cũng dừng đúng kỳ.
- **Kỳ chi quá hạn của khoản theo số kỳ** không mất và không tiêu kỳ: chiếu vào **ngày mai**, các kỳ sau giữ lịch `anchorDay`. Khoản này không bị tự tạm dừng; mỗi kỳ quá hạn có một cảnh báo "Quá hạn …" (kind `bill_due`). Khoản chỉ có `endDate` và khoản không kỳ cuối giữ luật cũ (trong ân hạn coi như trả hôm nay, quá ân hạn bỏ, quá lâu thì tạm dừng + hỏi "còn dùng không?").
- Khoản vừa hoàn thành không còn được chiếu, nhưng các lần trả cũ của nó vẫn bị loại khỏi "thói quen chi tiêu" và "thu không đều", để dự báo không trừ trả góp dưới dạng khác.
- Chưa làm: Agent `runway` / `stress_test` / `sensitivity` tính từ trung bình chi theo tháng trong lịch sử, chưa trừ bớt khoản sắp kết thúc (REQUIREMENTS §13.4).

## Mã lỗi

| Mã | HTTP | Khi nào |
|---|---|---|
| `VALIDATION_ERROR` | 400 | Cả `totalOccurrences` và `endDate`; số ngoài khoảng; `anchorDay` với chu kỳ khác tháng; `prepaidOccurrences ≥ totalOccurrences` khi tạo; `endDate` trước `nextDueDate`; `clearEnd` cùng `totalOccurrences`/`endDate`; `status` khác `Active` |
| `NOT_FOUND` | 404 | Khoản, kỳ, hoặc giao dịch không phải của mình |
| `RECURRING_ITEM_DUPLICATE` | 409 | Đã có khoản chưa hoàn thành cho cùng người nhận và chiều |
| `RECURRING_ITEM_STATE` | 409 | Chuyển trạng thái sai; đánh dấu đã trả / tất toán khoản không ở `active`/`paused`; mở lại khoản không phải đã tất toán |
| `RECURRING_ITEM_PAYMENT_LINKED` | 409 | Giao dịch đã phủ một kỳ (của khoản này hoặc khoản khác) |
| `RECURRING_ITEM_PAYMENT_INVALID` | 422 | Giao dịch chưa xác nhận, là chuyển ví, hoặc sai chiều tiền |
| `RECURRING_ITEM_NO_FINAL` | 422 | Tất toán khoản không có số kỳ hay ngày kết thúc |
