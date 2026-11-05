# API thông tin thị trường

Admin gửi link website. Hệ thống tự đọc mỗi 3 giờ và tự nhận dạng loại trang. Mọi API dưới đây chỉ dành cho `Admin`, thao tác ghi nằm trong nhật ký admin.

## Loại nguồn

| `kind` | Là gì | Cách đọc |
|---|---|---|
| `price_table` | Trang có bảng giá xăng | Đọc bằng luật, ra số chính xác |
| `feed` | RSS hoặc Atom của báo | Lọc tin về giá cả, rồi tải từng bài |
| `page` | Trang danh sách bài | Lấy link cùng tên miền có từ khóa về giá cả, rồi tải từng bài |

Hệ thống không đọc trang cấm trong `robots.txt`, trang cần đăng nhập hoặc trang chặn công cụ tự động, và không tìm cách vượt qua. Vì vậy Facebook không dùng được. `robots.txt` không thay thế điều khoản sử dụng: trước khi phát hành thật phải kiểm tra điều khoản của từng trang.

## Nguồn

| API | Việc |
|---|---|
| `GET /api/v1/admin/market-sources` | Danh sách, kèm lần đọc gần nhất và mã lỗi |
| `POST /api/v1/admin/market-sources` | Thêm một link: `{ "url": "https://...", "name": "tùy chọn" }`. Trả `{ source, warnings }` |
| `POST /api/v1/admin/market-sources/bulk` | Thêm tối đa 50 link: `{ "urls": [...] }`. Báo kết quả từng link. Mỗi tên miền đọc tối đa 1 lần mỗi giây nên lô lớn có thể mất vài chục giây |
| `PATCH /api/v1/admin/market-sources/{id}` | Sửa `name`, `kind`, `isActive`. Không xóa nguồn |
| `POST /api/v1/admin/market-sources/{id}/test` | Đọc thử, không lưu. Trả giá đọc được hoặc 3 bài đầu tiên |

`warnings` khi thêm nguồn:

| Mã | Ý nghĩa |
|---|---|
| `AI_NOT_ALLOWED` | `robots.txt` chặn bot AI. Vẫn đọc được bảng giá, nhưng nội dung không được gửi cho AI |
| `NO_CONTENT` | Trang gần như trống, thường vì cần JavaScript. Hệ thống không chạy JavaScript |
| `NO_RELEVANT_LINKS` | Lúc thêm chưa thấy link nào về giá cả |

Mã lỗi 422: `MARKET_SOURCE_URL_INVALID`, `MARKET_SOURCE_ADDRESS_NOT_ALLOWED`, `MARKET_SOURCE_ROBOTS_DISALLOWED`, `MARKET_SOURCE_ROBOTS_UNAVAILABLE`, `MARKET_SOURCE_REFUSED`, `MARKET_SOURCE_NOT_FOUND`, `MARKET_SOURCE_TOO_LARGE`, `MARKET_SOURCE_UNSUPPORTED_CONTENT`, `MARKET_SOURCE_UNREACHABLE`. Link đã có trả 409 `MARKET_SOURCE_DUPLICATE`.

## Bài đã đọc

`GET /api/v1/admin/market-documents?sourceId=&status=&offset=&limit=`

Không có toàn văn, chỉ có tiêu đề, link, độ dài, trạng thái. Giữ 180 ngày.

| `status` | Ý nghĩa |
|---|---|
| `pending_ai` | Đã đọc được, chờ AI. Không có key hoặc AI lỗi thì ở lại chờ lượt sau |
| `processed` | AI đã đọc, có thể không rút ra được gì |
| `ai_not_allowed` | Nguồn hoặc trang chứa bài không cho phép dùng nội dung với AI |
| `no_content` | Không có đủ nội dung trong HTML |
| `blocked` | Trang hoặc `robots.txt` từ chối |
| `failed` | Lỗi không thử lại được, ví dụ bài đã bị gỡ |

## Giá xăng

| API | Việc |
|---|---|
| `GET /api/v1/admin/fuel-prices?fuelKey=&offset=&limit=` | Lịch sử giá đã xác nhận. Chỉ có dòng mới khi giá đổi |
| `POST /api/v1/admin/fuel-prices` | Đường ngoại lệ khi nguồn hỏng hoặc sai: `{ "fuelKey", "region1PriceCents", "region2PriceCents", "note" }`. Bắt buộc có ghi chú |

`fuelKey`: `e10_ron95_5`, `e10_ron95_3`, `e5_ron92_2`, `do_0001s_5`, `do_005s_2`, `kerosene_2k`. Giá tính bằng đồng.

Một giá chỉ được xác nhận khi đủ `minAgreeingSources` nguồn thuộc các tên miền khác nhau cho đúng cùng cặp số trong một lượt đọc. Nguồn lệch giờ cập nhật thì chờ lượt sau. Đổi hơn `maxChangePercent` thì không nhận. Sau khi admin sửa tay, nguồn còn báo đúng giá cũ thì không ghi đè, nguồn đổi sang giá khác mới được tính.

## Tin bài và AI

Mỗi bài được gửi cho `POST /api/v1/market-data/extract` của `ai-service`, cần khóa nội bộ. Chỉ gửi nội dung trang công khai, tối đa 20.000 ký tự.

Yêu cầu: `{ "title", "text", "published_at" }`. Trả lời: `{ "status": "ok" | "not_configured" | "failed", "signals": [{ "topic", "direction", "fact_status", "change_percent", "effective_date", "evidence" }] }`.

