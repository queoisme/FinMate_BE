# API dự báo dòng tiền — `/api/v1/forecast/cashflow`

> Tạo ở Phase 97 (2026-11-27). Trước đó hợp đồng chỉ nằm ở `docs/REQUIREMENTS.md` FR-FCST-01…03. Tài liệu này mô tả trường trả về và cách tính ba đường.
> Thay đổi Phase 97 chỉ **thêm** trường; không trường nào bị đổi tên hay bỏ.

## Endpoint

| Phương thức | Đường dẫn | Ghi chú |
|---|---|---|
| `GET` | `/api/v1/forecast/cashflow?accountId=&to=` | `accountId` bỏ trống = mọi ví được tính; `to` mặc định cuối tháng (còn < 7 ngày thì cuối tháng sau), tối đa `forecast.maxHorizonDays` |
| `POST` | `/api/v1/forecast/cashflow/scenario` | Như trên, thêm 1–`forecast.maxScenarioItems` khoản giả định; không lưu gì |

## Trường trả về (`CashflowForecastDto`)

| Trường | Kiểu | Nghĩa |
|---|---|---|
| `from`, `to` | ngày | Kỳ dự báo; `from` là hôm nay (giờ VN) |
| `startBalanceCents` | int64 | Tổng số dư các ví trong phạm vi lúc gọi |
| `endBalanceCents`, `endLowBalanceCents`, `endHighBalanceCents` | int64 | Số dư cuối kỳ trên ba đường |
| `lowestDay`, `lowestBalanceCents` | ngày, int64 | Ngày có số dư dự kiến thấp nhất |
| `confidence` | `low` / `normal` | `low` khi chi linh hoạt chưa đủ `forecast.minHistoryDays` ngày lịch sử |
| `basis` | `history` / `budget` / `average` / `none` | Cơ sở của chi linh hoạt |
| `incomeBasis` | `history` / `average` / `none` / null | **Phase 97.** Cơ sở của thu không định kỳ; null nếu không chiếu |
| `days[]` | | Từng ngày, xem dưới |
| `mainCauses[]` | | Tối đa 3 khoản **ra** lớn nhất (định kỳ, khoản vay, chi hằng ngày); không gồm khoản thu |
| `dataCoverage`, `undatedLoans`, `unlinkedLoanTransactions` | | Như trước (Phase 84, 90) |

`days[]` (`CashflowDayDto`):

| Trường | Nghĩa |
|---|---|
| `date` | Ngày |
| `expectedBalanceCents`, `lowBalanceCents`, `highBalanceCents` | Số dư cuối ngày trên đường dự kiến / thận trọng / lạc quan |
| `flexibleSpendCents` | Chi linh hoạt dự kiến của ngày (đường dự kiến), đã trừ phần chi hôm nay |
| `irregularIncomeCents` | **Phase 97.** Thu không định kỳ dự kiến của ngày (đường dự kiến), đã trừ phần nhận hôm nay; 0 nếu không có |
| `events[]` | Khoản định kỳ, khoản vay, kịch bản rơi vào ngày đó (`kind`, `label`, `amountCents` có dấu, `optimisticOnly`) |

## Cách tính (phương pháp `mean_dow_income_v2`, Phase 97 tầng 1)

- **Chi linh hoạt:** trung bình theo thứ trong tuần của 90 ngày gần nhất (ngày không chi = 0), không gồm khoản khớp khoản định kỳ, chuyển khoản, vay/mượn, khoản hoàn, khoản bị loại khỏi dự báo. Thiếu lịch sử thì lần lượt: ngân sách tháng, trung bình đang có, 0.
- **Thu không định kỳ:** trung bình theo thứ của các khoản thu cùng loại lọc, không khớp khoản thu định kỳ (FR-FCST-13a). Thiếu lịch sử: mức phẳng.
- **Đường dự kiến** = số dư + sự kiện + thu không định kỳ − chi linh hoạt (chi nhân hệ số ngày lễ nếu có).
- **Đường thận trọng** = sự kiện + thu × (P25 / trung bình theo tháng) − (chi dự kiến + độ lệch P75 − trung vị cũ).
- **Đường lạc quan** = sự kiện (cả khoản cho vay đến hạn) + thu dự kiến − (chi dự kiến − độ lệch trung vị − P25 cũ).
- Khoản thu định kỳ đã quá hạn mà chưa thấy tiền không được chiếu.

Dải hiện là trạng thái tạm (giữ độ rộng cũ quanh đường mới). Tầng 2 của Phase 97, nếu làm, sẽ thay bằng dải tính từ phân phối của tổng nhiều ngày và đổi tên phương pháp; khi đó ý nghĩa `lowBalanceCents` / `highBalanceCents` sẽ được ghi lại ở đây.

## Báo nhóm app

Không bắt buộc sửa gì. Có thể hiện dòng "thu dự kiến" từ `irregularIncomeCents` và nhãn cho `incomeBasis`. Với người thu không đều (freelancer, tiểu thương), số dư dự báo sẽ cao hơn trước (gần thực tế hơn); cảnh báo số dư thấp và kết luận "Mua món này có ổn không?" có thể đổi theo.
