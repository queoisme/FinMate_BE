# Xuất dữ liệu (Phase 91)

Hai cách để người dùng lấy dữ liệu của mình ra:

1. **CSV giao dịch** (đồng bộ): mở bằng Excel / Google Sheets, dùng hằng ngày hoặc khi chuyển app.
2. **Gói toàn bộ dữ liệu cá nhân** (bất đồng bộ, ZIP): phục vụ quyền được cung cấp dữ liệu (Luật BVDLCN 2025, Nghị định 356/2025, hạn 10 ngày) và quyền mang dữ liệu đi.

PDF báo cáo vẫn do app dựng (`docs/goals-and-reports-extras-api.md` mục 7). Kế hoạch, danh mục bảng và quyết định D1–D18, S1–S5: `docs/superpowers/plans/2026-11-25-data-export.md`.

**Công tắc** `features.dataExport` mặc định **bật** (quyền theo luật), chỉ tắt khi sự cố. Tắt thì: CSV, xin mã, tạo yêu cầu, lấy link trả 422 `DATA_EXPORT_DISABLED`; job dựng gói tạm dừng (yêu cầu nằm `Pending`, chạy tiếp khi bật); dọn dẹp vẫn chạy; link đã cấp vẫn tải được tới khi hết 10 phút.

**Không** bị chặn bởi gói trả phí hay hạn mức gói miễn phí (BR-EXP-13): `ExportsController` không có `[RequireSubscriptionFeature]`, có test kiến trúc khoá lại.

## Cấu hình (`dataExport.*` trong cấu hình hệ thống)

| Khoá | Mặc định | Giới hạn | Ý nghĩa |
|---|---|---|---|
| `maxRangeDays` | 366 | 31–3660 | Khoảng ngày tối đa một file CSV |
| `maxRows` | 50.000 | 1.000–500.000 | Số dòng tối đa một file CSV |
| `csvPerHour` | 20 | 1–1.000 | Số file CSV mỗi user trong 60 phút trượt (S3) |
| `maxRequestsPerDay` | 1 | 1–10 | Số yêu cầu gói trong 24 giờ |
| `fileRetentionHours` | 72 | 1–720 | Giữ gói chừng này giờ sau khi xong |
| `maxArchiveMb` | 25 | 1–200 | Gói lớn hơn thì thất bại và mở sự cố (S4) |
| `downloadLinkMinutes` | 10 | 1–60 | Thời gian sống của link tải |
| `requestHistoryDays` | 365 | 30–3650 | Giữ dòng lịch sử yêu cầu (không có file) |

`GET /app-config` trả cả `config.features.dataExport`, `config.dataExport.*` và trường mới `dataExportPackageAvailable` (false khi máy chủ thiếu `SECRETS_MASTER_KEY`, D8: app ẩn nút "Tải toàn bộ dữ liệu", CSV vẫn dùng được).

## 1. CSV giao dịch

`GET /api/v1/exports/transactions.csv?from=&to=&accountId=&categoryId=&status=&delimiter=&includeDeleted=`

| Tham số | Mặc định | Ghi chú |
|---|---|---|
| `from`, `to` | tháng hiện tại (giờ VN) | `yyyy-MM-dd`, ngày giờ VN, **gồm cả hai đầu**. Chỉ có `from` → tới hết tháng của `from`; chỉ có `to` → từ đầu tháng của `to` |
| `accountId` | tất cả | Giao dịch có ví nguồn **hoặc** ví đích là ví này. Ví của người khác → 404 |
| `categoryId` | tất cả | Danh mục hệ thống hoặc danh mục riêng của mình; danh mục riêng của người khác → 404 |
| `status` | `Confirmed` | `Confirmed` \| `Draft` \| `All` |
| `delimiter` | `comma` | `comma` \| `semicolon` (S1, Excel đặt vùng Việt Nam tách cột bằng `;`) |
| `includeDeleted` | `false` | Tuỳ chọn (S6). Mặc định **không** có giao dịch đã xoá/đã gộp. `true` → thêm chúng (theo cùng bộ lọc ngày/ví/danh mục/trạng thái, tính vào `maxRows`) và thêm cột cuối "Đã xoá lúc" (trống với dòng còn sống). Dòng đã xoá luôn có "Tính vào chi tiêu" = Không, "Số tiền tính vào chi tiêu" = 0, nên tổng cột vẫn bằng `totalSpentCents` |

Trả `200 text/csv; charset=utf-8`, `Content-Disposition: attachment; filename="finmate-giao-dich-{from}-{to}.csv"`, `Cache-Control: no-store`. Mọi lỗi trả JSON **trước** byte đầu tiên của file.