`topic`: `fuel`, `electricity`, `water`, `food_price`, `other`. `direction`: `up`, `down`, `stable`. `fact_status`: `actual` hoặc `forecast`.

Backend chỉ lưu một mục khi `evidence` có nguyên văn trong bài và `change_percent`, nếu có, nằm trong `evidence`. Việc này cho thấy thông tin đến từ bài, không chứng minh bài đúng. Mỗi ngày gửi tối đa `maxAiDocumentsPerDay` bài, tính cả bài lỗi, mỗi bài tính một lần trong ngày. AI lỗi 3 lần với cùng một bài thì bài chuyển `failed` với mã `AI_FAILED`, để không chặn các bài khác. Vì không phân biệt được lỗi do bài hay do nhà cung cấp, khi Gemini hỏng lâu, cứ 3 lượt đọc sẽ có một bài bị bỏ.

## Sự kiện

Nhiều trang cùng đưa tin một thay đổi giá thì thành sự kiện chờ duyệt. Chỉ tính tin `actual`, tăng hoặc giảm, về `electricity`, `water`, `food_price`, có ngày đăng rõ ràng trong 7 ngày, từ nguồn thuộc ít nhất `minEventDomains` tên miền. Tên miền con tính chung, và đếm theo nguồn admin đã thêm, không theo link trong bài. Tin về xăng không thành sự kiện vì đã có giá thật. Báo hay đăng lại của nhau nên số tên miền không chứng minh tin đúng, vì vậy mặc định phải có admin duyệt.

| API | Việc |
|---|---|
| `GET /api/v1/admin/market-events?status=&offset=&limit=` | Danh sách, kèm tiêu đề, link, đoạn trích từng bài. `status`: `pending`, `approved`, `rejected`, `expired` |
| `POST /api/v1/admin/market-events/{id}/approve` | Duyệt: `{ "note": "tùy chọn" }` |
| `POST /api/v1/admin/market-events/{id}/reject` | Từ chối. Tin đăng lại trong 7 ngày không tạo sự kiện mới |

Sự kiện đã xử lý trả 409 `MARKET_EVENT_NOT_PENDING`. Sự kiện đang mở không có tin mới sau 7 ngày thì chuyển `expired`.

## Cảnh báo

Bật bằng `features.marketAlerts`, mặc định tắt. Việc đọc dữ liệu vẫn chạy khi tắt, và bật cờ không gửi bù tin cũ. Cảnh báo có `kind` là `market_change`, đi qua `forecast_alerts`, theo công tắc `forecastAlertsEnabled` của người dùng. Từ 21:00 đến 08:00 thì hẹn tới 08:00.

Người dùng nhận cảnh báo khi có đủ:

1. **Xăng:** giá `referenceFuelKey` vùng 1 cao hơn giá ngay trước đó, và tăng từ `riseThresholdPercent` so với mốc `compareWindowDays` ngày trước, hoặc so với giá cũ nhất nếu chưa có đủ lịch sử. **Điện, nước, thực phẩm:** có sự kiện tăng giá đã duyệt. Tin về xăng không kích hoạt cảnh báo xăng.
2. Có từ `minCategoryTransactions` giao dịch đã xác nhận trong `spendLookbackDays` ngày ở danh mục tương ứng: xăng là `transport` và tên cửa hàng hoặc mô tả có từ khóa xăng, điện và nước là `bills`, thực phẩm là `food`.
3. Có ngân sách danh mục đó, và dự báo `at_risk`, `over`, hoặc đã dùng từ ngưỡng T1 của người đó.
4. Chưa nhận cảnh báo cùng chủ đề trong `alertCooldownDays` ngày.

Nội dung là mẫu cố định trong code. Không chữ nào đến từ trang web hay AI, ngoài tên nguồn do admin đặt. Số tiền thêm chỉ ước tính cho xăng: chi xăng trung bình mỗi tháng nhân % tăng, làm tròn tới 1.000đ. App cần xử lý `kind` mới này.

Nhiều lượt liền không xác nhận được giá xăng thì mở sự cố `market_data` mức thường. Một nguồn lỗi riêng lẻ chỉ hiện ở `lastErrorCode`.

## Cấu hình

Nhóm `marketData` trong `/admin/system-config`: `minAgreeingSources` (2), `maxDocumentsPerRun` (20), `minPriceCents` (10.000), `maxPriceCents` (60.000), `maxChangePercent` (20), `minEventDomains` (2), `autoPublishNewsEvents` (tắt), `maxAiDocumentsPerDay` (100), `referenceFuelKey` (`e10_ron95_3`), `riseThresholdPercent` (3), `compareWindowDays` (14), `alertCooldownDays` (14), `spendLookbackDays` (60), `minCategoryTransactions` (2).

## An toàn

`SafeWebFetcher` là nơi duy nhất backend mở kết nối tới URL không cố định:

- Chỉ `https` cổng 443. Chặn địa chỉ nội bộ ở lúc mở kết nối, sau khi phân giải tên miền, nên chống được cả chuyển hướng và DNS rebinding.
- Tối đa 3 lần chuyển hướng, 2 MB, 15 giây cho cả lần tải. Không dùng proxy. Chỉ nhận HTML, JSON, XML, text.
- Khai báo `User-Agent: FinMateBot/1.0`, không giả trình duyệt.
