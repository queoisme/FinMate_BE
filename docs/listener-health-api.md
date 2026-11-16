# Sức khoẻ dịch vụ đọc thông báo và độ phủ dữ liệu: API

Dành cho nhóm Flutter và Admin (Phase 84, FR-LH-01…11). Mọi route nằm dưới `/api/v1`, cần JWT, trả về envelope chung `{success, data, error, meta}`. Mọi trường mới đều tuỳ chọn: app cũ không gọi endpoint mới vẫn chạy như trước, và độ phủ của nó là `unknown`, không phải `poor`.

Kế hoạch, quyết định D1–D20 và điều chỉnh S1–S7: `docs/superpowers/plans/2026-11-14-listener-health.md`.

## Khái niệm

Flow 1 dựa vào `NotificationListenerService` trên điện thoại. Dịch vụ này có thể chết lặng lẽ: hãng máy diệt app chạy nền, user tắt quyền Notification Access hoặc tắt thông báo của app ngân hàng. Khi đó user tưởng app vẫn ghi đủ, nên báo cáo và dự báo sai mà không ai biết. Phase này cho server biết chuyện đó bằng ba cách:

- **App báo trạng thái**, mỗi ngày một lần và mỗi khi quyền đổi (`PUT /devices/listener-status`).
- **Server tự suy ra "im lặng bất thường"** từ lịch sử thông báo của từng ví, không cần app.
- **Độ phủ dữ liệu**: phần trăm số ngày có ít nhất một thiết bị đang đọc được thông báo. Con số này được gắn vào dự báo.

Không bao giờ gửi lên server: nội dung thông báo, danh sách app khác ngoài các app ngân hàng/ví user đã chọn, số tiền.

## `PUT /devices/listener-status`

```json
{
  "installationId": "6f0c…",
  "permissionGranted": true,
  "listenerConnected": true,
  "selectedPackages": [
    { "packageName": "com.VCB", "notificationsEnabled": true, "lastPostedAt": "2026-11-14T01:23:00Z" }
  ],
  "appVersion": "2.4.0",
  "manufacturer": "Xiaomi"
}
```

| Trường | Bắt buộc | Ý nghĩa |
|---|---|---|
| `installationId` | có | UUID app tự sinh **một lần mỗi lần cài** và lưu cục bộ. Không dùng token FCM, vì token đổi bất cứ lúc nào |
| `permissionGranted` | có | Quyền Notification Access còn không |
| `listenerConnected` | có | Service đang kết nối (`onListenerConnected` / `onListenerDisconnected`) |
| `selectedPackages` | không | **Chỉ** các app ngân hàng/ví user đã chọn, tối đa 50 mục. Package mà server không biết (không phải ví của user, không phải provider) bị bỏ lặng lẽ |
| `…notificationsEnabled` | không | Thông báo của app đó có đang bật trên máy |
| `…lastPostedAt` | không | Lần cuối listener thấy **bất kỳ** thông báo nào của app đó, kể cả thông báo bị lọc trên máy. Ở tương lai quá 10 phút thì server kẹp về giờ server |
| `appVersion` | không | ≤ 32 ký tự |
| `manufacturer` | không | ≤ 64 ký tự; server lưu chữ thường, dùng để chọn trang hướng dẫn tự khởi động / tối ưu pin |

Trả về **204**. Thiếu một trong hai cờ → 400 (body thiếu trường không bị hiểu thành "mất quyền"). User báo cho thiết bị thứ 11 → 422 `LISTENER_TOO_MANY_DEVICES` (`listenerHealth.maxDevicesPerUser`).

**Khi nào gọi:** WorkManager mỗi ngày một lần; ngay khi quyền thay đổi (mở lại app sau khi user vào Cài đặt); ngay khi service mất kết nối hoặc kết nối lại.

**Ngày ok.** Server gộp mọi lần báo trong một ngày (giờ VN, theo lúc server nhận) thành một dòng. Ngày đó ok khi không có lần báo nào `permissionGranted = false` **và** lần báo **cuối** của ngày có `listenerConnected = true`. Mất kết nối giữa ngày rồi có lại thì ngày vẫn ok. Ngày app không báo lần nào lấy trạng thái của lần báo trước nếu lần đó cách trưa ngày ấy không quá `coverageInheritHours` (36 giờ), tức đúng một ngày lỡ báo được bù.

## `GET /me/data-coverage?days=30`

`days` từ 7 đến 90 (mặc định 30); ngoài khoảng → 400.

```json
{
  "windowDays": 30,
  "coveragePercent": 50,
  "level": "poor",
  "accounts": [
    { "accountId": "…", "accountName": "VCB", "status": "silent", "lastNotificationAt": "…", "silentDays": 3, "daysWithNotifications": 27, "notificationsOff": false }
  ],
  "devices": [
    { "installationId": "…", "status": "ok", "permissionGranted": true, "listenerConnected": true, "lastReportedAt": "…", "appVersion": "2.4.0", "manufacturer": "xiaomi" }
  ]
}
```

