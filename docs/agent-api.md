# AI Financial Agent — hợp đồng API cho app

**Trạng thái:** đã triển khai ở backend và ai-service; chưa có phía app (FE là repo riêng). Số liệu đánh giá thật: xem `docs/agent-eval.md`.

## Nguyên tắc

1. **LLM không bao giờ sinh số tiền.** Mọi số tiền trong câu trả lời là số nguyên đồng VND và bằng đúng giá trị của một bằng chứng mà mệnh đề đó trích dẫn; bộ kiểm chứng là code thuần Python, không gọi LLM. Vi phạm → sinh lại một lần → rơi về câu mẫu dựng từ bằng chứng.
2. **Mọi dữ kiện có nguồn, ngày và độ tin cậy.** Độ tin cậy = độ đầy đủ × độ mới, bị chặn trần theo nguồn; nó **không** là xác suất thống kê và UI không được hiển thị như xác suất.
3. **Không đoán.** Thiếu dữ liệu → `insufficient_data` kèm lý do. Ngoài phạm vi tài chính cá nhân của chính người dùng → `refused`.
4. **`userId` chỉ lấy từ JWT.** ai-service không bao giờ thấy id thật (chỉ SHA-256) và không nhận câu hỏi qua log.
5. **Agent chỉ đề xuất.** Không tự tạo giao dịch, mục tiêu hay thay đổi dữ liệu.

## `POST /api/v1/agent/ask`

Yêu cầu (JWT bắt buộc): `{ "question": "Mua xe 600 triệu sau 2 năm được không?" }` — 1–500 ký tự.

Hạn mức: 20 lượt/phút/người dùng (`RATE_LIMIT_AGENT_PER_MINUTE`), vượt thì 429.

Lỗi: 422 `invalid_question`; 503 khi ai-service không dùng được **trước khi** có dữ liệu để trả lời. Nếu ai-service sập sau khi đã có bằng chứng thì vẫn trả 200 với `usedTemplate: true` (câu mẫu dựng từ bằng chứng).

### `AgentAnswerDto`

```json
{
  "status": "answered",
  "message": null,
  "claims": [{ "text": "Bạn đang có 100.000.000 đồng.", "evidenceIds": ["balance"] }],
  "evidence": [
    { "id": "balance", "key": "total_balance", "valueVnd": 100000000, "text": "Tổng số dư các tài khoản",
      "unit": "VND", "source": "confirmed", "asOf": "2026-11-07T10:00:00+07:00", "confidence": 1.0,
      "sourceName": null, "sourceUrl": null }
  ],
  "verdict": "tight",
  "traceId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
  "usedTemplate": false
}
```

| Trường | Ý nghĩa |
|---|---|
| `status` | `answered` (có `claims` + `evidence`), `clarify` (hỏi lại `message`), `refused` (ngoài phạm vi / tư vấn đầu tư / dữ liệu người khác / tiêm lệnh; `message` là câu từ chối), `insufficient_data` (`message` nói thiếu gì) |
| `claims[]` | Từng mệnh đề của câu trả lời. `evidenceIds` luôn nằm trong `evidence[]` của chính lượt này |
| `evidence[]` | Toàn bộ bằng chứng của lượt, để màn "Vì sao?" liệt kê từng dòng |
| `evidence[].source` | `confirmed` (đã xác nhận), `user_entered` (người dùng nhập), `estimated` (ước tính), `document` (tài liệu / giả định đã duyệt) |
| `evidence[].valueVnd` | Số nguyên đồng; `null` khi bằng chứng chỉ là chữ (đoạn tài liệu, số tháng) |
| `verdict` | Chỉ với câu hỏi mục tiêu / mua / chịu đựng: `feasible`, `tight`, `infeasible`; không có thì `null`. Không dùng từ "chắc chắn" hay "nên mua" |
| `traceId` | Mã vết của lượt (không chứa nội dung) |
| `usedTemplate` | `true` = câu trả lời dựng bằng mẫu cố định từ bằng chứng (ai-service lỗi hoặc không qua kiểm chứng) |
| `suggestedActions[]` | Thẻ đề xuất hành động (luôn có mảng, rỗng khi không có gì để đề xuất; tối đa 8). Xem mục "Thẻ đề xuất hành động" bên dưới |

### Thẻ đề xuất hành động