### Định dạng (BR-EXP-02, 03)

- UTF-8 **có BOM**; **không** có dòng `sep=` (có dòng này Excel bỏ qua BOM và mất dấu tiếng Việt).
- Xuống dòng CRLF; RFC 4180: ô được bọc `"` khi chứa dấu phân cách đang dùng, `"`, CR, LF hoặc có khoảng trắng đầu/cuối; `"` trong ô nhân đôi. Ô có `,` không bị bọc khi dùng `;` và ngược lại.
- **Chống CSV injection:** ô chữ bắt đầu bằng `=`, `+`, `-`, `@`, tab hoặc CR được thêm `'` phía trước (trước khi bọc ngoặc). Ô số không bao giờ bị thêm, kể cả số âm.
- Số tiền là **số nguyên đồng**, không phân cách nghìn, không ký hiệu.
- Thời điểm ISO 8601 `+07:00`; cột "Ngày" riêng dạng `yyyy-MM-dd`.

### Cột (thứ tự cố định)

| # | Cột | Giá trị |
|---|---|---|
| 1 | Ngày | `yyyy-MM-dd` giờ VN |
| 2 | Thời điểm | `yyyy-MM-ddTHH:mm:ss+07:00` |
| 3 | Loại | Chi / Thu / Chuyển ví |
| 4 | Số tiền | luôn dương |
| 5 | Ví | tên ví (kể cả ví đã xoá) |
| 6 | Ví đích | ví nhận của chuyển ví; trống với giao dịch quỹ nhóm |
| 7 | Danh mục | tên danh mục (hệ thống, riêng hoặc danh mục nhóm) |
| 8 | Nơi chi | |
| 9 | Ghi chú | |
| 10 | Nguồn | Tay / Thông báo / Giọng nói / Hoá đơn / Hỗ trợ. Giao dịch chat đã lưu mang `Manual` nên ghi "Tay" (D1) |
| 11 | Trạng thái | Đã xác nhận / Chờ duyệt (S2: `TransactionStatus` chỉ có hai giá trị; từ chối nháp là xoá) |
| 12 | Quan hệ | một giá trị theo ưu tiên (D11): Đảo giao dịch, Đã bị đảo, Hoàn tiền, Lãi vay, Vay nợ, Quỹ nhóm, Nghi trùng |
| 13 | Liên quan đến | id tương ứng: giao dịch gốc (hoàn/đảo), lệnh đảo (đã bị đảo), khoản vay, dòng quỹ, giao dịch nghi trùng |
| 14 | Gộp từ | các kênh đã gộp, vd. "Thông báo; Tay" |
| 15 | Nhóm | tên nhóm nếu chính user đã chia sẻ giao dịch này |
| 16 | Tính vào chi tiêu | Có / Không |
| 17 | Số tiền tính vào chi tiêu | có dấu: khoản chi được tính → `+số tiền`, hoàn tiền → `−số tiền`, còn lại 0 (D5) |
| 18 | Mã giao dịch | id |

Cột 16–17 đi qua đúng luật của báo cáo (`NetFlow`, loại `loan`, `Transfer`, chỉ `Confirmed`; quyết định Q16 của Phase 90): cho vay / đi vay / trả nợ, chuyển ví, góp/rút quỹ nhóm, thu nhập, cặp đảo và nháp là 0; lãi vay tách riêng là chi thật. Cộng cột 17 của một tháng ra `totalSpentCents` của `GET /reports/monthly-summary` cùng tháng, trừ một trường hợp có chủ ý: tháng có tiền hoàn nhiều hơn tiền chi thì báo cáo chặn chi ở 0 (`cashFlow.refundSurplusCents`) còn cột 17 cộng ra số âm.

**Không có** (BR-EXP-05): nội dung thông báo ngân hàng thô, `clientRequestId`, `notificationLogId`, số dư sau giao dịch, mã tham chiếu ngân hàng, độ tin cậy, phiên bản mô hình.

### Giới hạn

- Khoảng > `maxRangeDays` hoặc số dòng > `maxRows` → 422 `EXPORT_RANGE_TOO_LARGE` (đếm trước, không dựng file).
- Quá `csvPerHour` → 429 `EXPORT_RATE_LIMITED`. Chỉ lần trả file thành công bị đếm; 400/404/422 không đốt lượt.
- `to` trước `from`, `status` hay `delimiter` lạ → 400 `VALIDATION_ERROR`.

