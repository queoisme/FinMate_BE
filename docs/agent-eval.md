# Đánh giá AI Financial Agent

Bộ đánh giá nằm ở `ai-service/agent_eval/`. Mục đích: có số đo thật cho từng thành phần của agent (Gate → Plan → Execute → Compose → Verify), thay vì nói "chạy tốt".

## Thành phần

| File | Vai trò |
|---|---|
| `personas.py` | Hồ sơ người dùng giả lập có đáp án thật: `stable_worker`, `freelancer`, `student`; sinh theo hạt giống cố định, 12 tháng lịch sử + 24 tháng tương lai chỉ dùng để chấm. |
| `dataset/questions.jsonl` | Câu hỏi có nhãn: forecast, knowledge, clarify, out_of_scope, adversarial, slang_amount, runway, stress, sensitivity, spending (`sp-*`), budget_plan (`bp-*`) — 370 câu (319 + 51 câu `spending`/`budget_plan`). |
| `metrics.py` | Chỉ số thuần tính toán (không gọi mạng). |
| `run.py` | Chạy từng câu hỏi trên một hồ sơ giả lập qua backend, chấm điểm, ghi `agent_eval/results/<run_id>.json` (+ `.md`). |
| `prices.json` | Bảng giá `{model: {input_per_million, output_per_million}}`. Rỗng cho tới khi điền từ trang giá chính thức của nhà cung cấp; thiếu giá thì chi phí in `n/a`, không in $0. |

## Cách chạy

1. Backend chạy **Development** với `AGENT_EVAL_ENABLED=true` (ai-service cũng `AGENT_EVAL_ENABLED=true` nếu muốn cấu hình `no_verifier`). Ngoài Development cờ này bật là backend **từ chối khởi động**.
2. Đặt khóa trong biến môi trường (không truyền qua dòng lệnh): `AI_SERVICE_API_KEY` (backend, gửi trong header `X-Eval-Key`), `INTERNAL_API_KEY` (ai-service, cho `chat_parse_baseline`).
3. `cd ai-service && python -m agent_eval.run --config full`

`POST /api/v1/internal/agent/run` nhận `{snapshot: {monthlyIncome[], monthlySpend[], totalBalance}, question, flags: {verifier, rag}}`, dựng nguồn dữ liệu giả lập **chỉ cho request đó** (`SwitchableFinancialDataSource`) nên công cụ thật đọc dữ liệu giả lập mà không bị sửa, và trả `{answer, trace[{tool,status}], usage{inputTokens,outputTokens}}`. Mỗi lượt dùng một `userId` ngẫu nhiên: không dính ngữ cảnh/bộ nhớ của lượt khác, không chạm dữ liệu người dùng thật. Không bật cờ thì endpoint trả 404.

## Cấu hình cắt bỏ

| `--config` | Ý nghĩa |
|---|---|
| `full` | Đủ cả năm bước. |
| `no_verifier` | Bỏ bộ kiểm chứng số tiền → đo bộ kiểm chứng bắt được bao nhiêu mệnh đề sai. |
| `no_rag` | Tắt truy xuất tri thức (có hiệu lực từ khi có RAG, Task 14). |
| `chat_parse_baseline` | Đường `/chat/parse` hiện có: không từ chối, không công cụ, không bằng chứng. Mốc so sánh. |

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

Kế hoạch đo: `docs/superpowers/plans/2026-11-08-ai-agent-measurement.md`.

### Lần chạy thật đầu tiên — 2026-11-08, `full`, `gemini-3.1-flash-lite`, 60 câu của bộ v1

Backend là mã đã merge (chạy trên máy, DB riêng `finmate_eval`), ai-service dùng khóa Gemini trong `ai-service/.env`. Khóa này bị giới hạn tốc độ rất thấp (429 ngay ở lượt thứ hai), nên chạy với `--pause-seconds 15`; không nghỉ thì câu rơi về câu mẫu vì 429 và số đo nhiễu. Dữ liệu là hồ sơ giả lập.

