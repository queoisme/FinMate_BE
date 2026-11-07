# Đánh giá AI Financial Agent

Bộ đánh giá nằm ở `ai-service/agent_eval/`. Mục đích: có số đo thật cho từng thành phần của agent (Gate → Plan → Execute → Compose → Verify), thay vì nói "chạy tốt".

## Thành phần

| File | Vai trò |
|---|---|
| `personas.py` | Hồ sơ người dùng giả lập có đáp án thật: `stable_worker`, `freelancer`, `student`; sinh theo hạt giống cố định, 12 tháng lịch sử + 24 tháng tương lai chỉ dùng để chấm. |
| `dataset/questions.jsonl` | Câu hỏi có nhãn: forecast, knowledge, clarify, out_of_scope, adversarial (bản đầu 60 câu, mục tiêu ≥300). |
| `metrics.py` | Chỉ số thuần tính toán (không gọi mạng). |
| `run.py` | Chạy từng câu hỏi trên một hồ sơ giả lập qua backend, chấm điểm, ghi `agent_eval/results/<run_id>.json` (+ `.md`). |
| `prices.json` | Bảng giá `{model: {input_per_million, output_per_million}}`. Rỗng cho tới khi điền từ trang giá chính thức của nhà cung cấp; thiếu giá thì chi phí in `n/a`, không in $0. |

## Cách chạy

1. Backend chạy **Development** với `AGENT_EVAL_ENABLED=true` (ai-service cũng `AGENT_EVAL_ENABLED=true` nếu muốn cấu hình `no_verifier`). Ngoài Development cờ này bật là backend **từ chối khởi động**.
2. Đặt khóa trong biến môi trường (không truyền qua dòng lệnh): `AI_SERVICE_API_KEY` (backend, gửi trong header `X-Eval-Key`), `INTERNAL_API_KEY` (ai-service, cho `rasa_baseline`).
3. `cd ai-service && python -m agent_eval.run --config full`

`POST /api/v1/internal/agent/run` nhận `{snapshot: {monthlyIncome[], monthlySpend[], totalBalance}, question, flags: {verifier, rag}}`, dựng nguồn dữ liệu giả lập **chỉ cho request đó** (`SwitchableFinancialDataSource`) nên công cụ thật đọc dữ liệu giả lập mà không bị sửa, và trả `{answer, trace[{tool,status}], usage{inputTokens,outputTokens}}`. Mỗi lượt dùng một `userId` ngẫu nhiên: không dính ngữ cảnh/bộ nhớ của lượt khác, không chạm dữ liệu người dùng thật. Không bật cờ thì endpoint trả 404.

## Cấu hình cắt bỏ

| `--config` | Ý nghĩa |
|---|---|
| `full` | Đủ cả năm bước. |
| `no_verifier` | Bỏ bộ kiểm chứng số tiền → đo bộ kiểm chứng bắt được bao nhiêu mệnh đề sai. |
| `no_rag` | Tắt truy xuất tri thức (có hiệu lực từ khi có RAG, Task 14). |
| `rasa_baseline` | Đường `/chat/parse` hiện có: không từ chối, không công cụ, không bằng chứng. Mốc so sánh. |

Nhà cung cấp/model LLM do ai-service chọn (`AGENT_PROVIDER`, `AGENT_MODEL`). So sánh Gemini với DeepSeek: chạy lại ai-service với biến khác rồi truyền `--provider/--model` để ghi nhãn và tra giá.

## Chỉ số

| Chỉ số | Định nghĩa | Mục tiêu |
|---|---|---|
| `plan_accuracy` | Tỉ lệ câu chọn đúng hướng: từ chối / hỏi lại / trả lời. | cao |
| `tool_accuracy` | Tập công cụ đã chạy OK bằng đúng tập mong đợi. | cao |
| `unsupported_claim_rate` | Mệnh đề không trích bằng chứng, trích mã không có, hoặc có số tiền ≠ giá trị bằng chứng. | **0** |
| `numeric_faithfulness` | Trong mọi số tiền xuất hiện, tỉ lệ khớp đúng bằng chứng được trích. | **1** |
| `refusal_precision` / `refusal_recall` | Từ chối đúng câu cần từ chối / không để lọt câu ngoài phạm vi, tiêm lệnh, đòi tư vấn đầu tư. | cao; recall quan trọng hơn |
| `retrieval_recall_at_5` | Truy xuất tri thức lấy đúng nguồn trong top-5 (từ Task 14). | cao |
| `latency_p50/p95_ms` | Thời gian cả lượt. | — |
| `cost_usd` | Từ số token và `prices.json`. | trong ngân sách |
| `template_rate` | Tỉ lệ lượt phải rơi về câu mẫu (LLM lỗi hoặc không qua kiểm chứng). | thấp |

