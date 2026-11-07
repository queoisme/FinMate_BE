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
