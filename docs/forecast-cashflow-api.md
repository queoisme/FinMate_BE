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

## Cách tính (phương pháp `mean_dow_income_sumband_v3`, Phase 97 tầng 2)

- **Chi linh hoạt:** trung bình theo thứ trong tuần của 90 ngày gần nhất (ngày không chi = 0), không gồm lần trả của khoản định kỳ, chuyển khoản, vay/mượn, khoản hoàn, khoản bị loại khỏi dự báo. Thiếu lịch sử thì lần lượt: ngân sách tháng, trung bình đang có, 0.
- **Thu không định kỳ:** trung bình theo thứ của các khoản thu cùng loại lọc, không phải lần trả của khoản thu định kỳ (FR-FCST-13a). Thiếu lịch sử: mức phẳng.
- **Lần trả của khoản định kỳ** (loại khỏi cả hai thói quen trên): giao dịch khớp khoản trong dung sai số tiền, **hoặc** giao dịch cùng người trả/nhận, cùng chiều, nằm trong cửa sổ ngày quanh một kỳ đến hạn (tháng: hạn − 14 tới hạn + ân hạn), mỗi kỳ đúng một giao dịch, bất kể số tiền. Nhờ vậy lương hay hóa đơn đổi giá không bị đếm hai lần; thưởng, dự án lẻ hay mua thêm của cùng người ngoài cửa sổ (hoặc cùng cửa sổ khi kỳ đó đã có lần trả) vẫn là linh hoạt.
- **Đường dự kiến** = số dư + sự kiện + thu không định kỳ − chi linh hoạt (chi nhân hệ số ngày lễ nếu có).
- **Đường thận trọng / lạc quan** (`lowBalanceCents` / `highBalanceCents`): khoảng P25–P75 của số dư cuối ngày, tính từ phân phối của **tổng** dòng tiền linh hoạt ròng (thu không định kỳ − chi linh hoạt) từ hôm nay tới ngày đó: dự kiến ∓ 0,6745 × σ_L, với σ_L² = σ²·L + σ²·L²/n (σ² là phương sai của phần dư theo ngày quanh trung bình theo thứ, n là số ngày lịch sử, L là số ngày tính từ hôm nay). Khoản cho vay đến hạn chỉ cộng vào đường lạc quan. Nghĩa là khoảng một nửa số trường hợp số dư thật rơi trong dải (backtest tập phát triển: 45–48%).
- **Thiếu lịch sử** (dưới `forecast.minHistoryDays` ngày có giao dịch): giữ cách của tầng 1, tức chi theo P75/P25 từng ngày (với nhánh ngân sách / trung bình thì không có độ rộng) và thu thận trọng = 0, lạc quan = mức phẳng.
- Khoản thu định kỳ đã quá hạn mà chưa thấy tiền không được chiếu.

So với tầng 1 (`mean_dow_income_v2`): dải hẹp đi nhiều với người thu chi đều (lương cố định ở tầm 30: 4,5 → 1,5 triệu) và rộng ra nhiều với người thu không đều (freelancer ở tầm 14: 10,7 → 20,6 triệu), vì giờ nó đo bất định của cả thu lẫn chi.

## Báo nhóm app

Không bắt buộc sửa gì. Có thể hiện dòng "thu dự kiến" từ `irregularIncomeCents` và nhãn cho `incomeBasis`. Với người thu không đều (freelancer, tiểu thương), số dư dự báo sẽ cao hơn trước (gần thực tế hơn); cảnh báo số dư thấp và kết luận "Mua món này có ổn không?" có thể đổi theo.

Từ tầng 2, nếu app có chú thích cho vùng thận trọng–lạc quan, nên ghi là "khoảng một nửa khả năng số dư nằm trong vùng này". Kết luận "Mua món này có ổn không?" dùng đường thận trọng, nên với người thu chi đều nó sẽ bớt khắt khe ở tầm dài, còn với người thu không đều thì khắt khe hơn.
