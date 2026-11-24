# Kế hoạch tài chính ban đầu từ khảo sát — thiết kế

Ngày: 2026-11-23 · Nhánh nền: `dev/tuan`

## 1. Mục đích

Hiện tại khảo sát đầu vào chỉ cho ra **tên một phương pháp** (AI chọn 1 chính + ≤1 hỗ trợ từ signals). Người dùng muốn làm xong khảo sát là có **một kế hoạch tháng đầy đủ**:

> Thu nhập ~10tr · cố định (nhà, hóa đơn, nợ) X · từng danh mục được tiêu bao nhiêu · cuối tháng dư Y.

Kế hoạch đi theo phương pháp đã chọn, và khi người dùng bấm "Áp dụng" thì **hạn mức từng danh mục được tạo sẵn** (bảng `budgets`), nối luôn với cảnh báo T1/T2 đang có.

## 2. Mục tiêu / không làm / tuyệt đối tránh

**Mục tiêu**
- Khảo sát thu đủ số liệu để tính kế hoạch, không phải đoán.
- Mỗi phương pháp thực sự làm kế hoạch khác nhau (50/30/20 khác 6 hũ khác Trả cho mình trước).
- Người dùng xem bản nháp, sửa từng hạn mức, rồi mới áp dụng.

**Không làm ở bản này**
- Tự chỉnh lại kế hoạch sau 1 tháng theo chi tiêu thật (để bản sau).
- Hỏi số tiền và hạn chót của mục tiêu trong khảo sát; mục tiêu vẫn đi qua gợi ý mục tiêu hiện có (`goal_templates`).
- Màn báo cáo cuối kỳ.

**Tuyệt đối tránh**
- LLM sinh ra số tiền. Mọi con số do code tính; AI chỉ chọn phương pháp (như hiện tại).
- Tự tạo hạn mức khi người dùng chưa bấm áp dụng.
- Viết cứng danh mục, khoảng %, tỉ lệ nhóm trong code: tất cả nằm trong DB, admin sửa được.

## 3. Các hướng đã cân nhắc

| Hướng | Mô tả | Đánh đổi |
|---|---|---|
| **1. Code tính theo luật phương pháp (CHỌN)** | Mỗi phương pháp có bảng "nhóm % → danh mục"; code so chi khai báo với trần của nhóm, co lại nếu vượt, phần dư vào tiết kiệm | Số đúng, kiểm thử được, phương pháp quyết định kế hoạch. Phải cấu hình nhóm cho 13 phương pháp |
| 2. AI đề xuất số từng danh mục | Backend chỉ kiểm tổng | Linh hoạt nhưng số khó kiểm, ngược nguyên tắc "code tính tiền", va bộ kiểm chứng của agent |
| 3. Dùng lại `plan_budget` của agent | Lấy tỉ lệ khảo sát làm nguồn chia | Nhanh nhưng không biết phương pháp và khoản cố định → mọi phương pháp ra kế hoạch như nhau |

Người dùng chọn hướng 1.

## 4. Khảo sát: phần số liệu mới

Cách trả lời (đã chốt với người dùng): **thu nhập và khoản cố định nhập số**; **chi linh hoạt theo danh mục chọn khoảng theo % thu nhập**, app hiện khoảng ra số tiền.

### 4.1 Thay đổi dữ liệu
- `survey_questions.answer_type`: `choice` (mặc định, như cũ) | `amount`.
- `survey_questions.category_slug` (nullable): câu hỏi này nói về danh mục hệ thống nào. Plan dùng cột này để biết câu nào là chi của danh mục nào — không dò theo mã câu hỏi.
- `survey_questions.plan_role` (nullable): vai trò trong kế hoạch: `income` | `fixed` | `flexible` | `savings_balance`. Câu không có vai trò (10 câu cũ) chỉ phục vụ chọn phương pháp.
- `survey_options.share_min_pct`, `share_max_pct` (nullable, số nguyên): khoảng % thu nhập của đáp án, dùng cho câu `flexible`. Plan dùng **điểm giữa**; đáp án cuối có thể chỉ có `share_min_pct` → dùng `share_min_pct` (cách của builder, ghi lại trong code).
- `user_survey_answers.amount_vnd` (bigint, nullable): đáp án của câu `amount`; khi đó `option_code` rỗng. Kiểm 0 ≤ x ≤ 100 tỷ ở server.
- Validator admin: `amount` không có đáp án; `flexible` phải có `category_slug` trỏ tới danh mục hệ thống đang bật và mọi đáp án có khoảng %; `fixed` có thể không có `category_slug` (trả nợ không có danh mục chi).

