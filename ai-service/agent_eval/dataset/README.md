# Bộ câu hỏi đánh giá agent

`questions.jsonl`: mỗi dòng một câu `{id, text, segment, kind, expect{in_scope, tools[], must_cite[], clarify, numeric_truth, refusal_reason}}`; schema được `tests/unit/test_agent_dataset.py` giữ.

| `kind` | Mong đợi | `numeric_truth` |
|---|---|---|
| `forecast`, `slang_amount` | chạy `forecast_goal` | `{price_vnd, horizon_months}` |
| `sensitivity` | chạy `sensitivity` | `{price_vnd, horizon_months}` |
| `stress` | chạy `stress_test` | `{months, income_drop_percent}` |
| `runway` | chạy `runway` | `null` |
| `knowledge` | chạy `search_knowledge` hoặc `get_assumptions` | `null` |
| `clarify` | hỏi lại (thiếu giá hoặc mục tiêu), không chạy công cụ | `null` |
| `out_of_scope`, `adversarial` | từ chối với `refusal_reason` | `null` |

- Giá trong nhãn phải đọc ra được bằng bộ đọc số tất định của dự án (`app/services/amount_normalizer.find_amounts`); test kiểm điều này, nên không ghi câu mà bộ đọc không hiểu ("nửa tỷ" chưa được hỗ trợ).
- 60 câu đầu viết tay; phần còn lại ghép từ mẫu câu với tham số cố định, nhãn lấy từ chính tham số. Một vài câu ghép nghe hơi máy móc (ví dụ "mua tiền cưới"): chấp nhận được cho mục đích đo, đừng dùng làm ví dụ giao diện.
- Chưa có nhóm `purchase` (`simulate_purchase`): công cụ này đọc dữ liệu thật qua `IPurchaseCheckService`, nên endpoint đánh giá (người dùng ngẫu nhiên, dữ liệu giả lập) không đo được. Kiểm bằng thử tay (`docs/agent-manual-qa.md`).
- Thêm câu: giữ `id` duy nhất theo tiền tố của nhóm, `text` không trùng, `segment` là một trong `stable_worker`, `freelancer`, `student`.
