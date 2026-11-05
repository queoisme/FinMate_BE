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
| `ai_not_allowed` | Nguồn không cho phép dùng nội dung với AI |
| `no_content` | Không có đủ nội dung trong HTML |
| `blocked` | Trang hoặc `robots.txt` từ chối |
| `failed` | Lỗi không thử lại được, ví dụ bài đã bị gỡ |

## Giá xăng

| API | Việc |
|---|---|
| `GET /api/v1/admin/fuel-prices?fuelKey=&offset=&limit=` | Lịch sử giá đã xác nhận. Chỉ có dòng mới khi giá đổi |
| `POST /api/v1/admin/fuel-prices` | Đường ngoại lệ khi nguồn hỏng hoặc sai: `{ "fuelKey", "region1PriceCents", "region2PriceCents", "note" }`. Bắt buộc có ghi chú |

`fuelKey`: `e10_ron95_5`, `e10_ron95_3`, `e5_ron92_2`, `do_0001s_5`, `do_005s_2`, `kerosene_2k`. Giá tính bằng đồng.

Một giá chỉ được xác nhận khi đủ `minAgreeingSources` nguồn cho đúng cùng cặp số trong một lượt đọc. Nguồn lệch giờ cập nhật thì chờ lượt sau. Đổi hơn `maxChangePercent` thì không nhận.

## Tin bài và AI

Mỗi bài được gửi cho `POST /api/v1/market-data/extract` của `ai-service`, cần khóa nội bộ. Chỉ gửi nội dung trang công khai, tối đa 20.000 ký tự.

Yêu cầu: `{ "title", "text", "published_at" }`. Trả lời: `{ "status": "ok" | "not_configured" | "failed", "signals": [{ "topic", "direction", "fact_status", "change_percent", "effective_date", "evidence" }] }`.

`topic`: `fuel`, `electricity`, `water`, `food_price`, `other`. `direction`: `up`, `down`, `stable`. `fact_status`: `actual` hoặc `forecast`.

Backend chỉ lưu một mục khi `evidence` có nguyên văn trong bài và `change_percent`, nếu có, nằm trong `evidence`. Việc này cho thấy thông tin đến từ bài, không chứng minh bài đúng. Tính cả lần lỗi, mỗi ngày gửi tối đa `maxAiDocumentsPerDay` bài.

## Sự kiện

Nhiều trang cùng đưa tin một thay đổi giá thì thành sự kiện chờ duyệt. Chỉ tính tin `actual`, tăng hoặc giảm, về `electricity`, `water`, `food_price`, đăng trong 7 ngày, từ ít nhất `minEventDomains` tên miền. Tin về xăng không thành sự kiện vì đã có giá thật. Báo hay đăng lại của nhau nên số tên miền không chứng minh tin đúng, vì vậy mặc định phải có admin duyệt.

| API | Việc |
|---|---|
| `GET /api/v1/admin/market-events?status=&offset=&limit=` | Danh sách, kèm tiêu đề, link, đoạn trích từng bài. `status`: `pending`, `approved`, `rejected`, `expired` |
| `POST /api/v1/admin/market-events/{id}/approve` | Duyệt: `{ "note": "tùy chọn" }` |
| `POST /api/v1/admin/market-events/{id}/reject` | Từ chối. Tin đăng lại trong 7 ngày không tạo sự kiện mới |

Sự kiện đã xử lý trả 409 `MARKET_EVENT_NOT_PENDING`. Sự kiện đang mở không có tin mới sau 7 ngày thì chuyển `expired`.

## Cấu hình

Nhóm `marketData` trong `/admin/system-config`: `minAgreeingSources` (2), `maxDocumentsPerRun` (20), `minPriceCents` (10.000), `maxPriceCents` (60.000), `maxChangePercent` (20), `minEventDomains` (2), `autoPublishNewsEvents` (tắt), `maxAiDocumentsPerDay` (100).

## An toàn

`SafeWebFetcher` là nơi duy nhất backend mở kết nối tới URL không cố định:

- Chỉ `https` cổng 443. Chặn địa chỉ nội bộ ở lúc mở kết nối, sau khi phân giải tên miền, nên chống được cả chuyển hướng và DNS rebinding.
- Tối đa 3 lần chuyển hướng, 2 MB, 15 giây. Chỉ nhận HTML, JSON, XML, text.
- Khai báo `User-Agent: FinMateBot/1.0`, không giả trình duyệt.