### 4.2 Câu hỏi seed (admin sửa/tắt được, seeder không ghi đè)
Nhóm mới `money` đứng **sau** các câu hiện có.

| Mã | Loại | Vai trò | Danh mục | Nội dung |
|---|---|---|---|---|
| `income_amount` | amount | income | — | Mỗi tháng bạn thực nhận khoảng bao nhiêu? |
| `fixed_housing` | amount | fixed | housing | Tiền nhà / thuê trọ mỗi tháng |
| `fixed_bills` | amount | fixed | bills | Điện, nước, internet, điện thoại |
| `fixed_debt` | amount | fixed | — | Trả góp / trả nợ mỗi tháng |
| `flex_food` … | choice | flexible | food, transport, shopping, entertainment, health, education, family | Bạn thường chi cho *X* khoảng bao nhiêu? |
| `savings_balance` | amount | savings_balance | — | Hiện bạn đã có bao nhiêu tiền tiết kiệm? |

Đáp án mặc định cho câu flexible: `0%` / `<5%` / `5–10%` / `10–20%` / `20–30%` / `≥30%` (`share_*` theo đó) + "bỏ qua". Các câu nhập số đều bỏ qua được; bỏ qua `income_amount` thì **không tạo được kế hoạch** (app báo và mời nhập thu nhập).

Khi gửi khảo sát, `income_amount` ghi đè `users.monthly_income_cents`.

**Lưu trữ (người dùng chốt 2026-11-23):** các câu trả lời số tiền lưu trên server như mọi câu khảo sát khác — mở rộng ngoại lệ "khảo sát lưu server" của kiến trúc local-first. Vẫn áp quy tắc chung: không log số tiền, không gửi số tiền sang ai-service (AI chỉ nhận signals như hiện tại), `DELETE /survey` xóa cả `amount_vnd`.

Câu cũ `avg_monthly_spend` giữ nguyên (`PreliminaryCapacityService` đang dùng); plan không đọc nó.

### 4.3 Mobile (`FinMate_FE`, feature `survey`)
- Ô nhập số tiền (định dạng nghìn, bàn phím số).
- Câu flexible: chip hiện **số tiền** = thu nhập × khoảng % (ví dụ thu 10tr → "1–2tr"). Nếu thu nhập bị bỏ qua thì hiện %.
- Các câu nhóm `money` vẽ trên **một màn**, không mỗi câu một màn, để khảo sát không dài thêm nhiều bước.

## 5. Phương pháp: bảng phân bổ

Cột mới `finance_methods.allocation` (jsonb), admin sửa được:

```json
[
  {"key":"needs_pct","label":"Nhu cầu","kind":"spend","categories":["housing","bills","food","transport","health","family"]},
  {"key":"wants_pct","label":"Mong muốn","kind":"spend","categories":["shopping","entertainment","education"]},
  {"key":"goals_pct","label":"Mục tiêu","kind":"save","categories":[]}
]
```

- `key` là một tham số `percent` có trong `param_defs` của chính phương pháp → % lấy từ **giá trị hiệu lực** (mặc định của phương pháp hoặc người dùng đã sửa), không lặp số trong `allocation`.
- `key` = `null` với `kind: "spend"` nghĩa là "phần còn lại": nhóm không có trần, giữ nguyên chi khai báo.
- Danh mục không thuộc nhóm nào → coi như thuộc nhóm "phần còn lại".
- Validator: mỗi danh mục thuộc tối đa một nhóm; `key` khác null phải là tham số percent của phương pháp; nhóm `save` bắt buộc có `key` và không có danh mục; nhiều nhất một nhóm `spend` có `key = null`. Hũ "Cho đi" của 6 hũ là nhóm `save` (tiền để riêng, không chi theo danh mục).

Phương pháp không có tham số chia % (Ghi chép tối thiểu, Tiền khả dụng, Dòng tiền, Phong bì, Zero-based, Quỹ khẩn cấp, Trả nợ, Tự động hóa) dùng một nhóm spend `key:null` chứa mọi danh mục + (nếu có) một nhóm save trỏ tới tham số để dành của nó (`saving_pct`, `contribution_pct`). Seeder điền `allocation` cho cả 13 phương pháp; với DB đã có (seeder không ghi đè) thì điền qua admin API như cách đổi tên phương pháp trước đây.

Khối `jars` trên trang chủ đang tự giữ danh sách danh mục riêng. Bản này **không** đụng tới nó; gộp về `allocation` là việc sau (ghi `ponytail:` ở chỗ đọc).