| Chỉ số | Giá trị |
|---|---|
| Câu trả lời được / lỗi | 54 / 6 |
| plan_accuracy | 1,00 |
| tool_accuracy | 0,83 |
| unsupported_claim_rate | 0,00 |
| numeric_faithfulness | 1,00 |
| refusal_precision / recall | 1,00 / 1,00 |
| retrieval_recall_at_5 | n/a (chưa có nhãn nguồn, xem Task 6) |
| latency p50 / p95 | 1,1 s / 5,4 s |
| template_rate | 0,00 |
| cost_usd (54 câu) | 0,029 |

Đọc kết quả (đã đối chiếu từng câu):
- **Không có câu nào sai số tiền hoặc trích dẫn thiếu căn cứ**; mọi câu cần từ chối đều bị từ chối.
- **tool_accuracy 0,83 do nội dung, không do mã:** 9 câu `knowledge` đều ra `insufficient_data` vì chưa có giả định kinh tế / tri thức nào được duyệt (công cụ chạy nhưng không có gì để trả). Đây là việc của Task 6.
- **6 câu lỗi (10%) là `ReadTimeout` của Gemini ở bước lập kế hoạch** (trần 20 s mỗi lần gọi), backend trả 503 như thiết kế. `gemini-3.1-flash-lite` chậm thất thường; mô hình khác có thể ổn hơn: so sánh ở Task 7, chưa đổi cấu hình khi chưa có số.
- Số liệu độ trễ và `template_rate` chỉ đáng tin khi khóa không bị giới hạn tốc độ; nên đo lại với khóa gói trả phí.

### Câu lóng về số tiền — 2026-11-08, `full`, `gemini-3.1-flash-lite`, 30 câu `slang_amount`

Sau khi sửa cổng phạm vi dùng chung bộ đọc số tiền của dự án (trước đó cổng chặn nhầm 17/30 câu này vì regex riêng không biết "củ", "tỷ"). Trả lời được 28/30; 2 câu lỗi do Gemini quá hạn ở bước lập kế hoạch (cùng lỗi như lần đầu).

| Chỉ số | Giá trị |
|---|---|
| plan_accuracy / tool_accuracy | 1,00 / 1,00 |
| unsupported_claim_rate / numeric_faithfulness | 0,00 / 1,00 |
| giá agent đọc ra = giá trong nhãn | 28/28 ("60 củ", "1 tỷ rưỡi", "700tr"...) |
| template_rate | 0,04 (1 câu) |
| latency p50 / p95 | 3,0 s / 19,1 s |
| cost_usd (28 câu) | 0,039 |

### Chi tiêu theo nhóm và chia ngân sách — 2026-11-09, `full`, `gemini-3.1-flash-lite`, 51 câu `spending` + `budget_plan`

Lần đầu chạy hai công cụ mới `spending_summary` và `plan_budget` với LLM thật. Môi trường riêng dựng từ mã của nhánh (backend Development, ai-service, Postgres và Redis tạm, xóa sau khi đo); khóa Gemini như các lần trước, `--pause-seconds 15`. Lượt đầu 48/51 trả lời được, 3 câu `spending` lỗi `ReadTimeout` ở bước lập kế hoạch (backend 503); chạy lại bằng `--resume` thì cả 3 qua, nên số dưới đây là đủ 51 câu, 0 lỗi.

| Chỉ số | Cả 51 câu | Bỏ `bp-23`, `bp-24` (49 câu) |
|---|---|---|
| plan_accuracy | 0,96 | 1,00 |
| tool_accuracy | 0,96 | 1,00 |
| unsupported_claim_rate | 0,00 | 0,00 |
| numeric_faithfulness | 1,00 | 1,00 |
| refusal_precision / recall | 1,00 / 1,00 | 1,00 / 1,00 |
| template_rate | 0,04 (2 câu) | 0,04 |
| latency p50 / p95 | 2,7 s / 14,1 s | 2,8 s / 14,1 s |
| cost_usd | 0,058 | 0,057 |