Mỗi phần tử: `{kind, title, amountVnd, date, categorySlug, categoryName, evidenceIds}`. Thẻ do **code của công cụ** sinh từ dữ liệu có cấu trúc, **không do LLM viết**: `title` và mọi con số lấy từ bằng chứng của chính lượt này (`evidenceIds` chỉ tới chúng nên màn "Vì sao?" mở được nguồn), nên thẻ không thể mang một con số không có nguồn. Thẻ chỉ là **đề xuất**: app hiện nút, người dùng bấm xác nhận thì app mới gọi API sẵn có; agent không tự đổi dữ liệu nào.

| `kind` | Khi nào có | Trường dùng | App làm gì khi người dùng bấm |
|---|---|---|---|
| `wait_until` | Khoản mua chưa ổn (`verdict` khác `feasible`) và có ngày an toàn sau ngày mua | `date` (`yyyy-MM-dd`) | Hỏi lại "Mua món này có ổn không?" với ngày đó (`/forecast/purchase-check`) hoặc đặt nhắc |
| `lower_amount_to` | Khoản mua chưa ổn và mức tối đa còn giữ được vùng đệm thấp hơn giá | `amountVnd` | Điền lại giá bằng `amountVnd` rồi hỏi lại |
| `set_budget_limit` | Kết quả chia ngân sách (`plan_budget`): mỗi nhóm một thẻ, không có thẻ cho nhóm "Các nhóm khác" | `categorySlug`, `categoryName`, `amountVnd` | Tạo hoặc sửa hạn mức tháng của nhóm đó (`POST`/`PATCH /budgets`); app đổi `categorySlug` sang mã nhóm của người dùng |

Không có thẻ khi `status` khác `answered`, khi khoản mua đã khả thi với dữ liệu đã xác nhận, hoặc khi hoãn/giảm không đổi được gì. Thêm loại thẻ mới = thêm một hằng ở `SuggestedActionKinds` và để công cụ tương ứng sinh ra nó cùng bằng chứng làm căn cứ.

### Thẻ kết quả (gợi ý cho app)

- **Kết luận** ở trên cùng: dịch `verdict` thành "Khả thi / Cần cân nhắc / Khó đạt" kèm kỳ tính; không hiện một "điểm AI" chung chung.
- **Dải** (với dự báo): hiển thị ba kịch bản (bảo thủ, cơ sở, lạc quan) lấy từ các bằng chứng `scenario_*`, `shortfall_*`; luôn ghi đây là dải dự báo, không phải lời hứa.
- **"Vì sao?"**: danh sách `evidence[]`, mỗi dòng gồm nhãn (`text`), giá trị, **nguồn** (`source` + `sourceName`), **ngày cập nhật** (`asOf`) và **độ tin cậy** (`confidence`, làm tròn 2 chữ số). Đánh dấu nhãn `đã xác nhận` / `người dùng nhập` / `ước tính` / `theo tài liệu`.
- Khi `usedTemplate` là `true`, không cần báo lỗi: nội dung vẫn đúng số, chỉ văn phong cố định.

## Công cụ của agent (danh sách cho phép)

| Công cụ | Việc | Thiếu dữ liệu |
|---|---|---|
| `forecast_goal` | Bao lâu đủ tiền mua món giá X trong N tháng, ba kịch bản | dưới 3 tháng lịch sử → `history_too_short` |
| `get_assumptions` | Tra giả định kinh tế đã duyệt (nguồn, ngày) | chưa có → `assumption_missing` |
| `search_knowledge` | Tra tri thức đã duyệt (RAG) | không có đoạn nào → `knowledge_missing` (ghi mã băm câu hỏi) |
| `simulate_purchase` | Số dư thấp nhất trong kỳ trước và sau khi mua; bọc `IPurchaseCheckService` (cùng số với màn "Mua món này có ổn không?") | engine báo `insufficient_data` → `purchase_data_missing`; có dòng ước tính → kết luận tối đa `tight` |
| `runway` | Số dư đủ chi tiêu bao nhiêu tháng nếu thu nhập dừng | `history_too_short`, `no_spending_history` |
| `stress_test` | Thu nhập giảm X% trong N tháng thì sao | `history_too_short` |
| `sensitivity` | Yếu tố nào làm kết luận mục tiêu đổi nhiều nhất | `history_too_short` |
| `spending_summary` | Chi tiêu đã xác nhận theo nhóm trong `months` tháng gần nhất (1–12, **tính cả tháng này** đến hôm nay): tổng chi + tối đa 5 nhóm lớn nhất `cat_1..5` kèm phần trăm (phần trăm chỉ nằm trong chữ, không phải số tiền bằng chứng), các nhóm còn lại gộp thành "Các nhóm khác" `cat_other` | không có chi tiêu trong khoảng → `no_spending_history` |
| `plan_budget` | Chia ngân sách tháng: `save_vnd` (bắt buộc) để dành, phần còn lại của `income_vnd` (tùy chọn; bỏ trống = trung vị thu nhập 12 tháng) chia theo tỉ lệ chi của **chính người dùng** trong 3 tháng trọn gần nhất, không có thì theo mẫu admin `agent.budgetTemplate` trong system config (mẫu không hợp lệ — tổng ≠ 100, trùng slug, không phải số nguyên… — được coi như không có). Chỉ giữ 7 nhóm có tỉ lệ lớn nhất, các nhóm còn lại gộp thành "Các nhóm khác" trước khi chia. Mỗi nhóm làm tròn xuống nghìn đồng, phần dư dồn vào nhóm lớn nhất: tổng các nhóm + tiết kiệm = thu nhập đúng. Bằng chứng (≤ 12): `income`, `save`, `remaining` (thu nhập − tiết kiệm = tổng các nhóm), `alloc_1..7` theo vị trí (key `budget_alloc:<slug>`, cắt cho vừa 64 ký tự) + `alloc_other`, `savings_rate` | tiết kiệm ≥ thu nhập → `goal_exceeds_income`; không nêu thu nhập và lịch sử không cho ra thu nhập (trung vị ≤ 0) → `history_too_short`; không có tỉ lệ của người dùng lẫn mẫu admin → `no_budget_basis` |