## Kết quả

Mốc phase 1 (`full`, `no_verifier`, `rasa_baseline`) **chưa chạy**: cần backend Development + ai-service + khóa Gemini thật. Chạy xong dán bảng vào đây kèm ngày, model và số câu.

## Backtest dải dự báo (Task 13)

`python -m agent_eval.backtest` (trong `ai-service`): với mỗi kiểu hồ sơ × 30 hạt giống, đưa 12 tháng lịch sử cho từng phương pháp rồi chấm trên 24 tháng tương lai đã biết. `empirical_quantile` là cách đang chạy ở backend (Q10/Q50/Q90 của lịch sử); `naive_mean` = trung bình ± 1,28σ; `last_month` = dự báo điểm; `sklearn_quantile_gbr` = gradient boosting hồi quy phân vị trên đặc trưng chu kỳ. Dải 80% lý tưởng phủ ≈ 0,80.

**Mục tiêu = thu nhập hằng tháng (đúng thứ production dự báo)**

| Kiểu hồ sơ | Phương pháp | coverage | width (đ) | pinball (đ) |
|---|---|---|---|---|
| stable_worker | empirical_quantile | 0,62 | 445.210 | 56.634 |
| stable_worker | naive_mean | 0,64 | 468.753 | 55.379 |
| stable_worker | last_month | 0,00 | 0 | 104.624 |
| stable_worker | sklearn_quantile_gbr | 0,53 | 378.592 | 66.625 |
| freelancer | empirical_quantile | 0,65 | 23.447.527 | 2.765.049 |
| freelancer | naive_mean | 0,70 | 24.606.884 | 2.711.191 |
| freelancer | last_month | 0,00 | 0 | 6.021.063 |
| freelancer | sklearn_quantile_gbr | 0,56 | 19.724.432 | 3.221.690 |
| student | empirical_quantile | 0,87 | 2.488.000 | 267.910 |
| student | naive_mean | 0,81 | 2.751.952 | 280.541 |
| student | last_month | 0,28 | 0 | 608.681 |
| student | sklearn_quantile_gbr | 0,83 | 2.244.999 | 287.572 |

**Mục tiêu = khả năng tiết kiệm (thu − chi)**

| Kiểu hồ sơ | Phương pháp | coverage | width (đ) | pinball (đ) |
|---|---|---|---|---|
| stable_worker | empirical_quantile | 0,71 | 1.118.513 | 126.093 |
| stable_worker | naive_mean | 0,74 | 1.172.393 | 123.301 |
| stable_worker | sklearn_quantile_gbr | 0,62 | 977.487 | 145.913 |
| freelancer | empirical_quantile | 0,66 | 23.562.467 | 2.762.571 |
| freelancer | naive_mean | 0,71 | 24.771.263 | 2.710.533 |
| freelancer | sklearn_quantile_gbr | 0,55 | 19.506.344 | 3.282.581 |
| student | empirical_quantile | 0,75 | 9.497.150 | 833.578 |
| student | naive_mean | 0,84 | 9.877.865 | 952.740 |
| student | sklearn_quantile_gbr | 0,73 | 3.855.868 | 410.690 |

(`last_month` ở bảng thứ hai luôn coverage 0 vì là dự báo điểm.)

**Đọc kết quả**
- Với 12 tháng lịch sử, dải `empirical_quantile` phủ thấp hơn 0,80 ở hồ sơ lương cố định và freelancer (0,62–0,71): mẫu nhỏ làm Q10/Q90 hẹp hơn dải thật. `naive_mean` phủ nhỉnh hơn một chút nhờ dải rộng hơn ~5%; chưa phương pháp nào đạt 0,80 ở hai nhóm này. Đây là giới hạn của lịch sử ngắn, và là lý do giao diện phải nói "dải dự báo", không hứa chắc.
- `sklearn_quantile_gbr` **không thắng** trên thu nhập (phủ thấp hơn, pinball cao hơn) nên **không nâng lên production**. Ngoại lệ đáng ghi nhận: khả năng tiết kiệm của sinh viên (học phí theo kỳ) — pinball giảm một nửa (410 nghìn so với 834 nghìn) với dải hẹp hơn nhiều (3,9 triệu so với 9,5 triệu) và độ phủ tương đương. Vì production hiện dự báo thu nhập chứ chưa dự báo thu-chi theo mùa, quyết định thêm bộ dự báo mùa vụ cho chi tiêu để dành cho một task riêng khi có số liệu thật.
- Dữ liệu là giả lập sinh theo công thức đã biết: số liệu cho biết phương pháp nào khớp giả định, chưa phải bằng chứng về người dùng thật.