| Trường | Ý nghĩa |
|---|---|
| `coveragePercent` | % số ngày trong cửa sổ có ít nhất một thiết bị ok, làm tròn xuống. Không tính những ngày trước khi tạo ví theo dõi đầu tiên và trước lần báo đầu tiên. `null` khi chưa thiết bị nào báo |
| `level` | `good` (≥ `forecast.goodCoveragePercent`, 85), `partial` (≥ `forecast.minCoveragePercent`, 60), `poor`, `unknown` |
| `accounts[]` | Chỉ ví đang bật theo dõi thông báo, có package và chưa xoá |
| `…status` | `ok`; `silent` (im quá mức cho phép); `not_enough_data` (ít hơn 8 thông báo trong 30 ngày, ví ít dùng thì im lặng là bình thường); `upload_gap` (listener vẫn thấy thông báo của app này nhưng server không nhận được gì: lỗi ở đường đồng bộ) |
| `…silentDays` | Số ngày trọn vẹn từ thông báo cuối server nhận được |
| `…notificationsOff` | Có thiết bị báo thông báo của app này đang tắt |
| `devices[].status` | `ok`, `permission_lost`, `unknown` (quá 72 giờ không báo) |

**Khoảng im lặng cho phép** = max(48 giờ, 3 × phân vị 90 của khoảng cách giữa hai thông báo liên tiếp trong 30 ngày tính tới thông báo cuối). Ví hai thông báo mỗi ngày thì mức cho phép là 48 giờ; ví mỗi ngày một thông báo thì là 72 giờ.

## Độ phủ trên dự báo

`GET /forecast/cashflow`, `POST /forecast/cashflow/scenario`, `GET /forecast/spendable`, `POST /forecast/purchase-check` có thêm:

```json
"dataCoverage": { "percent": 50, "level": "poor", "windowDays": 90, "warning": "Chỉ 50% số ngày gần đây FinMate đọc được thông báo ngân hàng, nên dự báo có thể thiếu giao dịch." }
```

- Cửa sổ là `forecast.historyWindowDays` kẹp về 7–90 (mặc định 90), không phải 30 như `/me/data-coverage`. `windowDays` cho biết cửa sổ thực dùng.
- `poor`: `confidence` thành `low` (riêng `spendable` vốn không có `confidence`, nên chỉ có `dataCoverage`), kèm `warning` (câu mẫu của backend, không qua AI). **Con số dự báo và độ rộng dải không đổi** (việc nới dải theo độ phủ còn chờ quyết, `REQUIREMENTS.md` §13.4).
- `purchase-check` với `poor`: thêm cảnh báo `data_coverage_poor` vào `warnings`, không bao giờ trả `fits_plan` (trả `insufficient_data` như với dữ liệu một phần); thiếu tiền vẫn báo `shortfall_risk`.
- `unknown` (app chưa báo trạng thái): không đổi gì ngoài việc có `dataCoverage`.
- Cache 10 phút mỗi user; mỗi lần `PUT /devices/listener-status` xoá cache.

**Agent:** khi câu trả lời dựa trên giao dịch của user (không phải chỉ tra tri thức) mà độ phủ là `poor`, câu trả lời có thêm bằng chứng `data_coverage` và mệnh đề cuối là đúng câu `warning` ở trên.

## Push `listener_health`

Qua hộp thư cảnh báo dự báo (`GET /forecast/alerts` cũng thấy), `data.action = "forecast_alert"`, `data.kind = "listener_health"`. Không có số tiền.

| `data.reason` | Khi nào | Thêm |
|---|---|---|
| `permission_lost` | Thiết bị báo `permissionGranted = false` | `installationId`, `manufacturer` |
| `account_silent` | Ví im lặng quá mức cho phép | `accountId`, `accountName`, `silentDays`, `hint?` |

`hint`: `bank_notifications_off` (thiết bị báo thông báo của app đó đang tắt) hoặc `sync_pending` (`upload_gap`: "Mở app để đồng bộ").

- Job chạy 09:00 giờ VN, tôn trọng giờ yên lặng.
- Mỗi ví/thiết bị nhắc cách nhau ít nhất 3 ngày, tối đa 3 lần cho một đợt. Có thông báo mới (với ví) hoặc quyền được cấp lại (với thiết bị) thì đợt kết thúc; lần im lặng / mất quyền sau là đợt mới.
- Thiết bị mất quyền thì chỉ gửi `permission_lost`, không gửi thêm `account_silent` cho các ví mà thiết bị đó theo dõi.
- Thiết bị `unknown` không được nhắc.
- Theo công tắc push chung và công tắc riêng `listenerHealthAlertsEnabled` (mặc định bật), **không** theo `forecastAlertsEnabled`. Đổi qua `PATCH /users/me/notification-prefs` `{pushEnabled, budgetAlertsEnabled, listenerHealthAlertsEnabled?}` (bỏ trống thì giữ nguyên); `GET /users/me` trả `listenerHealthAlertsEnabled`.

## Admin: `GET /admin/monitoring/provider-formats`

Chỉ Admin. Chỉ số đếm, không nội dung, không user.

```json
{
  "providers": [
    { "providerKey": "vietcombank", "displayName": "Vietcombank", "total24h": 40, "readableRate24h": 70.0, "financialShare24h": 50.0, "total7d": 150, "readableRate7d": 98.0, "financialShare7d": 60.0 }
  ],
  "uploadGapsByAppVersion": { "2.4.0": 5, "unknown": 1 }
}
```

- "Đọc được" = không phải `extraction_failed` hay `uncertain`. Kết quả `error` (AI service hỏng) không tính vào đâu.
- `7d` là 7 ngày **trước** 24 giờ gần nhất, đúng cửa sổ nền mà job giám sát so sánh. Tỉ lệ là `null` khi không có mẫu.
- `uploadGapsByAppVersion`: tổng số ví `upload_gap` mỗi ngày trong 7 ngày gần nhất, theo phiên bản app của thiết bị đã thấy thông báo.

Sự cố "Có thể {provider} đã đổi mẫu thông báo" (nguồn `notification_format`, mức Normal): xem mục Giám sát của `docs/ops-api.md`.