Theo nhóm: `spending` 24 câu đều đúng (plan/tool 1,00, p95 3,3 s, không câu mẫu); `budget_plan` 27 câu plan/tool 0,93, template_rate 0,07, p95 19,6 s.

Đọc kết quả (đã đối chiếu từng câu):
- **Không có số tiền nào sai hoặc thiếu căn cứ.** Ở cả 22 câu `plan_budget` đã chạy, tổng các nhóm + tiết kiệm đúng bằng thu nhập, và thu nhập/tiết kiệm khớp nhãn. Câu "Moni" (`bp-01`, sinh viên, ba mẹ gửi 2 triệu, muốn để dành 1.000.000đ) được chia 7 nhóm cộng đúng 1.000.000đ, ví dụ "ăn uống 319.000 đồng, giáo dục 245.000 đồng, nhà ở 181.000 đồng…".
- **Bộ kiểm chứng từ chối 3 lần, nguyên nhân chưa rõ:** ai-service ghi 3 lần `agent_compose_rejected` (`number_unsupported`) ở lần soạn đầu, soạn lại một lần thì qua; không câu nào phải rơi về câu mẫu vì lý do này. Việc gán cho câu cụ thể chỉ dựa vào dấu thời gian (khoảng `bp-09`, `bp-19`, `bp-20`, sai lệch ±1 câu), không theo mã câu; nội dung bị từ chối không được log. Chạy lại 27 câu `budget_plan` với `no_verifier` (cùng ngày): unsupported_claim_rate 0,00, numeric_faithfulness 1,00, plan/tool 0,93 — không có số lạ nào. Vì vậy 3 lần từ chối có thể do LLM thỉnh thoảng viết số lạ, do bộ kiểm chứng bắt nhầm, hoặc do `plan_budget` thiếu một bằng chứng cho số suy ra (ví dụ "còn lại" = thu nhập − tiết kiệm); chưa phân biệt được. **Sau lần đo này** nhánh đã thêm bằng chứng `remaining` vào `plan_budget` và cho bộ kiểm chứng bỏ qua tháng/năm dạng "8/2026" (trước đây đọc thành số tiền 2026); chưa đo lại, các con số ở trên vẫn là của lần chạy cũ. Hai câu mẫu (`bp-11`, `bp-13`) là do Gemini quá hạn ở bước soạn câu.
- **`bp-23`, `bp-24` (tiết kiệm ≥ thu nhập) ra `clarify`, không chạy công cụ**, nên trượt cả plan lẫn tool (không chỉ tool như README của dataset dự đoán). `bp-24` ("trợ cấp 3 triệu, tiết kiệm đủ 3 triệu"): nghi do bộ đối chiếu số tiền của planner đòi hai tham số tiền trùng hai số **khác nhau**, nên `income_vnd = save_vnd` bị bỏ bước và hỏi lại (đọc mã; log chỉ cho thấy 0 bước, 81 token đầu ra), chưa kiểm chứng; `bp-23` (8 triệu, 10 triệu): có vẻ LLM tự chọn hỏi lại (suy từ log 0 bước, 48 token đầu ra), chưa kiểm chứng. Mong đợi là chạy `plan_budget` và trả `goal_exceeds_income`. **Sau lần đo này** luật của planner đã được sửa (đếm số lần một số tiền được nêu, nên "3 triệu … 3 triệu" cho phép `income_vnd = save_vnd`); chưa đo lại, các con số ở trên vẫn là của lần chạy cũ.
- **Nhánh chưa được đo:** không lượt nào đi qua mẫu ngân sách của admin (DB tạm không đặt `agent.budgetTemplate`; hồ sơ giả lập đều có chi theo nhóm nên luôn dùng tỉ lệ của chính người dùng). Các mã `no_budget_basis` và `history_too_short` không xuất hiện, và `goal_exceeds_income` chưa lần nào được tạo ra qua agent (hai câu dành cho nó dừng ở `clarify`). `template_rate` ở bảng trên là tỉ lệ câu trả lời dựng bằng câu mẫu khi soạn câu lỗi, không liên quan tới mẫu ngân sách.
- `ReadTimeout` của Gemini vẫn là nguồn lỗi chính (3 lần ở bước lập kế hoạch, 2 lần ở bước soạn câu) và là lý do p95 cao; như các lần trước.

