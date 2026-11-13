# Hoàn tiền và đảo giao dịch: API

Dành cho nhóm Flutter (Phase 81, FR-TXN-18, FR-TXN-27…30). Mọi route nằm dưới `/api/v1`, cần JWT, trả về envelope chung `{success, data, error, meta}`. Enum trong JSON là chuỗi PascalCase (`"Refund"`, `"Reversal"`). Mọi trường mới đều tuỳ chọn: app cũ không gửi chúng vẫn chạy như trước.

## Khái niệm

- **Khoản hoàn** là một giao dịch tiền VÀO (`transactionType = "Credit"`, số dư +) có `relation = "Refund"` và `relatedTransactionId` trỏ tới khoản chi gốc. Nó **không phải thu nhập**: không cộng vào tổng thu ở đâu cả, mà trừ vào chi tiêu của danh mục khoản gốc. Danh mục và nhóm của nó luôn là của khoản gốc.
- **Đảo giao dịch** (`relation = "Reversal"`) là ngân hàng trả lại một giao dịch lỗi. Khoản gốc có `isReversed = true`. Cả cặp bị loại khỏi mọi con số tổng hợp, kể cả ở tháng của khoản gốc, nhưng vẫn hiện trong lịch sử. Lệnh đảo phải bằng đúng số tiền gốc và khoản gốc chưa được hoàn phần nào.
- **Hoàn không có khoản gốc** ("mồ côi"): `relation = "Refund"`, `relatedTransactionId = null`, người dùng chọn một danh mục CHI. Nó trừ vào chi tiêu của danh mục đó.
- **Cashback khuyến mãi không phải hoàn tiền**: nó vẫn là thu nhập như trước.
- Khoản gốc hợp lệ: khoản chi (`Debit`) đã xác nhận, của chính người dùng, không thuộc danh mục cho vay/mượn, chưa bị đảo. Tổng các khoản hoàn đã xác nhận không vượt số tiền gốc. Được hoàn một phần, nhiều lần. Liên kết tay không giới hạn thời gian.
- **Hệ thống chỉ gợi ý**, không bao giờ tự liên kết hay tự xác nhận.

## Trường mới trên `TransactionDto`

| Trường | Kiểu | Khi nào có |
|---|---|---|
| `relation` | `"Refund"` \| `"Reversal"` \| null | Khoản này là hoàn/đảo |
| `relatedTransactionId` | uuid \| null | Khoản chi gốc của khoản hoàn/đảo |
| `isReversed` | bool | Khoản chi đã bị đảo |
| `refundedCents` | long \| null | Khoản chi đã xác nhận: tổng đã hoàn/đảo đã xác nhận |
| `remainingRefundableCents` | long \| null | Khoản chi đã xác nhận: phần còn hoàn được (0 nếu đã đảo) |
| `suggestedRelatedTransactions` | mảng \| null | Chỉ với NHÁP tiền vào mà AI đoán là hoàn/đảo (tối đa 3, tốt nhất trước) |

Một phần tử gợi ý: `{transactionId, amountCents, refundedCents, remainingRefundableCents, merchantName, categoryId, categoryName, financialAccountId, transactedAt}`.

## Endpoint

### `POST /transactions/{id}/refunds` — ghi khoản hoàn/đảo từ khoản chi gốc

`{id}` là khoản chi gốc. Body (tất cả tuỳ chọn):

```json
{ "amountCents": 300000, "transactedAt": "2026-11-13T09:00:00+07:00", "financialAccountId": "…", "relation": "Refund", "clientRequestId": "…" }
```

Mặc định: hoàn **phần còn lại**, vào **ví của khoản gốc**, ngày hiện tại, `relation = "Refund"`. Với `"Reversal"` số tiền mặc định là số gốc.

- `201` khoản hoàn vừa tạo; `200` khi `clientRequestId` đã dùng (trả lại đúng khoản cũ, không tạo thêm).
- `404` khoản gốc hoặc ví không phải của người dùng.
- `422 REFUND_EXCEEDS_ORIGINAL`, `422 REFUND_ORIGINAL_NOT_ELIGIBLE`, `422 REVERSAL_AMOUNT_MISMATCH`.

```json
{ "success": true, "data": { "id": "…", "amountCents": 300000, "transactionType": "Credit", "status": "Confirmed",
  "categoryId": "<danh mục của khoản gốc>", "relation": "Refund", "relatedTransactionId": "<khoản gốc>", "isReversed": false } }
```

### `PUT /transactions/{id}/related` — biến một khoản tiền vào có sẵn thành hoàn/đảo

`{id}` là khoản tiền vào (nháp hoặc đã xác nhận).

