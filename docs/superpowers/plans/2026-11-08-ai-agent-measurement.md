# AI Financial Agent — Đo thật, nạp nội dung, chốt mô hình

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Biến agent từ "đã code xong, test tự động xanh" thành "có số đo thật với LLM thật, có nội dung đã duyệt để trả lời, và có mô hình được chọn bằng số".

**Architecture:** Không thêm thành phần agent mới. Dùng bộ đánh giá sẵn có (`ai-service/agent_eval`, `POST /api/v1/internal/agent/run`) chạy trên hồ sơ giả lập, mở rộng bộ câu hỏi, đo truy xuất, rồi nạp giả định / tri thức đã duyệt qua trang admin và kiểm tay trên stack thật. Mọi lần chạy tốn token đều có hạn mức do chủ dự án đặt trước.

**Tech Stack:** Python 3.11 (`agent_eval`, pytest), ASP.NET Core 9 (endpoint eval chỉ ở Development), Gemini (chính) / OpenRouter (dự phòng), PostgreSQL + pgvector.

**Spec:** `docs/agent-eval.md` (chỉ số, cấu hình cắt bỏ, ngưỡng đề xuất), `docs/agent-api.md` (hợp đồng), kế hoạch gốc `docs/superpowers/plans/2026-11-07-ai-financial-agent.md` (Task 18, 21).

## Quyết định cần chủ dự án trả lời TRƯỚC khi chạy Task 3 và 7 (tốn tiền thật)

1. Hạn mức token / tiền tối đa cho cả đợt đo (Task 3 + 7). Đề xuất: chạy thử 10 câu → 60 câu → rồi mới 300 câu × 4 cấu hình; dừng nếu một bước vượt hạn mức.
2. Khóa Gemini nào dùng (gói trả phí; gói miễn phí dùng dữ liệu để cải thiện sản phẩm, xem `docs/agent-api.md` mục Quyền riêng tư). Dữ liệu đánh giá là hồ sơ giả lập nên không có dữ liệu người thật.
3. Có so với DeepSeek / model thứ hai không (thêm một lượt chạy `full`).

## Global Constraints

- LLM không sinh số tiền; mọi số tiền = giá trị một bằng chứng (đã có, không đổi).
- Chạy đo chỉ trên Development, `AGENT_EVAL_ENABLED=true`; ngoài Development bật cờ này thì backend từ chối khởi động (không nới).
- Khóa API chỉ qua biến môi trường, không qua dòng lệnh, không ghi vào tệp kết quả hay log.
- Mỗi lượt đo dùng `userId` ngẫu nhiên và nguồn dữ liệu giả lập: không chạm dữ liệu người dùng thật, không xóa dữ liệu AI DB (OCR, tri thức, huấn luyện).
- Thiếu giá thì chi phí in `n/a`, không bao giờ in `$0` hoặc đoán giá; giá chỉ lấy từ trang giá chính thức của nhà cung cấp, ghi URL và ngày.
- Ngưỡng "đạt" (đề xuất, chỉnh sau khi có số): `unsupported_claim_rate` = 0; `numeric_faithfulness` = 1; `refusal_recall` ≥ 0,95; `plan_accuracy` ≥ 0,85.
- Không commit tệp tạm / kết quả thô lớn: chỉ commit bảng tóm tắt trong `docs/agent-eval.md`; `agent_eval/results/` phải nằm trong `.gitignore`.
- Dữ liệu đổi được (giả định, tri thức) nằm trong DB, nhập qua admin; không hard-code vào code.

## Review Focus

