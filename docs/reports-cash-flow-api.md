# Dòng tiền trong báo cáo — API (Phase 90, Task 0b)

Quyết định Q16 ngày 2026-11-23 (`docs/superpowers/plans/2026-11-23-loan-book-merge.md`): **chi tiêu / thu nhập là tiền đã dùng hết hoặc thực sự kiếm được. Tiền đổi chỗ (chuyển giữa ví của mình, góp/rút quỹ nhóm, cho vay / đi vay / trả nợ) không phải chi tiêu hay thu nhập, nhưng vẫn hiện trong dòng tiền. Lãi vay là chi tiêu / thu nhập thật.**

`totalSpentCents` và `totalIncomeCents` giữ nguyên vị trí và ý nghĩa (không gồm cho vay/mượn, như trước). Phần mới nằm trong object tuỳ chọn `cashFlow`.

## Ở đâu

- `GET /api/v1/reports/monthly-summary?year=&month=&accountId=`: `cashFlow` của cả tháng.
- `GET /api/v1/reports/timeline`: mỗi ngày có `cashFlow` của ngày đó.

## `cashFlow`

| Trường | Ý nghĩa |
|---|---|
| `netCents` | Tổng tác động số dư của các giao dịch **đã xác nhận** trong kỳ, tính thẳng từ số dư từng ví. Không gồm số dư ban đầu của ví tạo giữa kỳ, không gồm nháp |
| `loanOutflowCents` | Giao dịch danh mục "Cho vay / Mượn" làm tiền ra (cho vay, trả nợ) |
| `loanInflowCents` | Giao dịch danh mục "Cho vay / Mượn" làm tiền vào (đi vay, được trả) |
| `groupFundOutflowCents` | Góp quỹ nhóm, hoặc chi quỹ trả bằng ví cá nhân (Phase 85) |
| `groupFundInflowCents` | Rút từ quỹ nhóm về ví |
| `transferOutflowCents` | Chuyển sang ví khác của chính mình. **Chỉ khác 0 khi xem một ví** (`accountId`); xem tất cả ví thì hai vế bù nhau nên luôn 0 |
| `transferInflowCents` | Nhận từ ví khác của chính mình. Như trên |
| `refundSurplusCents` | `max(0, tổng hoàn − tổng chi gộp)` của kỳ: phần chi ròng âm bị chặn để `totalSpentCents` không âm. Thường bằng 0 |
| `reversalNetCents` | Tác động số dư của cặp đảo giao dịch (có dấu). Âm khi khoản gốc bị đảo ở kỳ này mà lệnh đảo ở kỳ khác; dương khi ngược lại; 0 khi cả cặp cùng kỳ. Chi/thu vẫn bỏ cả cặp như trước |

Phí chuyển ví (Phase 83) là một khoản chi thường, nằm trong `totalSpentCents`, không trong hai trường chuyển ví.

## Bất biến

Đúng cho mọi kỳ, mọi ngày, xem tất cả ví hay từng ví:

```
netCents = totalIncomeCents − totalSpentCents
         − loanOutflowCents + loanInflowCents
         − groupFundOutflowCents + groupFundInflowCents
         − transferOutflowCents + transferInflowCents
         + refundSurplusCents + reversalNetCents
```

App có thể dùng nó để giải thích vì sao số dư giảm nhiều hơn số chi: hiện "Chi tiêu", "Thu nhập", rồi "Cho vay / trả nợ", "Quỹ nhóm" và "Số dư thay đổi" (`netCents`).

## `refundSurplusCents` khác `crossPeriodRefundCents` thế nào

- `crossPeriodRefundCents` (Phase 81): tổng tiền hoàn nhận trong tháng cho món mua **tháng khác**. Giải thích vì sao tổng chi tháng nhỏ hơn tổng các danh mục. Chỉ có khi xem tất cả ví.
- `refundSurplusCents`: phần tiền hoàn **vượt quá** tổng chi gộp của kỳ, tức phần bị chặn khi tính `totalSpentCents`. Có cả khi xem một ví.

Ví dụ một tháng: chi 1.000.000; được hoàn 300.000 cho món mua tháng này và 900.000 cho món mua tháng trước.
- `refundedCents` = 1.200.000; `crossPeriodRefundCents` = 900.000.
- Chi ròng = 1.000.000 − 1.200.000 = −200.000, nên `totalSpentCents` = 0 và `refundSurplusCents` = 200.000.

Hai trường trùng số chỉ khi tháng đó không có món mua nào được hoàn ngay trong tháng và tiền hoàn khác kỳ lớn hơn toàn bộ chi.

## Thay đổi đi kèm (2026-11-23)

- **Báo cáo tháng của một ví** (`accountId`): `totalSpentCents` nay cũng chặn dưới ở 0 như khi xem tất cả ví (trước đây có thể âm); phần bị chặn ở `refundSurplusCents`.
- **Dòng thời gian:** tổng chi / thu của một ngày lấy từ **mọi** giao dịch đã xác nhận của ngày đó, không cộng từ các giao dịch trên trang. Trước đây một ngày bị cắt ngang giữa hai trang có tổng thiếu, và nháp trong danh sách cũng bị cộng vào tổng. Chi của ngày chặn ở 0, phần bị chặn ở `refundSurplusCents`. Danh sách giao dịch của ngày (kể cả nháp, kể cả cho vay/mượn) không đổi.
- `daily_summaries` có thêm `cash_flow_net_cents`, `loan_outflow_cents`, `loan_inflow_cents`, `group_fund_outflow_cents`, `group_fund_inflow_cents`, `reversal_net_cents`; migration `AddReportCashFlow` điền cho mọi ngày đã chốt. Không có cột chuyển ví (tóm tắt ngày là cho mọi ví) hay hoàn vượt chi (tính ở mức kỳ).
- Ngân sách, hũ, `spendable`, dự báo, insight, câu hỏi chi tiêu trong chat, Agent, báo cáo nhóm: không đổi.
