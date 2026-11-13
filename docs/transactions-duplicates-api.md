# Bản trùng có liên kết: API

Dành cho nhóm Flutter (Phase 82, FR-TXN-17, FR-TXN-31, FR-TXN-32). Mọi route nằm dưới `/api/v1`, cần JWT, trả về envelope chung `{success, data, error, meta}`. Enum trong JSON là chuỗi PascalCase (`"Suspected"`, `"Dismissed"`). Mọi trường mới đều tuỳ chọn: app cũ không đọc chúng vẫn chạy như trước.

## Khái niệm

- **Trùng kỹ thuật**: cùng một thông báo bị gửi lại (đồng bộ offline, cùng `contentHash`). Bị bỏ âm thầm như trước, không có gì mới.
- **Cùng sự kiện ngân hàng**: hai thông báo cùng ngân hàng/ví, cùng số tiền, cùng **số dư sau giao dịch**, cách nhau tối đa 10 phút. Thông báo sau bị bỏ, `POST /notifications/analyze` trả `status = "Duplicate"` và `duplicateOfTransactionId`, không có nháp.
- **Nghi trùng**: một giao dịch có thể là cùng khoản với một giao dịch đã ghi, ví dụ gõ chat "trà sữa 50k" rồi ngân hàng báo −50.000. Giao dịch mới **vẫn được tạo** (thông báo thì ở dạng nháp), mang `duplicateStatus = "Suspected"` và `duplicateOf` trỏ tới giao dịch kia. Người dùng chọn:
  - **Gộp** → còn lại một giao dịch;
  - **Không trùng** → `duplicateStatus = "Dismissed"`, cặp này không bao giờ được gợi ý lại; nháp xác nhận như thường.
  - Không trả lời: nháp nằm trong tab Chờ duyệt, không ảnh hưởng gì.
- **Ai được so với ai** (cùng chiều chi/thu, **đúng** số tiền):

  | Cặp | Cửa sổ mặc định |
  |---|---|
  | thông báo ↔ thông báo | 10 phút |
  | nhập tay / giọng nói / hỗ trợ ↔ thông báo | 48 giờ |
  | hoá đơn ↔ thông báo | 12 giờ |
  | hoá đơn ↔ nhập tay | 12 giờ, **chỉ cảnh báo** (không gắn nghi trùng) |
  | hai khoản tự nhập | không so |

  Khác ví vẫn là ứng viên (hay ghi nhầm vào ví Tiền mặt). Hai thông báo cùng ngân hàng có số dư sau **khác nhau** là hai giao dịch thật, không bao giờ nghi trùng. Chuyển khoản nội bộ và khoản hoàn/đảo không bao giờ là bản trùng.

## Trường mới trên `TransactionDto`

| Trường | Kiểu | Khi nào có |
|---|---|---|
| `duplicateStatus` | `"Suspected"` \| `"Dismissed"` \| null | Giao dịch có liên kết trùng |
| `duplicateOf` | `DuplicateSummary` \| null | Giao dịch mà khoản này có thể trùng (hoặc đã bác) |
| `suggestedDuplicates` | `DuplicateSummary[]` \| null | Chỉ với NHÁP: tối đa 3 ứng viên, tốt nhất trước, tính lúc đọc |
| `mergedSources` | string[] \| null | Giao dịch là kết quả của một lần gộp; có `"notification"` = ngân hàng đã xác thực |

`DuplicateSummary`: `{id, amountCents, transactedAt, source, merchantName, categoryName, financialAccountName}`. `source` là `"Notification"`, `"Manual"`, `"Voice"`, `"Receipt"` hoặc `"Support"`, để app nói "ngân hàng báo" hay "bạn đã nhập".

## Endpoint

### `POST /transactions/{id}/merge` — gộp hai giao dịch

```json
{ "withTransactionId": "…", "categoryId": "…", "clientRequestId": "…" }
```

`categoryId` tuỳ chọn và thắng mọi nguồn. `clientRequestId` được nhận nhưng không cần: gộp lại cùng cặp đã trả về đúng giao dịch cũ.

**Bản giữ lại do server chọn**, không phụ thuộc thứ tự hai id. Response là bản đó: app mở lại theo `data.id`, giao dịch còn lại trả 404 từ nay.
- Đúng một bản đã xác nhận → giữ bản đó.
- Cả hai đã xác nhận → giữ bản tạo trước.
- Cả hai là nháp → giữ bản ngân hàng báo.
- Bản giữ lại luôn ở trạng thái `Confirmed`.

Trường sau gộp:
- số tiền, thời điểm, ví lấy từ bản ngân hàng báo (không có thì từ hoá đơn);
- danh mục, ghi chú, nơi chi lấy từ bản người dùng nhập.

Số dư: ví của bản bị gộp được trả lại tiền, ví của bản giữ lại bị trừ đúng một lần. Ngân sách, báo cáo, dự báo chỉ còn thấy một khoản. Không gửi lại cảnh báo ngân sách đã gửi.

| Mã | Khi nào |
|---|---|
| `200` | Gộp xong, hoặc cặp này đã gộp rồi (trả về bản giữ lại cũ) |
| `404` | Một trong hai không tồn tại, đã xoá, hoặc của người khác |
| `422 MERGE_NOT_ALLOWED` | Khác chiều hoặc khác số tiền; chuyển khoản; khoản hoàn/đảo; bản bị gộp đã xác nhận và có khoản hoàn hoặc bị đảo; bản bị gộp đã chia sẻ vào nhóm; cả hai đều đã khớp một lần quét QR; một bản đã được gộp vào giao dịch khác. Lý do nằm trong `error.message` |

### `POST /transactions/{id}/not-duplicate` — "Không trùng"

Không có body. Trả giao dịch với `duplicateStatus = "Dismissed"`, `duplicateOf` giữ nguyên. Gọi lại → `200` như cũ. Giao dịch không có liên kết trùng → `422 NOT_SUSPECTED_DUPLICATE`. Của người khác → `404`.

### `GET /transactions?duplicateStatus=Suspected`

Các khoản đang chờ Gộp / Không trùng. Kết hợp được với các bộ lọc khác (`status=Draft`, …).

## Push

`data.action = "review_duplicate"` kèm `transactionId`, `duplicateOfTransactionId`, `suggestionCount`, `amountCents`, `transactionType`. Mở màn so hai giao dịch với hai nút "Gộp" / "Không trùng". Action này thắng `choose_refund_link` và `confirm_one_tap`: một chạm lúc đang nghi trùng là ghi một khoản chi hai lần. Payload không bao giờ được ghi log.

## Các kênh khác

- **Chat** (`POST /transactions/chat`): bản nháp có `suggestedDuplicates`. Có thông báo ngân hàng có thể là cùng khoản thì không tự lưu (`action = "confirm"`); lưu qua `POST /transactions` thì giao dịch mang `Suspected`. `possibleDuplicateTransactionId` giữ nguyên cho app cũ (khoản cùng số tiền trong ±10 phút, hoặc ứng viên hạng 1).
- **Quét hoá đơn** (`POST /transactions/scan-receipt`): kết quả, từng nhóm và từng đơn có `suggestedDuplicates`; `possibleDuplicateTransactionId` = ứng viên hạng 1.
- **`POST /notifications/analyze`**: thêm `duplicateOfTransactionId`; `status` có thể là `"Duplicate"`.

## Chưa có

- Hoàn tác gộp (unmerge).
- Ghép hai thông báo của một lần chuyển giữa hai ví của chính mình (VCB → MoMo) thành chuyển khoản nội bộ.
- Mã tham chiếu giao dịch của ngân hàng (chưa đọc được từ thông báo).
