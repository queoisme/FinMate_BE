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
| `pending_ai` | Đã đọc được, chờ AI |
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

## Cấu hình

Nhóm `marketData` trong `/admin/system-config`: `minAgreeingSources` (2), `maxDocumentsPerRun` (20), `minPriceCents` (10.000), `maxPriceCents` (60.000), `maxChangePercent` (20).

## An toàn

`SafeWebFetcher` là nơi duy nhất backend mở kết nối tới URL không cố định:

- Chỉ `https` cổng 443. Chặn địa chỉ nội bộ ở lúc mở kết nối, sau khi phân giải tên miền, nên chống được cả chuyển hướng và DNS rebinding.
- Tối đa 3 lần chuyển hướng, 2 MB, 15 giây. Chỉ nhận HTML, JSON, XML, text.
- Khai báo `User-Agent: FinMateBot/1.0`, không giả trình duyệt.