```json
{ "relatedTransactionId": "<khoản chi gốc>", "relation": "Refund" }
{ "relatedTransactionId": null, "relation": "Refund", "categoryId": "<danh mục chi>" }
```

Với khoản đã xác nhận, "thu nhập" cũ được hoàn tác và "hoàn tiền" được áp trong cùng một lượt lưu; số dư không đổi. Với nháp, chỉ ghi liên kết; tác động có khi xác nhận. `404` nếu một trong hai giao dịch không phải của người dùng. `422 REFUND_NOT_LINKABLE`: khoản này không phải tiền vào, trỏ vào chính nó, đang liên kết với khoản khác (gỡ trước), hoặc hoàn mồ côi mà danh mục là thu nhập/cho vay. Cũng có thể gặp `REFUND_EXCEEDS_ORIGINAL`, `REFUND_ORIGINAL_NOT_ELIGIBLE`, `REVERSAL_AMOUNT_MISMATCH`.

### `DELETE /transactions/{id}/related` — gỡ liên kết

Khoản đó trở lại thành thu nhập thường (danh mục "Thu nhập"). Gỡ một lệnh đảo thì khoản gốc trở lại mọi tổng hợp. Gọi khi không có liên kết: `200`, không đổi gì.

### `POST /transactions/{id}/confirm` — thêm hai trường

```json
{ "relatedTransactionId": "<khoản chi gốc>", "relation": "Refund" }
```

- Có `relatedTransactionId`: nháp được xác nhận làm khoản hoàn/đảo của khoản đó.
- Không gửi: giữ liên kết đã đặt bằng `PUT /related` (nếu có), không thì là **thu nhập thường** như trước.
- Gửi `categoryId` khác danh mục khoản gốc cùng lúc liên kết → `422 REFUND_CATEGORY_LOCKED`.

### `POST /transactions` — hoàn không có khoản gốc

Thêm `"relation": "Refund"`, bắt buộc `transactionType = "Credit"` và một `categoryId` chi; không chia sẻ vào nhóm được ở bước này.

### `GET /transactions?relation=Refund|Reversal`

Chỉ liệt kê khoản hoàn hoặc khoản đảo. Mọi phần tử chi đã xác nhận có `refundedCents`/`remainingRefundableCents`; nháp có hint có `suggestedRelatedTransactions`.

## Từ thông báo ngân hàng

1. AI đọc thông báo và đoán `relation_hint` (`refund`/`reversal`) cho khoản tiền vào. Từ khoá nằm trong tài liệu cấu hình `notification_vocab` (admin sửa được).
2. Backend vẫn tạo **nháp** như trước. `POST /notifications/analyze` trả thêm `relationHint` và `suggestedRelatedTransactions` (tối đa 3: cùng người dùng, khoản chi đã xác nhận còn hoàn được ≥ số tiền, trong 60 ngày (hoàn) hoặc 3 ngày (đảo); xếp cùng người nhận > cùng ví/nhà cung cấp > đúng số tiền > gần nhất). Gợi ý được tính lại mỗi lần đọc nháp (`GET /transactions/{id}`, danh sách nháp).
3. Push mang `data.action = "choose_refund_link"` (kèm `relationHint`, `suggestionCount`), **không bao giờ** `confirm_one_tap`, kể cả khi AI rất chắc và kể cả khi không có gợi ý nào. App mở màn chọn: một khoản chi gợi ý / chọn khoản khác / "hoàn không có khoản gốc" / "thu nhập thường".
4. Người dùng xác nhận bằng `POST /transactions/{id}/confirm` với `relatedTransactionId` (hoặc không gửi = thu nhập thường).

**Chat:** câu như "được hoàn tiền 300k" trả bản nháp có `relationHint = "Refund"` và `suggestedRelatedTransactions`; app chọn khoản gốc rồi gọi `POST /transactions/{originalId}/refunds`. Chat không tự lưu khoản thu nào.

## Sửa và xoá

