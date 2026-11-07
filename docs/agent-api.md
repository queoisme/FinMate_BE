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
| `simulate_purchase` | Số dư thấp nhất trong kỳ trước và sau khi mua | thiếu khoản định kỳ đã xác nhận → kết luận tối đa `tight` |
| `runway` | Số dư đủ chi tiêu bao nhiêu tháng nếu thu nhập dừng | `history_too_short`, `no_spending_history` |
| `stress_test` | Thu nhập giảm X% trong N tháng thì sao | `history_too_short` |
| `sensitivity` | Yếu tố nào làm kết luận mục tiêu đổi nhiều nhất | `history_too_short` |

## Các API đi kèm (JWT của chính người dùng)

| API | Mô tả |
|---|---|
| `GET/PUT/DELETE /api/v1/agent/memory[/{key}]` | Sở thích dài hạn (khóa cho phép: `preferred_method`, `eating_out_weekends`, `notification_quiet_hours`, `communication_tone`; tối đa 200 ký tự) |
| `GET/POST /api/v1/agent/recurring`, `DELETE /api/v1/agent/recurring/{id}` | Khoản thu/chi định kỳ đã xác nhận; `GET` kèm `suggestions` (đề xuất từ lịch sử, **chưa lưu** — xác nhận bằng `POST`) |
| `GET/POST /api/v1/support/tickets`, `POST .../{id}/resolve`, `POST .../{id}/reopen` | Khiếu nại của chính người dùng (trạng thái `Open → Triaged → Responded → Resolved`, phản đối thì `Reopened`) |

## API cho nhân sự (xem `docs/rbac-matrix.md`)

- `/api/v1/admin/assumptions` — giả định kinh tế (ContentStaff soạn, Manager trở lên duyệt / gỡ).
- `/api/v1/admin/knowledge` — tài liệu tri thức và câu hỏi chưa có tri thức (cùng phân quyền).
- `/api/v1/admin/support/tickets` — hàng đợi khiếu nại (SupportStaff trở lên; `review`, `decide` chỉ Manager trở lên; `assist` chỉ gợi ý, không tự gửi).

## Chỉ dùng khi phát triển

`POST /api/v1/internal/agent/run` (chạy agent trên hồ sơ giả lập) chỉ tồn tại ở môi trường Development khi `AGENT_EVAL_ENABLED=true` và cần header `X-Eval-Key`; ngoài Development bật cờ này thì backend **từ chối khởi động**. Không thuộc hợp đồng với app.

## Quyền riêng tư

Câu hỏi và bằng chứng được gửi tới nhà cung cấp LLM bên thứ ba (Gemini gói trả phí; không dùng gói miễn phí cho dữ liệu thật vì gói đó dùng dữ liệu để cải thiện sản phẩm). Người dùng đã chấp nhận ở bước chính sách quyền riêng tư; nhà cung cấp dự phòng chỉ hoạt động khi chủ dự án đặt `AGENT_BACKUP_MODEL`. Không log câu hỏi, số tiền hay khóa.