1. Gemini trả 429 / timeout giữa chừng một đợt dài: kết quả đã chạy phải được lưu và chạy tiếp được, không mất cả đợt, không tính câu lỗi là "đúng".
2. `prices.json` thiếu model: chi phí phải là `n/a`, tổng không được cộng 0 vào.
3. Câu nói tiền mơ hồ ("5 củ", "20 cành", "nửa tỷ"): agent phải hỏi lại hoặc hiểu đúng, không bịa số.
4. Câu tri thức khi chưa có tài liệu đã duyệt: phải ra `insufficient_data` (`knowledge_missing`), không trả lời từ trí nhớ mô hình.
5. Chỉ số truy xuất: hiện `retrieved` và `relevant` luôn rỗng nên `retrieval_recall_at_5` không đo được; không được báo "1,0" chỉ vì chia cho 0.

---

### Task 1: Điền bảng giá từ trang chính thức

**Files:**
- Modify: `ai-service/agent_eval/prices.json`
- Test: `ai-service/tests/unit/test_agent_metrics.py`

**Interfaces:**
- Consumes: `metrics` hiện có tính `cost_usd` từ `{model: {input_per_million, output_per_million}}`.
- Produces: `prices.json` có `{model: {input_per_million, output_per_million, source_url, as_of}}` cho model đang chạy (Gemini chính, model dự phòng, model so sánh nếu có).

- [ ] **Step 1: Viết test** `test_every_price_entry_has_a_source_url_and_a_date_and_positive_rates` (đọc `prices.json` thật; rỗng thì skip) và `test_a_model_missing_from_prices_costs_na_not_zero`.
- [ ] **Step 2: Chạy** `cd ai-service && pytest tests/unit/test_agent_metrics.py -q`. Expected: test thứ hai PASS sẵn, test thứ nhất SKIP (file rỗng).
- [ ] **Step 3: Lấy giá** từ trang giá chính thức của từng nhà cung cấp (WebFetch), điền vào `prices.json` kèm `source_url` và `as_of` là ngày lấy. Không điền khi không đọc được trang.
- [ ] **Step 4: Chạy lại** test ở Step 2. Expected: cả hai PASS.
- [ ] **Step 5: Commit** `chore(agent-eval): fill model prices with sources`.

### Task 2: Cho đợt đo sống sót khi LLM lỗi giữa chừng

**Files:**
- Modify: `ai-service/agent_eval/run.py`, `.gitignore` (thêm `ai-service/agent_eval/results/` nếu chưa có)
- Test: `ai-service/tests/unit/test_agent_run.py`

**Interfaces:**
- Consumes: vòng lặp câu hỏi ở `run.py` (đọc lại để xác nhận hiện nó xử lý lỗi một câu thế nào).
- Produces: mỗi câu ghi kết quả ngay sau khi chạy (tệp `.jsonl` tăng dần trong `results/<run_id>/`); lỗi mạng / 429 / 503 một câu thì ghi `error` và chạy tiếp; tham số `--resume <run_id>` bỏ qua các `id` đã có; báo cáo cuối nêu rõ số `errored` (không tính vào chỉ số đúng/sai nhưng in ra).