## Các API đi kèm (JWT của chính người dùng)

| API | Mô tả |
|---|---|
| `GET/PUT/DELETE /api/v1/agent/memory[/{key}]` | Sở thích dài hạn (khóa cho phép: `preferred_method`, `eating_out_weekends`, `notification_quiet_hours`, `communication_tone`; tối đa 200 ký tự) |
| `GET/POST/... /api/v1/recurring-items` | Khoản thu/chi định kỳ (Phase 54, module `Forecasting`): đề xuất tự phát hiện, người dùng xác nhận. Công cụ `simulate_purchase` của agent đọc chúng qua `IPurchaseCheckService`, không có API riêng của agent |
| `/api/v1/support/tickets` | Khiếu nại của chính người dùng (Phase 62). Agent không có luồng khiếu nại riêng |

## API cho nhân sự (vai trò Staff / OpsManager / Admin của Phase 61–65)

- `/api/v1/admin/assumptions` — giả định kinh tế: Staff trở lên soạn bản nháp, **OpsManager trở lên** (policy `ManagerOrAbove`) duyệt / gỡ.
- `/api/v1/admin/knowledge` — tài liệu tri thức và câu hỏi chưa có tri thức: cùng phân quyền như trên. `GET /documents` là danh sách (không kèm nội dung); `GET /documents/{id}` trả thêm `text` (toàn bộ nội dung) để người duyệt đọc trước khi bấm duyệt.
- Giao diện duyệt trên web quản trị (repo `FinMate_Admin`): menu vận hành **Tri thức AI** (`/ops/knowledge`) và **Giả định kinh tế** (`/ops/assumptions`), dành cho OpsManager và Admin (quyền `DecideApprovals`). Cấu hình tự cập nhật CPI nằm ở trang Admin **Số liệu kinh tế** (`/settings/economic`).
- `POST /api/v1/ops/tickets/{id}/assist` — trợ lý gợi ý cho nhân sự khi xử lý ticket: tóm tắt + bản nháp trả lời, **chỉ gợi ý** (không đổi ticket, không gửi gì cho khách; nhân sự tự gửi qua `/ops/tickets/{id}/messages`). `available: false` khi LLM sập; `verified: false` khi bản nháp bị bỏ vì chứa số tiền không có trong ticket. Ghi chú nội bộ không bao giờ gửi cho mô hình.

## Chỉ dùng khi phát triển

`POST /api/v1/internal/agent/run` (chạy agent trên hồ sơ giả lập) chỉ tồn tại ở môi trường Development khi `AGENT_EVAL_ENABLED=true` và cần header `X-Eval-Key`; ngoài Development bật cờ này thì backend **từ chối khởi động**. Không thuộc hợp đồng với app.

## Quyền riêng tư

Câu hỏi và bằng chứng được gửi tới nhà cung cấp LLM bên thứ ba (Gemini gói trả phí; không dùng gói miễn phí cho dữ liệu thật vì gói đó dùng dữ liệu để cải thiện sản phẩm). Người dùng đã chấp nhận ở bước chính sách quyền riêng tư; nhà cung cấp dự phòng chỉ hoạt động khi chủ dự án đặt `AGENT_BACKUP_MODEL`. Không log câu hỏi, số tiền hay khóa.