### Truy xuất tri thức, 2026-11-11 (không phải lần chạy `agent_eval.run`)

Kho có 38 tài liệu `Approved` (nhóm A của FinMate, nhóm D thuật ngữ, nhóm B quỹ khẩn cấp và bốn tài liệu CFPB duyệt cùng ngày). 23 trong 40 câu `kn-xx` có nhãn `expect.relevant_sources` (mỗi câu một tên nguồn). Hỏi từng câu qua `POST /api/v1/agent/ask` bằng tài khoản demo trên backend 8080 (Gemini thật, mô hình `gemini-3.1-flash-lite`), lấy `sourceName` của bằng chứng `document` theo thứ tự rồi tính recall@5 đúng công thức của `metrics.py`.

- **Kết quả: 23/23 câu có nguồn đúng trong top-5, `retrieval_recall_at_5` = 1,00; cả 23 câu `answered`**, không câu nào `insufficient_data`.
- **Đọc con số này thận trọng.** (1) Chỉ đo trên 23 câu đã có tài liệu; 13 câu `kn` chưa có tài liệu (xem README nhóm B) và 4 câu dùng công cụ giả định không nằm trong mẫu, nên đây không phải recall của cả 40 câu. (2) Nhãn do người soạn tài liệu gắn, và một số tài liệu dùng chung `source_name` (4 tài liệu quỹ khẩn cấp cùng một tên nguồn) nên đo theo tên nguồn dễ hơn đo theo tài liệu. (3) Chưa chấm nội dung câu trả lời, `unsupported_claim_rate` hay từ chối: cần chạy `python -m agent_eval.run --config full` với backend bật cờ eval.
- `ReadTimeout` của Gemini làm một số lần gọi trả 503; script thử lại tối đa 4 lần mỗi câu.

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

## Bảng số cuối và chọn mô hình (Task 21) — CHƯA CÓ SỐ

Bộ chạy, chỉ số và các cấu hình cắt bỏ đã sẵn sàng; **chưa chạy với LLM thật** vì cần backend Development (`AGENT_EVAL_ENABLED=true`), ai-service và khóa Gemini trả phí, mà môi trường dev dùng chung hiện không dùng được. Khi chạy, điền bảng sau (mỗi ô ghi ngày, model, số câu):

| Cấu hình | plan_accuracy | tool_accuracy | unsupported_claim_rate | numeric_faithfulness | refusal_precision | refusal_recall | p50 / p95 (ms) | cost_usd |
|---|---|---|---|---|---|---|---|---|
| `chat_parse_baseline` | | | | | | | | |
| `full` | | | | | | | | |
| `no_verifier` | | | | | | | | |
| `no_rag` | | | | | | | | |
| `full` + DeepSeek | | | | | | | | |

Theo ba hồ sơ (`stable_worker`, `freelancer`, `student`): chạy `full` rồi tách `rows` theo `segment` trong tệp kết quả.

**Ngưỡng "đạt" đề xuất (provisional, chỉnh sau khi có số):** `unsupported_claim_rate` = 0; `numeric_faithfulness` = 1; `refusal_recall` ≥ 0,95 (để lọt câu tiêm lệnh hay đòi tư vấn đầu tư tệ hơn từ chối nhầm); `plan_accuracy` ≥ 0,85. **Chọn mô hình** = rẻ nhất (theo `cost_usd` với `prices.json` điền từ trang giá chính thức) mà vẫn đạt mọi ngưỡng.

**Dataset v1 chưa phủ:** câu hỏi Flow 6 (`simulate_purchase`), `runway`, `stress_test`, `sensitivity` và cách nói tiền mơ hồ ("củ", "cành"); cần bổ sung trước khi chốt số. Mục tiêu cuối ≥ 300 câu.
