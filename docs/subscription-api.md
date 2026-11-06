# Gói đăng ký: tài liệu cho người làm web thanh toán và app Flutter

Backend chỉ có API. Website thanh toán là web app riêng, app Flutter nằm ở repo `FinMate_FE`.

Tính năng nằm sau cờ `features.subscriptions`, mặc định tắt. Khi tắt: không chặn chức năng, không đếm lượt, không tạo đơn mới (422 `SUBSCRIPTION_DISABLED`). Tiền đã trả vẫn được ghi nhận.

## Luồng

```
App   POST /subscriptions/web-session          → url (mã dùng một lần, sống 5 phút)
App   mở url bằng Custom Tab / SFSafariViewController
Web   POST /billing/web/session {code}         → sessionToken (sống 30 phút)
Web   GET  /billing/web/plans
Web   POST /billing/web/checkout {planId, cycle} → actionUrl + fields
Web   tự submit form POST tới actionUrl        → trang của SePay → khách trả tiền
SePay quay về {web}/subscribe/result?invoice=…&status=success|error|cancel
Web   GET  /billing/web/orders/{invoice}       → status
App   khi quay lại: GET /subscriptions/me
```

`status` trên URL quay về chỉ để chọn câu hiển thị, ai cũng sửa được. **Chỉ tin `status` của API.** Sau khi khách trả xong, đơn có thể vẫn `Pending` trong vài giây tới khoảng 10 phút, nên hãy hỏi lại vài lần và hiện "đang xác nhận thanh toán".

## Việc của web

- Đường dẫn cố định, backend dựng các URL này: `{SUBSCRIPTION_WEB_URL}/subscribe?code=…&plan=…` (`plan` có thể không có) và `{SUBSCRIPTION_WEB_URL}/subscribe/result?invoice=…&status=…`.
- Gửi `Referrer-Policy: no-referrer`, và xóa `code` khỏi thanh địa chỉ ngay sau khi đổi. Không nhúng script bên thứ ba vào các trang này.
- Giữ `sessionToken` trong `sessionStorage`, không dùng cookie hay `localStorage`. Gửi bằng header `X-Billing-Session`.
- Phiên hết hạn trả 401. Trang kết quả khi đó bảo khách quay lại app xem gói, đừng hiện "thất bại".
- Submit `fields` nguyên vẹn, gồm cả `signature`. Sửa bất kỳ trường nào thì SePay từ chối.
- Đặt domain vào `SUBSCRIPTION_WEB_ORIGINS`, nếu không trình duyệt không gọi được API.

Ví dụ submit:

```js
const form = document.createElement('form');
form.method = 'POST';
form.action = data.actionUrl;
for (const [name, value] of Object.entries(data.fields)) {
  const input = document.createElement('input');
  input.type = 'hidden'; input.name = name; input.value = value;
  form.appendChild(input);
}
document.body.appendChild(form);
form.submit();
```

## API của web: `/api/v1/billing/web`

| API | Xác thực | Việc |
|---|---|---|
| `POST session` `{ "code": "…" }` | Không | Đổi mã lấy phiên. Dùng một lần; 422 `SUBSCRIPTION_INVALID_WEB_CODE` nếu hết hạn hoặc đã dùng |
| `GET plans` | Phiên | Các gói đang bán, gồm gói miễn phí (`isFree`) |
| `GET me` | Phiên | Gói hiện tại, kỳ đã xếp sau, và `nextPurchaseStartsAt` |
| `POST checkout` `{ "planId": "…", "cycle": "Monthly" \| "Yearly" }` | Phiên | Tạo đơn, trả form đã ký |
| `GET orders/{invoiceNumber}` | Phiên | Đơn của chính người đó; đơn của người khác trả 404 |

Phiên chỉ mở các API trên. JWT không dùng được ở đây, và phiên không dùng được ở nơi khác.

Kết quả `checkout`:

