# Đề xuất BE — Điểm sức khỏe tài chính & quy tắc 50/30/20 (cho F6 "Nhận xét AI & Báo cáo tháng")

**Trạng thái: CHƯA DUYỆT — chờ nhóm trưởng xem xét.** Đây là đề xuất, không phải đặc tả đã chốt.
FE hiện build F6 **không** dùng điểm số/100 hay breakdown 50/30/20 — chỉ dùng đúng dữ liệu BE đang
có (xem cuối file). Tài liệu này ghi lại để nhóm trưởng cân nhắc sau, có thể đổi/bỏ bất kỳ phần nào.

> Lưu ý đặt tên: đây KHÔNG phải "Flow 6" theo cách đánh số nội bộ ở
> `flow-6-purchase-decision-assistant.md` (đó là tính năng "mua món này có ổn không?"). Đây là F6
> của Luồng 6 — Phân tích & Báo cáo, phía FE (`.context/NAVIGATION_FLOW_AND_ROADMAP.md`).

## 1. Khoảng trống hiện tại

Stitch F6 vẽ: điểm sức khỏe tài chính (vd "82/100"), breakdown theo quy tắc 50/30/20 (Tiết kiệm/
Chi cố định/Chi biến đổi), 3 ô nhận xét (Điểm sáng/Cần cải thiện/Dự báo tháng tới), nút đánh giá
"Rất hữu ích/Bình thường", xuất Infographic PNG/PDF.

BE hiện chỉ có `InsightGeneratorJob` sinh 3 loại insight cố định
(`vs_last_month`/`recurring_detected`/`unusual_spending`) qua `GET /api/v1/reports/insights`
(`SpendingInsightDto`) — không có điểm số /100, không có phân loại danh mục theo 50/30/20, không
có feedback "hữu ích/bình thường" cho insight (chỉ có `PATCH .../read`), không có export ảnh/PDF.

## 2. Đề xuất (tái dùng tối đa cái đã có, không cần tính toán AI mới)

### 2.1 Thêm `Category.Kind` để chia được 50/30/20
- Thêm cột nullable `Kind` (enum `Essential | Discretionary | Savings`) vào `Category`, migration
  tương ứng, seed mặc định cho category hệ thống (ví dụ: food/housing/bills/transport →
  `Essential`; entertainment/shopping → `Discretionary`; income/loan loại khỏi tính 50/30/20).
  Category do người dùng tự tạo: mặc định `null` (chưa phân loại), không ép đoán.
- `category-breakdown` hiện có (`GetCategoryBreakdownQueryHandler`) chỉ cần thêm field `kind` vào
  `CategoryShareDto` (join theo `Category.Kind` đã seed) — không đổi logic tính `percent`/`spentCents`
  hiện tại.
- Phần "Savings" (20%) không tính được từ category chi tiêu — đề xuất lấy từ
  `netCents` (thu nhập trừ chi tiêu) của `monthly-summary`, hoặc tổng góp mục tiêu tháng này
  (`SavingsGoal` deposits) nếu muốn chính xác hơn — cần nhóm trưởng chọn 1 trong 2 cách.

### 2.2 Endpoint mới `GET /api/v1/reports/financial-health`
- Điểm số tính **bằng công thức xác định** (không gọi AI, cùng triết lý với `PurchaseCheckService`
  — "backend tạo kết luận từ các kiểm tra độc lập", không phải "AI đoán"), kết hợp tín hiệu đã có:
  - Tỷ lệ tiết kiệm thực tế so với mục tiêu 20% (từ `netCents`/thu nhập).
  - Tỷ lệ chi Essential so với mục tiêu 50% (từ `category-breakdown` + `Category.Kind` ở trên).
  - Trạng thái ngân sách từ `/forecast/budgets` (`ok`/`at_risk`/`over`, đã có sẵn).
  - Số khoản bất thường chưa xử lý từ `/forecast/anomalies` (đã có sẵn).
- Trả về: `score` (0-100), `delta` (so với tháng trước — cần lưu lại điểm tháng trước, có thể làm
  snapshot đơn giản hoặc tính lại từ dữ liệu tháng trước, cần bàn thêm), `rule502030: { savingsPercent,
  essentialPercent, discretionaryPercent, savingsStatus, essentialStatus, discretionaryStatus }`.
- Chưa phê duyệt ngưỡng cụ thể cho từng "status" (vd bao nhiêu % thì "Đạt"/"Lưu ý ⚠") — cần nhóm
  trưởng chốt, giống cách `docs/flows/flow-6-purchase-decision-assistant.md` §12 để ngỏ ngưỡng kết
  luận cho purchase-check.

### 2.3 3 ô nhận xét — tái dùng `GET /reports/insights`, không sinh văn bản AI mới
- Map trực tiếp: mỗi `SpendingInsightDto` đã có (`insightType`/`title`/`body`) hiển thị vào đúng 1
  trong 3 ô (ví dụ `vs_last_month` → "Dự báo/nhận xét theo tháng trước", `unusual_spending` →
  "Cần cải thiện", `recurring_detected` → "Điểm sáng"/"Cần lưu ý" tuỳ dấu). Không cần BE sinh thêm
  loại insight mới cho M1 — chỉ cần UI biết map type nào vào ô nào.

### 2.4 Việc KHÔNG đề xuất làm ở M1 (để tránh mở rộng quá nhiều cùng lúc)
- Feedback "Rất hữu ích/Bình thường" cho insight — có thể thêm sau theo đúng pattern
  `AlertFeedbackRequest`/`AnomalyFeedbackRequest` đã có, không khó nhưng chưa cần thiết cho bản đầu.
- Xuất Infographic PNG/PDF — cần thư viện render ảnh ở BE hoặc FE tự vẽ rồi export, phạm vi lớn
  hơn hẳn phần còn lại, nên để riêng một đợt khác nếu được duyệt làm F6 đầy đủ.

## 3. FE hiện đang làm gì (trong lúc chờ duyệt)

F6 (`lib/features/analytics/presentation/screens/f6_ai_summary_screen.dart`) build **chỉ bằng dữ
liệu BE đang có thật**: tổng thu/chi/số dư ròng tháng từ `GET /reports/monthly-summary`, danh sách
đầy đủ nhận xét từ `GET /reports/insights` (có đánh dấu đã xem). Không có điểm số/100, không có
breakdown 50/30/20, không có nút feedback hữu ích/bình thường, không có xuất Infographic — các
phần này sẽ bổ sung sau khi đề xuất ở trên được duyệt (hoặc theo hướng khác nếu nhóm trưởng chọn
khác).