## 2. Gói toàn bộ dữ liệu cá nhân

### Luồng

```
POST /exports/personal-data/otp            → 202, email chứa mã 6 số (mục đích data_export, 10 phút, 5 lần sai)
POST /exports/personal-data { code }       → 202 { id, status: "Pending", ... }
(job dựng gói)                             → push action=data_export_ready, exportId; email "Dữ liệu của bạn đã sẵn sàng"
GET  /exports/personal-data/{id}           → status Ready, expiresAt
POST /exports/personal-data/{id}/download-link → { url, expiresAt }
GET  {url}  (vẫn kèm Bearer token)         → application/zip, link chết ngay
```

### Endpoint

| Endpoint | Thành công | Lỗi |
|---|---|---|
| `POST /api/v1/exports/personal-data/otp` | 202. Đang trong 60 giây chờ gửi lại thì không gửi thêm, mã cũ vẫn dùng được | 429 khi đang có yêu cầu chạy hoặc hết hạn mức 24 giờ (**không** gửi mail); 422 `EXPORT_OTP_EMAIL_FAILED`; 422 `DATA_EXPORT_UNAVAILABLE` |
| `POST /api/v1/exports/personal-data` `{ "code": "123456" }` | 202 + yêu cầu `Pending` | thứ tự kiểm: cờ → khoá chủ (422 `DATA_EXPORT_UNAVAILABLE`) → hạn mức (429 `EXPORT_RATE_LIMITED`) → mã (403 `EXPORT_OTP_INVALID` / `EXPORT_OTP_TOO_MANY_ATTEMPTS`). Hạn mức kiểm trước mã nên 429 không đốt mã |
| `GET /api/v1/exports/personal-data` | 200, danh sách yêu cầu trong `requestHistoryDays`, mới nhất trước, không phân trang | |
| `GET /api/v1/exports/personal-data/{id}` | 200 | 404 khi không có hoặc của người khác |
| `POST /api/v1/exports/personal-data/{id}/download-link` | 200 `{ "url": "/api/v1/exports/personal-data/{id}/download?token=…", "expiresAt": "…" }` | 404; 409 `EXPORT_NOT_READY` (chưa xong hoặc thất bại); 410 `EXPORT_EXPIRED` |
| `GET /api/v1/exports/personal-data/{id}/download?token=` | 200 `application/zip`, `finmate-du-lieu-{yyyy-MM-dd}.zip`, `Cache-Control: no-store` | 401 khi không đăng nhập; 404 khi của người khác (kể cả đúng token); 410 `EXPORT_LINK_EXPIRED` (đã dùng, quá hạn, bị link mới thay, thiếu token); 410 `EXPORT_EXPIRED` |

Hạn mức: mỗi user tối đa **một** yêu cầu `Pending`/`Running` (chỉ mục duy nhất một phần chặn cả khi gửi song song) và tối đa `maxRequestsPerDay` yêu cầu trong 24 giờ. Yêu cầu thất bại do hệ thống (`internal_error`, `archive_too_large`, `account_deleted`) **không** tính (D14); `Ready` và `Expired` vẫn tính.

### Yêu cầu (DTO)

```json
{
  "id": "…", "status": "Ready",
  "requestedAt": "…", "startedAt": "…", "readyAt": "…", "failedAt": null, "failureReason": null,
  "expiresAt": "…", "expiredAt": null, "sizeBytes": 183422, "downloadCount": 1, "lastDownloadedAt": "…"
}
```

`status`: `Pending` → `Running` → `Ready` → `Expired`, hoặc `Failed`. `failureReason` là câu tiếng Việt cố định (không bao giờ là lỗi kỹ thuật).

### Link tải (BR-EXP-10, D7, D9)

- Token 32 byte ngẫu nhiên (base64url); DB chỉ giữ SHA-256. Link sống `downloadLinkMinutes`, không bao giờ quá hạn file.
- Endpoint tải **vẫn đòi đăng nhập đúng user**: app tải bằng HTTP client có bearer, không giao URL cho trình duyệt hay `DownloadManager`.
- Link chết ngay khi **bắt đầu** trả file (server không biết chắc client nhận đủ). Tải đứt → lấy link mới; lấy được nhiều lần tới khi file hết hạn, mỗi lần đều ghi audit. Link mới thay link cũ.
- Token nằm ở query string; log request chỉ ghi đường dẫn. Không log token hay link ở đâu khác.

### Push và email