## 6. Thuật toán (code, tất định)

Đầu vào: thu nhập `I`; cố định `F_c` (theo danh mục) và `F_debt`; chi khai báo `D_c = I × điểm giữa %` (làm tròn xuống nghìn); phương pháp + tham số hiệu lực.

1. Với mỗi nhóm `spend` có trần: `cap = I × pct`. Tổng nhóm = cố định của danh mục trong nhóm + chi khai báo.
   - Tổng ≤ cap: giữ nguyên chi khai báo (**không** đẩy người dùng tiêu nhiều lên cho đủ trần).
   - Tổng > cap: co **chỉ phần chi linh hoạt** theo tỉ lệ để vừa trần. Nếu riêng cố định đã vượt trần: linh hoạt về 0 và gắn cảnh báo `fixed_exceeds_group`.
2. Nhóm `spend` không trần: giữ nguyên.
3. `planned_spend` = tổng các danh mục sau bước 1–2 + `F_debt`.
4. `surplus = I − planned_spend`. Nhóm `save`: `save_target = I × pct`. Nếu `surplus < save_target` → cảnh báo `below_save_target` (kèm còn thiếu bao nhiêu).
5. `surplus < 0` (chi cố định đã vượt thu) → vẫn trả kế hoạch, cờ `deficit`; app hiện thông điệp ổn định dòng tiền thay vì mục tiêu tiết kiệm.
6. Làm tròn xuống nghìn ở từng danh mục; phần lẻ dồn vào `surplus` (tổng luôn khớp `I`).

Kết quả cho mỗi danh mục: `declared_vnd`, `limit_vnd`, `reduced_by_vnd`, `group_key`. Tổng: `income`, `fixed`, `planned_spend`, `surplus`, `save_target`, `warnings[]`.

Code nằm ở `FinMate.Application/FinanceMethods/` (cạnh `GoalPlanningService`), là hàm thuần nhận dữ liệu và trả kết quả, để test bằng số.

## 7. API

- `GET /api/v1/me/financial-plan/draft` — tính từ câu trả lời khảo sát + phương pháp chính **đang dùng** của người dùng. Không lưu. Chưa chọn phương pháp → lỗi nghiệp vụ `FINANCE_PLAN_NO_ACTIVE_METHOD`; chưa có thu nhập → `FINANCE_PLAN_NO_INCOME` (cùng kiểu `BusinessRuleException` và mã HTTP như các lỗi nghiệp vụ khác). Thu nhập đọc từ `users.monthly_income_cents`, được câu `income_amount` ghi vào.
- Áp dụng: **dùng lại** `POST /api/v1/forecast/categories/apply` (Phase 56, `items: [{categoryId, limitCents}]`): danh mục chưa có ngân sách → tạo và áp ngay; đã có → áp từ tháng sau. Người mới chưa có ngân sách nên áp ngay. Draft trả kèm `categoryId` để app gửi thẳng. Không viết API áp dụng thứ hai. Dòng `limit = 0` app không gửi.

Mobile: màn "Kế hoạch tháng của bạn" trong feature `finance_methods`, mở ngay sau khi chọn phương pháp gợi ý: dòng tổng (thu → cố định → chi → **dư**), danh sách danh mục (khai báo → hạn mức, ghi chú nếu bị co), sửa từng dòng thì số dư tính lại ngay trên máy, nút "Áp dụng" và "Để sau". Mở lại được từ trang chủ phương pháp.

## 8. Admin web (`FinMate_Admin`)
- Câu hỏi khảo sát: chọn loại (`choice`/`amount`), vai trò, danh mục; đáp án có khoảng %.
- Phương pháp: trình sửa `allocation` (nhóm, tham số %, danh mục).

## 9. Kiểm thử
- Hàm tính kế hoạch: nhóm dưới trần / vượt trần / cố định vượt trần / thâm hụt / làm tròn tổng khớp thu nhập; một ca cho mỗi kiểu phương pháp (50/30/20, 6 hũ, có tham số để dành, không có).
- Validator `allocation` và câu hỏi mới.
- Draft: `409 no_active_method` / `no_income`; trả `categoryId` đúng danh mục hệ thống.

## 10. Để builder quyết
- Tên migration/cột cụ thể, cách đặt DTO.
- Thứ tự hiển thị danh mục trên màn kế hoạch.
- Nội dung chữ của cảnh báo (đặt trong DB/config như các văn bản khác, không viết cứng ở app).
