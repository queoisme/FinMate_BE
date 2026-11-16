# Kết quả kiểm nguồn số liệu kinh tế

Ngày kiểm: 2026-11-09. Chỉ đọc, không ghi gì vào hệ thống.

## CPI — Cục Thống kê (nso.gov.vn)

Bộ đọc `CpiReleaseParser` chạy trên trang thật của ba tháng gần nhất; dữ liệu mẫu nằm ở `backend/FinMate.Tests/Fixtures/cpi-release-2026-{08,09,10}.html` và được test.

| Tháng | Bài (đường dẫn rút gọn) | Ngày đăng | CPI bình quân từ đầu năm | Tháng so với cùng kỳ | Tháng so với tháng trước |
|---|---|---|---|---|---|
| 10/2026 | `/du-lieu-va-so-lieu-thong-ke/2026/11/…thang-muoi-va-muoi-thang-nam-2026/` | 03/11/2026 | 4,57% | 5,09% | 0,20% |
| 9/2026 | `/du-lieu-va-so-lieu-thong-ke/2026/10/…thang-chin-quy-iii-va-9-thang-nam-2026/` | 03/10/2026 | 4,52% | 5,08% | 0,62% |
| 8/2026 | `/tin-tuc-thong-ke/2026/09/…thang-tam-va-8-thang-nam-2026/` | 03/09/2026 | 4,45% | 4,89% | 0,47% |

Cả ba tháng đọc ra, cùng cấu trúc câu ("Bình quân … tháng năm 2026, CPI tăng X% so với cùng kỳ năm trước", "CPI) tháng … tăng/giảm … so với tháng trước", "Ngày đăng: dd/mm/yyyy"). Không cần sửa bộ đọc.

### Phát hiện quan trọng: trang chỉ mục

- **`/tin-tuc-khac` (địa chỉ ghi trong kế hoạch) KHÔNG phải nơi liệt kê CPI.** Đó là chuyên mục tin tức (lao động, y học cổ truyền…), không có liên kết "Chỉ số giá tiêu dùng" nào. Để địa chỉ này trong cấu hình thì job sẽ báo "không tìm thấy bài CPI" mỗi tháng.
- Bài CPI nằm ở **hai chuyên mục khác nhau** theo tháng (`du-lieu-va-so-lieu-thong-ke` và `tin-tuc-thong-ke`), nên một trang chuyên mục có thể bỏ sót bài.
- **Trang tìm kiếm của site gom được cả hai** và xếp mới nhất lên đầu (kiểm ngày 2026-11-09, trả đủ 10 bài từ tháng 1 đến tháng 10/2026):
  `https://www.nso.gov.vn/?s=ch%E1%BB%89+s%E1%BB%91+gi%C3%A1+ti%C3%AAu+d%C3%B9ng`
  Đây là địa chỉ nên đặt vào `economicFeeds.cpiIndexUrl`. Job lấy liên kết đầu tiên có chữ "Chỉ số giá tiêu dùng" trong chữ hiển thị, tức bài mới nhất.
- Trang `/du-lieu-va-so-lieu-thong-ke/` chỉ cho thấy bài mới nhất (1 liên kết CPI), dùng được nhưng sẽ gãy khi bài CPI tháng sau đăng sang chuyên mục khác.

### Rủi ro còn lại

- Nguồn là trang web viết cho người đọc, không phải API. Khi đổi giao diện hay đổi cách đặt câu, job sẽ không đọc được: nó giữ số cũ và mở sự cố `economic_feed`, không đoán số.
- Thứ tự kết quả tìm kiếm không được site cam kết. Nếu một ngày bài không phải mới nhất đứng đầu, số bình quân cũ vẫn nằm trong khoảng hợp lý nhưng có thể lệch; đây là lý do giữ ngưỡng tự duyệt ±1 điểm phần trăm và để số lớn hơn chờ người duyệt.
- Chưa đọc điều khoản sử dụng dữ liệu của site; mỗi tháng chỉ gọi 2 trang nên tải không đáng kể.

## Giá xăng

Chưa kiểm (Task 5, cổng quyết định của Manager).
