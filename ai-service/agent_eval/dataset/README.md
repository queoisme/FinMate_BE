# Bộ câu hỏi đánh giá agent

`questions.jsonl`: mỗi dòng một câu `{id, text, segment, kind, expect{in_scope, tools[], must_cite[], clarify, numeric_truth, refusal_reason}}`; schema được `tests/unit/test_agent_dataset.py` giữ.

| `kind` | Mong đợi | `numeric_truth` |
|---|---|---|
| `forecast`, `slang_amount` | chạy `forecast_goal` | `{price_vnd, horizon_months}` |
| `sensitivity` | chạy `sensitivity` | `{price_vnd, horizon_months}` |
| `stress` | chạy `stress_test` | `{months, income_drop_percent}` |
| `runway` | chạy `runway` | `null` |
| `spending` | chạy `spending_summary` (chi theo nhóm 3 tháng gần nhất của hồ sơ, `snapshot.category_spend`); vài câu hỏi dữ liệu người khác → từ chối `other_users_data` | `{months}` (luôn 3: nguồn giả lập bỏ qua khoảng ngày) hoặc `null` khi từ chối |
| `budget_plan` | chạy `plan_budget`; thiếu mục tiêu tiết kiệm → hỏi lại, không chạy công cụ | `{income_vnd, save_vnd}`, `{save_vnd}` khi câu không nêu thu nhập, `null` khi hỏi lại |
| `knowledge` | chạy `search_knowledge` hoặc `get_assumptions` | `null` |
| `clarify` | hỏi lại (thiếu giá hoặc mục tiêu), không chạy công cụ | `null` |
| `out_of_scope`, `adversarial` | từ chối với `refusal_reason` | `null` |

- Giá trong nhãn phải đọc ra được bằng bộ đọc số tất định của dự án (`app/services/amount_normalizer.find_amounts`); test kiểm điều này, nên không ghi câu mà bộ đọc không hiểu ("nửa tỷ" chưa được hỗ trợ).
- 60 câu đầu viết tay; phần còn lại ghép từ mẫu câu với tham số cố định, nhãn lấy từ chính tham số. Một vài câu ghép nghe hơi máy móc (ví dụ "mua tiền cưới"): chấp nhận được cho mục đích đo, đừng dùng làm ví dụ giao diện.
- Chưa có nhóm `purchase` (`simulate_purchase`): công cụ này đọc dữ liệu thật qua `IPurchaseCheckService`, nên endpoint đánh giá (người dùng ngẫu nhiên, dữ liệu giả lập) không đo được. Kiểm bằng thử tay (`docs/agent-manual-qa.md`).
- `budget_plan` có hai câu mục tiêu ≥ thu nhập (`bp-23`, `bp-24`): công cụ chạy nhưng trả `insufficient_data` (`goal_exceeds_income`). Schema chưa có trường "trạng thái mong đợi", và `tool_accuracy` chỉ đếm bước `Ok`, nên hai câu này luôn bị tính trượt công cụ dù agent làm đúng; `plan_accuracy` vẫn đúng (thiếu dữ liệu tính là trả lời).
- Thêm câu: giữ `id` duy nhất theo tiền tố của nhóm, `text` không trùng, `segment` là một trong `stable_worker`, `freelancer`, `student`.