| Thao tác | Kết quả |
|---|---|
| Xoá khoản chi còn khoản hoàn/đảo đã xác nhận | `409 HAS_REFUNDS` (xoá các khoản đó trước) |
| Đổi số tiền hoặc loại của khoản chi đó | `409 HAS_REFUNDS` |
| Đổi danh mục khoản chi đó | Được; mọi khoản hoàn đi theo danh mục mới |
| Chia sẻ / bỏ chia sẻ / đổi danh mục nhóm của khoản chi đó | Được; khoản hoàn đi theo |
| Chia sẻ / bỏ chia sẻ riêng một khoản hoàn | `422 REFUND_FOLLOWS_ORIGINAL` |
| Đổi danh mục khoản hoàn đã liên kết | `422 REFUND_CATEGORY_LOCKED` |
| Đổi khoản hoàn sang `Debit` | `422 TRANSACTION_TYPE_CHANGE_NOT_ALLOWED` |
| Đổi số tiền khoản hoàn | Kiểm lại tổng ≤ số gốc (`422 REFUND_EXCEEDS_ORIGINAL`) |
| Xoá khoản hoàn / lệnh đảo | Hoàn tác số dư và ngân sách như giao dịch thường; xoá lệnh đảo thì khoản gốc trở lại |
| Xoá khoản chi khi chỉ có NHÁP trỏ tới nó | Được; nháp mất liên kết |

## Con số ở các màn hình

- **Ngân sách:** khoản hoàn làm giảm `spentCents` ở tháng của **ngày nhận tiền hoàn** (không sửa tháng cũ); lệnh đảo hoàn tác khoản gốc ở **tháng của khoản gốc**. `spentCents` không xuống dưới 0. Không có cảnh báo mới, và cảnh báo đã gửi không bị gửi lại.
- **Báo cáo tháng** (`GET /reports/monthly-summary`): `totalSpentCents` là chi ròng (chi − hoàn, chặn dưới ở 0), `totalIncomeCents` không gồm khoản hoàn, thêm `refundedCents` (tổng hoàn trong tháng) và `crossPeriodRefundCents` (phần hoàn trong tháng cho món mua tháng khác, null nếu không có). Vì phần này, tổng chi tháng có thể nhỏ hơn tổng các dòng danh mục; danh mục có số ròng ≤ 0 không có dòng.
- **Báo cáo nhóm** (`GET /groups/{id}/reports/monthly`): chi ròng, thu không gồm hoàn, thêm `refundedCents`.
- **Dự báo:** khoản chi hoàn 100% và cặp đảo bị loại khỏi thói quen chi, phát hiện bất thường, xu hướng danh mục, nhận diện định kỳ; hoàn một phần dùng số ròng (trừ vào **ngày của khoản chi**, nên có thể lệch nhẹ so với báo cáo tháng khi hoàn chéo tháng). Khoản hoàn không bao giờ vào dự báo thu nhập. Số dư tính bình thường.
- **Bất thường:** khoản chi được hoàn 100% hoặc bị đảo tự đóng đánh dấu với lý do `refunded`/`reversed`, không có thông báo. App không gửi được hai giá trị này.
- **Khoản định kỳ:** đảo một lần trả hoá đơn → khoản định kỳ trở về **chưa trả** (lùi một chu kỳ), nhắc hạn chạy lại. Hoàn tiền, kể cả hoàn đủ, không đổi trạng thái đã trả.
- **QR chuyển tiền:** ý định QR đã khớp với một khoản bị đảo **vẫn giữ trạng thái đã khớp**, không quay về chờ; một lần chuyển lại cùng số tiền sẽ không khớp lại ý định cũ. Người dùng muốn chuyển lại thì quét QR mới.

## Mã lỗi

| HTTP | Mã | Nghĩa |
|---|---|---|
| 404 | `NOT_FOUND` | Giao dịch, ví hoặc danh mục không phải của người dùng |
| 409 | `HAS_REFUNDS` | Khoản chi còn khoản hoàn/đảo đã xác nhận |
| 422 | `REFUND_EXCEEDS_ORIGINAL` | Tổng hoàn vượt số tiền gốc (hoặc đã hoàn hết) |
| 422 | `REFUND_ORIGINAL_NOT_ELIGIBLE` | Khoản gốc không phải chi đã xác nhận, là cho vay/mượn, hoặc đã bị đảo |
| 422 | `REVERSAL_AMOUNT_MISMATCH` | Đảo phải bằng đúng số gốc và khoản gốc chưa được hoàn |
| 422 | `REFUND_CATEGORY_LOCKED` | Khoản hoàn dùng danh mục của khoản gốc |
| 422 | `REFUND_NOT_LINKABLE` | Không phải tiền vào, trỏ vào chính nó, đang liên kết khoản khác, hoặc hoàn mồ côi thiếu danh mục chi |
| 422 | `REFUND_FOLLOWS_ORIGINAL` | Khoản hoàn vào/ra nhóm theo khoản gốc |

## Cấu hình hệ thống (admin)

Nhóm `refunds`: `matchWindowDays` (mặc định 60, 1–365), `reversalMatchWindowDays` (3, 1–60), `maxSuggestions` (3, 1–10). Chỉ giới hạn danh sách gợi ý.