- Xong: push `data_export_ready` (`data`: `action`, `exportId`) và email "FinMate — Dữ liệu của bạn đã sẵn sàng". Không link, không số liệu. Email gửi kể cả khi user tắt push.
- Thất bại do hệ thống / quá cỡ: push `data_export_failed`. Tài khoản đang chờ xoá: không báo gì.

### Nội dung ZIP (BR-EXP-07, 08)

`README.txt` (tiếng Việt: các file, đơn vị tiền, múi giờ, ngày xuất, phiên bản định dạng `1`, cách mở CSV bằng Excel và Google Sheets, cái gì không có và vì sao) và:

| File | Nội dung |
|---|---|
| `profile.json` | hồ sơ, vai trò, đăng nhập bằng mật khẩu / Google (true/false), cài đặt cộng đồng |
| `notification_settings.json` | công tắc thông báo, ngưỡng T1/T2 |
| `consents.json` | trạng thái hiện tại từng loại đồng ý + lịch sử từ 2026-11-25 (audit `User.ConsentChanged`, D3) |
| `devices.json` | thiết bị nhận push (không token), trạng thái dịch vụ đọc thông báo |
| `account_requests.json` | yêu cầu xoá tài khoản, các lần tải dữ liệu |
| `security_log.json` | sự kiện audit trong danh sách cho phép (đăng nhập, mật khẩu, email, đồng ý, xin xoá, xuất dữ liệu), kèm IP, không metadata (D4) |
| `notifications.json` | hộp thư trong app, cảnh báo dự báo |
| `accounts.json` | ví, kể cả đã xoá |
| `categories.json`, `category_rules.json` | danh mục riêng, danh mục đã ẩn, luật nơi chi / người nhận → danh mục |
| `transactions.csv` | mọi giao dịch còn sống (đã xác nhận và chờ duyệt), định dạng như mục 1, luôn dấu phẩy |
| `transactions_deleted.csv` | giao dịch đã xoá hoặc đã gộp, thêm cột "Đã xoá lúc"; bản bị gộp có Quan hệ "Đã gộp vào" (D6) |
| `bank_notifications.json` | package, thời điểm nhận, kết quả, giao dịch tạo ra; không tiêu đề, không nội dung |
| `receipts.json` | hoá đơn đã quét (không ảnh, không chữ OCR thô) |
| `anomalies.json`, `qr_transfers.json` | |
| `budgets.json`, `goals.json`, `recurring_items.json`, `loans.json` | kế hoạch của riêng user; khoản góp vào mục tiêu nhóm có, tên mục tiêu nhóm không |
| `finance_methods.json`, `insights.json`, `forecasts.json` | phản hồi dự báo không có nhãn của nhân viên |
| `groups.json` | nhóm đang/đã tham gia, vai trò; thành viên khác chỉ `{ role, isYou }`; phần của riêng user: chia hoá đơn (hoá đơn người khác trả chỉ còn ngày, phần của mình, danh mục, D10), thanh toán, nhắc nợ, dòng quỹ, lời mời, đề xuất, hoạt động |
| `support.json` | ticket của mình, tin nhắn khách và nhân viên (không `InternalNote`, không id nhân viên, ảnh chỉ đếm), các lần cho phép xem dữ liệu, nhật ký nhân viên đã xem |
| `community.json` | bài, bình luận, lượt thích, báo cáo của mình (ảnh chỉ đếm) |
| `subscriptions.json` | đơn mua, kỳ sử dụng, số lượt |
| `agent_memory.json` | bộ nhớ dài hạn của Trợ lý, lấy từ ai-service; không trả lời sau 3 lần thử thì `{ "unavailable": true }` và README ghi rõ (D2) |

JSON: `{ "formatVersion": 1, "exportedAt": "…+07:00", "items": [...] }` cho danh sách, `"data": {...}` cho một đối tượng; tên trường camelCase như API; tiền là số nguyên đồng (trường `…Cents` cũng là đồng); thời điểm `+07:00` (D13).

**Không có:** mật khẩu (băm), refresh token, token FCM, OTP, secret; giao dịch người khác chia sẻ, tên và email thành viên khác, ghi chú nội bộ của nhân viên; nội dung và tiêu đề thông báo ngân hàng thô, số dư sau giao dịch; ảnh; dữ liệu ở AI DB (chỉ có mã băm, không truy ngược); `daily_summaries`, `transaction_merge_snapshots` (bản sao kỹ thuật). Danh mục đầy đủ từng bảng: kế hoạch mục 2.

## 3. Mã lỗi

