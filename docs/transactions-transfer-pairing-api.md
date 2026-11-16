# Chuyển tiền giữa các ví của chính mình: API

Dành cho nhóm Flutter (Phase 83, FR-TXN-18, FR-TXN-33…35). Mọi route nằm dưới `/api/v1`, cần JWT, trả về envelope chung `{success, data, error, meta}`. Enum trong JSON là chuỗi PascalCase. Mọi trường mới đều tuỳ chọn: app cũ không đọc chúng vẫn chạy như trước.

## Khái niệm

Nạp MoMo từ Vietcombank sinh hai thông báo: VCB −500.000 và MoMo +500.000. Ghi thành chi 500k + thu 500k là sai, vì thật ra chỉ là tiền chuyển giữa hai ví của chính người dùng. Rút ATM còn phổ biến hơn: chỉ có một thông báo (VCB −2.000.000), phía tiền mặt không có thông báo nào.

- **Hệ thống chỉ gợi ý**, không bao giờ tự chuyển. Người dùng chọn "Đúng, là chuyển ví" (`convert-to-transfer`) hoặc "Không phải" (`not-transfer`).
- **Ghép hai chiều** được gợi ý khi đủ các điều kiện sau:
  - một khoản chi ở ví X và một khoản thu ở ví Y khác X;
  - tiền vào đến sau tiền ra tối đa 30 phút (hoặc sớm hơn tối đa 2 phút);
  - hai số chênh nhau 0–20.000đ và không quá 2%;
  - **có tín hiệu**: AI nhận ra từ khoá chuyển ví ("nap tien", "rut tien atm", "chuyen tien den vi"…), hoặc nội dung một chiều nhắc tên ví bên kia ("TT MOMO").

  Số tiền và giờ khớp mà không có tín hiệu thì **không** gợi ý: "chuyển cho bạn 500k" và "người khác chuyển cho mình 500k" không được thành một lần chuyển ví.
- **Một chiều**: AI nhận ra từ khoá mà không thấy chiều bên kia. Rút/nộp ATM thì gợi ý ví Tiền mặt; nếu chưa có ví Tiền mặt, `candidateAccounts` rỗng và app mời tạo ví (backend không tự tạo). Loại khác thì gợi ý mọi ví còn lại.
- **Không bao giờ gợi ý hoặc chuyển:**
  - giao dịch đã là chuyển khoản;
  - khoản hoàn/đảo, khoản đã bị đảo;
  - giao dịch đã gộp, đang nghi trùng (gộp hoặc "Không trùng" trước);
  - giao dịch đã khớp QR, khoản chi đã có khoản hoàn;
  - giao dịch đã chia sẻ vào nhóm (gỡ khỏi nhóm trước).
- **Số dư luôn đúng:** sau chuyển đổi, số dư mỗi ví bằng đúng số dư nếu hai thông báo được xác nhận như chi/thu thường, dù trước đó mỗi chiều là nháp hay đã xác nhận. Phần chênh (phí) thành một khoản chi riêng "Phí giao dịch", danh mục "Khác".
- **Ngân sách, báo cáo, dự báo** không còn thấy khoản chi hay khoản thu đó nữa; chỉ phí (nếu có) là chi tiêu.

## Trường mới trên `TransactionDto`

`transferSuggestion` (null khi không có gợi ý). Chỉ có trên nháp, hoặc giao dịch đã xác nhận nhưng vừa tạo trong 30 phút:

```json
{
  "kind": "WalletTopup",
  "pairedTransaction": { "id": "…", "amountCents": 500000, "transactedAt": "…", "financialAccountId": "…", "financialAccountName": "VCB" },
  "candidateAccounts": [],
  "feeCents": 0
}
```

| Trường | Ý nghĩa |
|---|---|
| `kind` | `AtmWithdrawal`, `AtmDeposit`, `WalletTopup`, `WalletWithdrawal`, `OwnAccount` |
| `pairedTransaction` | Chiều bên kia khi đã tìm thấy; null khi gợi ý một chiều |
| `candidateAccounts` | `[{id, name, accountType}]`: ví người dùng chọn khi một chiều; rỗng khi đã ghép |
| `feeCents` | Phí sẽ tạo khi ghép (0 nếu bằng nhau); null khi một chiều |

## Endpoint

### `POST /transactions/{id}/convert-to-transfer`

```json
{ "counterAccountId": "…", "pairedTransactionId": "…", "clientRequestId": "…" }
```