- [ ] **Step 1: Viết test** `a_failing_question_is_recorded_as_errored_and_the_run_continues`, `resume_skips_ids_already_recorded`, `errored_rows_are_reported_and_excluded_from_accuracy`.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_agent_run.py -q`. Expected: FAIL (chưa có hành vi).
- [ ] **Step 3: Implement** ghi tăng dần + `--resume` + dòng `errored` trong báo cáo `.md`; dùng lại `build_row`, không thêm package.
- [ ] **Step 4: Chạy lại** test. Expected: PASS.
- [ ] **Step 5: Commit** `feat(agent-eval): incremental results, resume and errored rows`.

### Task 3: Chạy thử rồi chạy bộ 60 câu hiện có, sửa lỗi lộ ra

**Files:**
- Modify: `docs/agent-eval.md` (mục "Kết quả": ngày, model, số câu, bảng; thêm một dòng trỏ tới kế hoạch này)
- Có thể sửa: `ai-service/app/agent/*`, `backend/FinMate.Application/Agent/*` tùy lỗi tìm thấy

**Interfaces:**
- Consumes: Task 1, Task 2; stack Development (backend `AGENT_EVAL_ENABLED=true`, ai-service, Gemini key); hạn mức đã được chủ dự án trả lời.
- Produces: tệp kết quả `agent_eval/results/<run_id>/` cho `full` trên 60 câu, và bảng đầu tiên trong `docs/agent-eval.md`.

- [ ] **Step 1: Dựng môi trường** (một lần): `AGENT_EVAL_ENABLED=true` cho backend và ai-service, `AI_SERVICE_API_KEY` và `INTERNAL_API_KEY` qua biến môi trường. Xác nhận `POST /api/v1/internal/agent/run` trả 200 với một câu mẫu và 404 khi tắt cờ.
- [ ] **Step 2: Chạy thử** `python -m agent_eval.run --config full --limit 10`. Expected: 10 hàng, không `errored`; đọc tay 10 câu trả lời xem luồng Gate → Plan → Execute → Compose → Verify có đúng không.
- [ ] **Step 3: Chạy 60 câu** `python -m agent_eval.run --config full`. Expected: tệp `.md` kết quả; ghi bảng vào `docs/agent-eval.md`.
- [ ] **Step 4: Phân loại mọi câu sai** (kế hoạch sai, công cụ sai, claim không có bằng chứng, từ chối sót). Mỗi nhóm lỗi: viết test tái hiện thất bại trước, sửa, chạy lại; không sửa thì ghi lý do trong tài liệu.
- [ ] **Step 5: Chạy lại 60 câu**; ghi bảng "trước / sau sửa".
- [ ] **Step 6: Commit** `docs(agent-eval): first real run on 60 questions` (các commit sửa lỗi đi riêng).

### Task 4: Mở rộng bộ câu hỏi lên ≥ 300 và phủ các công cụ còn thiếu

**Files:**
- Modify: `ai-service/agent_eval/dataset/questions.jsonl`, `ai-service/tests/unit/test_agent_dataset.py`
- Create: `ai-service/agent_eval/dataset/README.md` (cách thêm câu, schema)

**Interfaces:**
- Consumes: schema hiện có `{id, text, segment, kind, expect{in_scope, tools[], must_cite[], clarify, numeric_truth, refusal_reason}}`.
- Produces: ≥ 300 dòng; thêm `kind`: `purchase` (công cụ `simulate_purchase`), `runway`, `stress`, `sensitivity`, `slang_amount` (củ, cành, nửa tỷ, "5tr"); mỗi `kind` ≥ 20 câu, mỗi `segment` ≥ 60 câu; `adversarial` ≥ 40 (tiêm lệnh, đòi tư vấn mã cổ phiếu / crypto, hỏi dữ liệu người khác).

- [ ] **Step 1: Viết test** `dataset_has_at_least_300_questions`, `every_kind_has_at_least_20`, `slang_amount_questions_expect_clarify_or_the_exact_amount`, `no_two_questions_share_the_same_text`; mở rộng `test_the_dataset_covers_*` cho các `kind` mới.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_agent_dataset.py -q`. Expected: FAIL (mới 60 câu).
- [ ] **Step 3: Soạn câu** theo từng `kind` bằng văn nói tự nhiên của người Việt (biến thể cách hỏi, số tiền, kỳ hạn). Đáp án (`expect`) viết tay cùng lúc với câu, không sinh từ chính agent. Câu `slang_amount` phải có đáp án rõ: hỏi lại hoặc số đúng.
- [ ] **Step 4: Chạy lại** test. Expected: PASS.
- [ ] **Step 5: Commit** `test(agent-eval): grow dataset to 300+ with purchase, runway, stress, sensitivity and slang`.

### Task 5: Đo được truy xuất tri thức (`retrieval_recall_at_5`)

**Files:**
- Modify: `ai-service/agent_eval/run.py` (`build_row`), `ai-service/agent_eval/metrics.py`, `ai-service/agent_eval/dataset/questions.jsonl` (câu `knowledge`: thêm `expect.relevant_sources`), `ai-service/tests/unit/test_agent_run.py`, `ai-service/tests/unit/test_agent_metrics.py`

**Interfaces:**
- Consumes: `evidence[]` có `source = document` và `sourceName` / `sourceUrl`; dataset từ Task 4.
- Produces: `build_row` điền `retrieved` = nguồn (theo thứ tự) từ bằng chứng `document`, `relevant` = `expect.relevant_sources`; metric trả `None` (in "n/a") khi không câu nào có `relevant`, không trả 1,0.

- [ ] **Step 1: Viết test** `retrieved_comes_from_document_evidence_in_order`, `a_question_without_relevant_sources_is_not_counted_in_recall`, `recall_is_na_when_no_question_has_relevant_sources`.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_agent_run.py tests/unit/test_agent_metrics.py -q`. Expected: FAIL.
- [ ] **Step 3: Implement** như Interfaces. `relevant_sources` gắn sau khi Task 6 đã nạp tài liệu thật (tên nguồn phải khớp tên tài liệu đã duyệt), nên Step này làm xong phần code trước, phần gắn nhãn ở cuối Task 6.
- [ ] **Step 4: Chạy lại** test. Expected: PASS.
- [ ] **Step 5: Commit** `feat(agent-eval): measure retrieval recall@5 from document evidence`.

### Task 6: Nạp giả định kinh tế và tri thức đã duyệt

**Files:**
- Create: `docs/agent-content-checklist.md` (danh sách mục cần nạp, nguồn, người duyệt)
- Dữ liệu vào DB qua trang admin (`/api/v1/admin/assumptions`, `/api/v1/admin/knowledge`); không hard-code.

**Interfaces:**
- Consumes: khóa mà công cụ cần, tối thiểu `general_price_growth` (mặc định của `forecast_goal`) và các khóa cho các nhóm mục tiêu trong dataset (xe, nhà, học phí, du lịch...).
- Produces: bản ghi `economic_assumptions` ở trạng thái `approved` (có `sourceName`, `sourceUrl`, `asOf`) và tài liệu tri thức `approved` trong `kb_documents` / `kb_chunks` (có vector).

- [ ] **Step 1: Liệt kê** (từ code công cụ + dataset Task 4) khóa giả định và chủ đề tri thức cần có; ghi vào checklist kèm nguồn đề xuất (Tổng cục Thống kê, Ngân hàng Nhà nước, văn bản pháp luật thuế; chỉ nguồn chính thống, URL đúng trang).
- [ ] **Step 2: Chủ dự án (hoặc nhân sự được giao) nhập bản nháp** qua admin; **OpsManager / Admin duyệt**. Tôi không tự duyệt thay.
- [ ] **Step 3: Kiểm** bằng vài câu qua `POST /api/v1/internal/agent/run`. Expected: `get_assumptions` và `search_knowledge` trả bằng chứng có nguồn và ngày; khóa chưa duyệt vẫn ra `assumption_missing`.
- [ ] **Step 4: Gắn `relevant_sources`** cho các câu `knowledge` trong dataset theo tên tài liệu vừa duyệt (hoàn tất Task 5 Step 3).
- [ ] **Step 5: Commit** `docs(agent): content checklist for assumptions and knowledge`.

### Task 7: Chạy đủ các cấu hình trên bộ ≥ 300 câu và chọn mô hình

**Files:**
- Modify: `docs/agent-eval.md` (mục "Bảng số cuối và chọn mô hình": điền bảng, ngày, model, số câu, kết luận chọn model)

**Interfaces:**
- Consumes: Task 1–6; hạn mức chủ dự án đã duyệt.
- Produces: bảng cấu hình × chỉ số; model được chọn = rẻ nhất mà đạt mọi ngưỡng; danh sách lỗi còn lại.

- [ ] **Step 1: Chạy** `full` trước, rồi `no_verifier`, `no_rag`, `rasa_baseline` (nếu Rasa đang tắt thì ghi rõ "không chạy"). Mỗi lần dùng ghi tăng dần + `--resume` nếu lỗi.
- [ ] **Step 2: Nếu chủ dự án chọn so sánh**, đổi `AGENT_PROVIDER` / `AGENT_MODEL` ở ai-service, chạy lại `full` và gắn nhãn `--provider/--model`.
- [ ] **Step 3: Điền bảng**, tách theo `segment` và theo `kind`; ghi `errored` và `template_rate`.
- [ ] **Step 4: Chọn mô hình** theo quy tắc; nếu không model nào đạt ngưỡng, ghi rõ nhóm lỗi, không hạ ngưỡng để báo đạt.
- [ ] **Step 5: Commit** `docs(agent-eval): final table and model choice`.

### Task 8: Thử tay trên stack thật với tài khoản người dùng thật

**Files:**
- Create: `docs/agent-manual-qa.md` (câu hỏi, câu trả lời, nhận xét giọng văn, lỗi tìm thấy)

**Interfaces:**
- Consumes: backend + ai-service chạy bình thường (không bật cờ eval), một tài khoản thử có ít nhất 3 tháng giao dịch xác nhận, Task 6 đã nạp nội dung.
- Produces: bản ghi 15 câu thật (5 dự báo mục tiêu, 3 mua món đồ, 3 tri thức, 2 từ chối, 2 hỏi lại) kèm `usedTemplate`, `verdict` và đánh giá đọc có tự nhiên không.

- [ ] **Step 1: Đăng nhập** bằng tài khoản thử, gọi `POST /api/v1/agent/ask` cho từng câu; chép nguyên câu trả lời, `status`, `verdict`, `usedTemplate`.
- [ ] **Step 2: Đối chiếu** mỗi số tiền với `evidence[]`, và tự tính tay ít nhất 3 câu.
- [ ] **Step 3: Ghi lỗi** (giọng văn, số sai, thiếu cảnh báo, kết luận quá chắc); lỗi chức năng thì tạo test tái hiện rồi sửa như Task 3 Step 4.
- [ ] **Step 4: Commit** `docs(agent): manual QA notes on the real stack`.

### Task 9 (chỉ làm nếu Task 7 đạt ngưỡng và còn thời gian): Nhập sao kê

Đúng như Task 18 của kế hoạch gốc `2026-11-07-ai-financial-agent.md` (dòng 409–420): `statement_import.py`, route `POST /api/v1/agent/import-statement`, test `sum_mismatch_marks_import_partial`, `never_creates_confirmed_transactions`, `duplicate_lines_are_flagged`. Không bắt đầu khi chưa có Task 7.

---

## Ngoài kế hoạch này (chờ chủ dự án quyết)

- Rasa: commit phần tắt trong `docker-compose.yml`, hay xóa hẳn code và tài liệu (nếu xóa, làm thành một kế hoạch riêng).
- Xóa trang sandbox tạm ở FinMate_Admin sau khi thử xong.
- Giao diện app (repo FinMate_FE): chưa làm; hợp đồng ở `docs/agent-api.md`.

## Tự kiểm kế hoạch

- **Phủ yêu cầu:** giá (T1), độ bền khi chạy dài (T2), chạy thật và sửa lỗi (T3), dataset ≥ 300 và phủ công cụ còn thiếu (T4), đo truy xuất (T5), nội dung đã duyệt (T6), bảng cuối + chọn model (T7), thử tay (T8), tùy chọn Task 18 (T9).
- **Thứ tự chạy:** T1 → T2 → T3 → T4 → T5 (code) → T6 (nạp nội dung, gắn nhãn) → T7 → T8 → T9. T6 phải xong trước T7, nếu không mọi câu tri thức đều ra `knowledge_missing`.
- **Giả định cần xác nhận khi bắt tay làm:** cách `run.py` hiện xử lý lỗi một câu (T2 phải đọc code trước khi viết test), và trang giá chính thức đọc được bằng WebFetch.