| HTTP | Mã | Khi |
|---|---|---|
| 400 | `VALIDATION_ERROR` | tham số CSV sai |
| 403 | `EXPORT_OTP_INVALID` | thiếu, sai, hết hạn, đã dùng, hoặc mã của mục đích khác |
| 403 | `EXPORT_OTP_TOO_MANY_ATTEMPTS` | sai quá 5 lần |
| 404 | `NOT_FOUND` | ví / danh mục / yêu cầu không có hoặc của người khác |
| 409 | `EXPORT_NOT_READY` | lấy link khi chưa `Ready` |
| 409 | `DATA_EXPORT_IN_PROGRESS` | `DELETE /auth/account` khi có yêu cầu `Pending`/`Running` (S5) |
| 410 | `EXPORT_EXPIRED` | file đã hết hạn và bị xoá |
| 410 | `EXPORT_LINK_EXPIRED` | link đã dùng, quá hạn, bị thay, hoặc thiếu token |
| 422 | `DATA_EXPORT_DISABLED` | công tắc tắt |
| 422 | `DATA_EXPORT_UNAVAILABLE` | máy chủ thiếu `SECRETS_MASTER_KEY` (D8) |
| 422 | `EXPORT_RANGE_TOO_LARGE` | quá `maxRangeDays` / `maxRows` |
| 422 | `EXPORT_OTP_EMAIL_FAILED` | không gửi được email chứa mã |
| 429 | `EXPORT_RATE_LIMITED` | quá `csvPerHour`, đang có yêu cầu chạy, hoặc quá `maxRequestsPerDay` |

App chỉ cần hiện `message`.

## 4. Lưu trữ, bảo mật, audit

- Bảng `personal_data_exports` (siêu dữ liệu) và `personal_data_export_archives` (`bytea` đã mã hoá AES-256-GCM bằng `SECRETS_MASTER_KEY`, dữ liệu kèm là `data-export:{id}`). Cùng cơ chế với ảnh ticket nhưng khác ngữ cảnh. Không bao giờ lưu bản rõ.
- Gói nằm trong Postgres nên có thể còn trong **bản sao lưu DB** lâu hơn 72 giờ (vẫn đã mã hoá). Hướng sau: kho file riêng không nằm trong sao lưu (S4, §13.4).
- Job `PersonalDataExportJob` (enqueue khi tạo yêu cầu), `data-export-sweep` (mỗi 5 phút: chạy lại việc bị mất hoặc job chết quá 15 phút thuê; tối đa 3 lượt), `data-export-cleanup` (phút 7 mỗi giờ: hết hạn → `Expired`, xoá file và link, audit; xoá lịch sử cũ; mở / đóng sự cố quá hạn). Dựng ZIP trong bộ nhớ, dừng ngay khi vượt `maxArchiveMb`.
- Sự cố giám sát (mức Normal, không có user trong tiêu đề): `data_export:archive_too_large` (một gói vượt cỡ), `data_export:overdue` (còn yêu cầu chưa xong sau 5 ngày; hạn luật 10 ngày; tự đóng).
- Audit (metadata chỉ `exportId`, kèm `failureCode` khi thất bại; không số tiền, nội dung, token hay bộ lọc): `DataExport.Requested`, `.Ready`, `.Failed`, `.LinkIssued`, `.Downloaded`, `.Expired`, `.TransactionsCsv`. Lịch sử đồng ý: `User.ConsentChanged` (`type`, `version`, `accepted`).
- Không log số tiền, nội dung file, link hay token.

## 5. Xoá tài khoản

- Tài khoản đang chờ xoá vẫn **không** đăng nhập được, nên không tự tải được (BR-EXP-12). Yêu cầu đang chờ của người đã xin xoá thất bại `account_deleted`, không báo.
- `DELETE /auth/account` trả 409 `DATA_EXPORT_IN_PROGRESS` khi còn yêu cầu `Pending`/`Running` (S5). Sau khi đã xin xoá, người dùng lấy dữ liệu qua hỗ trợ (DSAR đầy đủ, phase sau).
- Xoá cứng user xoá yêu cầu và file (khoá ngoại cascade).

## 6. Nhân viên (BR-EXP-14)

Loại ticket mới `PersonalDataRequest` (ưu tiên mặc định P2) và câu trả lời mẫu "Hướng dẫn tự tải dữ liệu cá nhân" (gieo bằng migration, sửa được ở `/ops/canned-replies`). Nhân viên **không** tạo hay tải gói thay khách: không endpoint `/ops` hay `/admin` nào chạm tới xuất dữ liệu (test kiến trúc). Hàng đợi DSAR đầy đủ là phase sau (§13.4).