- `{id}` là khoản chi → ví của nó là nguồn, `counterAccountId` là đích. `{id}` là khoản thu → ngược lại.
- Có `pairedTransactionId` → ghép hai chiều; `counterAccountId` phải là ví của chiều kia.
- Không có → đổi một chiều thành chuyển tới / từ ví đã chọn.

Response `200`:

```json
{ "transfer": { …TransactionDto… }, "fee": { …TransactionDto… } , "warnings": ["CASH_BALANCE_NEGATIVE"] }
```

- `transfer`: giao dịch còn lại, `transactionType = "Transfer"`, `financialAccountId` = nguồn, `counterAccountId` = đích, không danh mục. Chiều kia (nếu ghép) bị xoá: mở lại theo id cũ thì `404`.
- `fee`: khoản "Phí giao dịch", hoặc null.
- `warnings`: không chặn. `CASH_BALANCE_NEGATIVE` = ví Tiền mặt bị âm sau chuyển đổi (vd. nộp ATM mà ví Tiền mặt chưa ghi đủ). Hệ thống cho phép số dư âm; app nên nhắc người dùng kiểm tra lại ví Tiền mặt.
- Gọi lại cùng input → `200` với kết quả cũ, không di chuyển tiền lần nữa.

| Lỗi | Khi nào |
|---|---|
| `404` | Giao dịch hoặc ví không tồn tại, hoặc của người khác |
| `422 TRANSFER_SHARED_NOT_ALLOWED` | Đã chia sẻ vào nhóm |
| `422 TRANSFER_NOT_ALLOWED` | Khoản hoàn/đảo, đã bị đảo, đang nghi trùng, đã khớp QR, có khoản hoàn, đã gộp vào giao dịch khác, đã là chuyển khoản tới ví khác. Lý do trong `error.message` |
| `422 TRANSFER_PAIR_MISMATCH` | Hai chiều cùng loại hoặc cùng ví, `counterAccountId` không phải ví chiều kia, chênh lệch vượt giới hạn phí |
| `422 TRANSACTION_TRANSFER_SAME_ACCOUNT` | Ví đích trùng ví nguồn |

### `POST /transactions/{id}/convert-to-transfer/preview`

Cùng body. Không ghi gì; trả kết quả **sẽ** ra và cùng lỗi như lệnh thật:

```json
{ "survivor": { …TransactionDto… }, "fee": null, "balanceChanges": [ { "accountId": "…", "accountName": "VCB", "balanceBeforeCents": 5000000, "balanceAfterCents": 4500000 } ], "warnings": null }
```

### `POST /transactions/{id}/merge/preview`

Cùng body với `merge` (Phase 82), cùng dạng response như trên. Dùng để cho người dùng thấy giao dịch còn lại và số dư từng ví trước khi bấm "Gộp".

### `POST /transactions/{id}/not-transfer`

Không body. Tắt gợi ý cho giao dịch này **và** chiều đang được gợi ý ghép cùng. Sau đó xác nhận như chi/thu thường. Gọi lại → `200`. Vẫn chuyển đổi tay được, và khi đó cờ bị xoá.

## Push

`data.action = "review_transfer"`, kèm `transactionId`, `transferKind`, `candidateCount`, `pairedTransactionId` (khi đã ghép), `amountCents`, `transactionType`. Mở màn "Đây có phải chuyển tiền giữa ví của bạn?":
- có `pairedTransactionId` → hiện hai giao dịch;
- không có → hiện danh sách ví để chọn.

Thứ tự ưu tiên của action: `review_duplicate` > `review_transfer` > `choose_refund_link` > `confirm_one_tap` > `choose_category`. Payload không bao giờ được ghi log.

## Chuyển khoản ghi tay rồi thông báo mới về

Người dùng chat "nạp 500k vào momo" và app lưu bằng `POST /transactions/transfer`. Khi thông báo VCB −500.000 và/hoặc MoMo +500.000 về:
- mỗi thông báo thành **nháp nghi trùng** trỏ vào chuyển khoản đó (`duplicateStatus = "Suspected"`, push `review_duplicate`);
- gộp bằng `POST /transactions/{id}/merge` như Phase 82; chuyển khoản luôn là bản được giữ và nhận giờ của ngân hàng;
- tiền ra của ngân hàng lớn hơn số đã ghi (phí) thì phần chênh thành khoản "Phí giao dịch".

## Chưa có

- Hoàn tác chuyển đổi và hoàn tác gộp. Dữ liệu để làm đã được lưu trong 365 ngày.
- Danh mục phí riêng (đang dùng "Khác"), chờ chủ sản phẩm quyết.