```json
{ "success": true, "data": {
  "actionUrl": "https://pay.sepay.vn/v1/checkout/init",
  "fields": { "merchant": "…", "order_amount": "79000", "signature": "…" },
  "invoiceNumber": "FM261107A1B2C3D4E5F6",
  "planName": "Plus", "cycle": "Monthly", "amountCents": 79000 } }
```

Trạng thái đơn: `Pending`, `Paid`, `Cancelled`, `Expired`, `Voided`, `NeedsReview`. Đơn `NeedsReview`: hiện "đơn đang được kiểm tra, chúng tôi sẽ liên hệ", đừng nói là thất bại, vì khách đã trả tiền.

| Lỗi 422 | Khi nào |
|---|---|
| `SUBSCRIPTION_DISABLED` | Cờ tắt |
| `SUBSCRIPTION_NOT_CONFIGURED` | Máy chủ thiếu cấu hình SePay |
| `SUBSCRIPTION_PLAN_NOT_AVAILABLE` | Gói đã ngừng bán, là gói miễn phí, hoặc không bán theo chu kỳ đó |
| `SUBSCRIPTION_TOO_MANY_PENDING_ORDERS` | Đã có 10 đơn đang chờ trả |

Thiếu `cycle` hoặc sai giá trị trả 400.

## API của app: `/api/v1/subscriptions` (JWT)

| API | Việc |
|---|---|
| `GET plans` | Các gói đang bán |
| `GET me` | Gói hiện tại, ngày hết hạn, kỳ đã xếp sau (`upcoming`), lượt đã dùng từng chức năng |
| `GET orders` | 50 đơn gần nhất |
| `POST web-session` `{ "planId": "…" }` (`planId` tùy chọn) | Lấy liên kết sang web |

Dùng Custom Tab, không dùng WebView: WebView để app đọc được trang thanh toán.

## Chức năng theo gói

| Key | API bị chặn |
|---|---|
| `ai_chat` | `POST /transactions/chat`, `/transactions/parse`, `/chat/analyze` |
| `voice_input` | `POST /transactions/voice`, `/chat/analyze-voice` |
| `receipt_scan` | `POST /transactions/scan-receipt` |

Lỗi 422: `SUBSCRIPTION_FEATURE_LOCKED` (gói không có chức năng) và `SUBSCRIPTION_LIMIT_REACHED` (hết lượt tháng này). App hiện nút Đăng ký khi gặp hai mã này. Lượt tính theo tháng dương lịch giờ Việt Nam. Gọi lỗi, hoặc ảnh hóa đơn không đọc được số tiền, thì được trả lại lượt. Gửi lại với cùng `clientRequestId` khi giao dịch đã lưu được miễn lượt một lần.

Đọc thông báo ngân hàng và nhập tay luôn miễn phí.

## Quy tắc mua

- Trả trước từng kỳ, không tự trừ tiền. Hết hạn thì về gói miễn phí, dữ liệu giữ nguyên.
- Mua thêm khi còn hạn, cùng gói hay khác gói, kỳ mới đều bắt đầu ngay khi kỳ cuối hết. Không có nâng cấp tức thì. Web phải cho khách thấy ngày bắt đầu trước khi trả, lấy từ `nextPurchaseStartsAt` của `GET /billing/web/me`.
- Người đã mua giữ đúng giá và quyền lợi lúc tạo đơn. Manager sửa gói chỉ áp dụng cho lần mua sau.

## Thông báo đẩy

`data.action`: `subscription_activated` (đã nhận tiền), `subscription_expiring` (trước hạn 7 ngày và 1 ngày, bỏ qua nếu đã có kỳ nối tiếp).

## Phía máy chủ

- IPN: `POST /api/v1/webhooks/sepay/ipn`, khai báo trên SePay với kiểu xác thực `SECRET_KEY`.
- Biến môi trường: xem `backend/.env.example`. `sepay_secret_key`, `sepay_ipn_secret_key` nhập được ở trang quản trị key.
