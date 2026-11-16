# AI Financial Agent — Lấp các chỗ còn yếu (giả định, tri thức, bộ phân loại, thử mua hàng)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Đưa agent từ "có khung, thiếu dữ liệu" sang "trả lời được các câu về mục tiêu dài hạn, kiến thức chung và mua hàng", với số đo trước/sau cho từng thay đổi.

**Architecture:** Không đổi khung Gate → Plan → Execute → Compose → Verify. Phần lớn việc là **dữ liệu** (giả định kinh tế, tài liệu RAG, ví dụ cho bộ phân loại) đi qua đường đã có và có người duyệt; mã chỉ thêm ở hai chỗ nhỏ (in danh sách câu sai của `eval_intent.py`, người dùng demo cho môi trường Development). Mỗi nhóm việc đo bằng bộ đánh giá đã có.

**Tech Stack:** ASP.NET Core 9, Python 3.11 (`ai-service`), PostgreSQL + pgvector, bộ đánh giá `ai-service/agent_eval`, xUnit, pytest.

**Spec:** `docs/agent-content-checklist.md`, `docs/knowledge-drafts/README.md` (mục 4 dàn ý nhóm B, mục 5 danh sách giả định), `docs/agent-eval.md`. Kế hoạch `2026-11-09-agent-hardening-knowledge-compare.md` vẫn là nguồn cho `compare_spending` (Wave 3) và đo lại toàn bộ (Task 12); **Task 7 của nó (soạn và duyệt tri thức) được thay bằng Task 2–3 dưới đây, Task 11 của nó (đo khi tắt Rasa) không còn ý nghĩa vì Rasa đã bị gỡ.**

## Quyết định cần chủ dự án chốt trước khi chạy

1. **Ba mục "Không làm" giữ nguyên, vì là chủ ý chứ không phải thiếu sót:** tư vấn đầu tư bị từ chối theo thiết kế (README nhóm B: "agent từ chối loại câu này theo thiết kế"); báo cáo và mục tiêu dài hạn riêng ngoài phạm vi từ quyết định luồng quản lý tài chính; ghi giao dịch hộ đã có ở `POST /transactions/chat`, agent chỉ đọc. Đổi ý thì mở plan riêng.
2. **Giả định ngoài CPI nhập tay** (nhà, xe, lương, lãi tiền gửi, học phí): chỉ `general_price_growth` có đường tự cập nhật (`EconomicAssumptionRefresher.Key`). Đề xuất: nhập tay, mỗi số có trang nguồn và ngày. Không mở rộng bộ tự cập nhật cho các khóa này.
3. **kn-21 "ngân sách cuốn chiếu":** FinMate không có phương pháp này. Đề xuất: viết một tài liệu giải thích khái niệm chung có nguồn, không đổi câu hỏi.
4. **Tài khoản demo chỉ tồn tại ở Development**, không bao giờ seed ở môi trường khác.

## Global Constraints

- Tiền là `BIGINT` đồng; tỉ lệ giả định là điểm cơ bản nguyên (`rateBps`, 3,5% = 350, khoảng −10000…100000).
- Không hard-code dữ liệu thay đổi được: giả định, tài liệu, cụm từ cổng phạm vi nằm ở DB hoặc tệp dữ liệu, không nằm trong mã.
- Tài liệu RAG: một chủ đề một tài liệu, ≤ 450 ký tự mỗi đoạn tự đứng được, **không viết số tiền cụ thể** (chỉ %, số tháng, năm), tiếng Việt có dấu, `source_url` là trang cụ thể, `as_of` là ngày của số liệu. Mỗi con số pháp lý hoặc lãi suất phải mở trang nguồn kiểm tra, không điền theo trí nhớ.
- Bộ phân loại: chỉ thêm ví dụ vào split `train` của `ai-service/data/intent/examples.jsonl`; **không chép câu từ `tests/fixtures/eval/heldout_intent.jsonl` hay `intent_coverage.jsonl` vào dữ liệu train** (làm hỏng số đo).
- Không chạy script lạ với `--help` (một số không có argparse). `eval_intent.py` có argparse, chạy được.
- Không commit tab thử agent hay file tạm; `git add` từng đường dẫn cụ thể. Không push khi chưa được duyệt.

## Review Focus

- Khóa giả định bị gỡ hoặc chỉ ở trạng thái nháp: agent phải nói "thiếu giả định", không dùng số cũ hay số mặc định (đã có test, giữ nguyên, chạy lại sau khi nhập).
- Tài liệu có số pháp lý đã hết hiệu lực (`effective_to` trong quá khứ): agent không được trích. Kiểm ở Task 3.
- Câu hỏi tư vấn đầu tư lẫn trong câu có số tiền ("tôi có 50 triệu nên mua cổ phiếu nào"): cổng vẫn từ chối sau khi thêm tri thức mới (chạy lại các câu `out_of_scope` và `adversarial`).
- Thêm ví dụ cho một ý định làm tụt ý định khác (ví dụ `add_income` kéo `add_expense`): đo cả hai bộ độc lập, không chỉ bộ có câu lỗi.
- Tài khoản demo bị seed ở Production: chặn bằng kiểm tra môi trường và có test.

---

## Task 1: Giả định kinh tế (người làm, mã không đổi)

**Files:**
- Không đổi mã. Dùng trang admin `/settings/economic` (CPI) và trang Manager *Giả định kinh tế* (`/ops/assumptions`).
- Modify: `docs/knowledge-drafts/group-c-assumptions.json` (ghi lại số đã nhập, nguồn, ngày, để người sau kiểm lại)

**Interfaces:**
- Consumes: `IAssumptionService.CreateDraftAsync(actorId, key, rateBps, unit, sourceName, sourceUrl, asOf, ip)` và `ApproveAsync(id, approverId, ip)` (Staff nhập, OpsManager hoặc Admin duyệt; người nhập khác người duyệt).
- Produces: bản `Approved` cho các khóa `general_price_growth`, `house_price_growth`, `car_price_growth`, `wage_growth`, `savings_deposit_rate`, `tuition_growth` (đơn vị `pct_per_year`).

- [ ] **Step 1: Đo trước.** Chạy `cd ai-service && python -m agent_eval.run --config full --kinds forecast` (điều kiện chạy: `docs/agent-eval.md` mục "Cách chạy"). Lưu tên tệp kết quả làm mốc.
- [ ] **Step 2: Cấu hình CPI.** Ở `/settings/economic` đặt `economicFeeds.cpiIndexUrl` (trang công bố chỉ số giá tiêu dùng của Tổng cục Thống kê, mở trang thật để lấy URL), bấm *Chạy ngay*. Kỳ vọng: trạng thái dịch vụ `EconomicFeed` khỏe; có một bản `general_price_growth` chờ duyệt hoặc tự duyệt.
- [ ] **Step 3: Nhập tay năm khóa còn lại** theo bảng mục 5 của `docs/knowledge-drafts/README.md`, mỗi khóa một trang nguồn cụ thể. Ngoài `general_price_growth`, khóa nào không tìm được nguồn đáng tin thì để trống, không điền ước chừng.
- [ ] **Step 4: Duyệt** bằng tài khoản OpsManager (`manager@finmate.local`). Kiểm `GET /api/v1/admin/assumptions` (trang Manager gọi đường này) trả bản `approved`, `asOf` đúng ngày nguồn.
- [ ] **Step 5: Đo sau** như Step 1. Kỳ vọng: các câu `forecast` có kết luận dùng giả định tăng giá; `plan_accuracy` và `unsupported_claim_rate` không xấu đi (mục tiêu 0).
- [ ] **Step 6: Commit** `docs(agent): record the economic assumptions entered and their sources` (chỉ `group-c-assumptions.json`).

## Task 2: Tri thức nhóm B còn thiếu (người soạn, mã không đổi)

**Files:**
- Modify: `docs/knowledge-drafts/group-b-researched.json` (hiện 4 tài liệu quỹ khẩn cấp; thêm các chủ đề dàn ý mục 4 README: lập ngân sách thu nhập không ổn định, học phí và chi tiêu theo mùa, lạm phát, trả nợ tuyết lăn, nợ hay tiết kiệm trước, lãi kép, tỉ lệ nợ trên thu nhập, thuế TNCN với tiền lương, bảo hiểm y tế, lãi vay tiêu dùng, ngân sách cuốn chiếu...)
- Run: `scripts/submit_knowledge_drafts.py` (đã có)

**Interfaces:**
- Consumes: định dạng tài liệu `{title, source_name, source_url, as_of, effective_from, effective_to, text}` và các kiểm tra của script (độ dài, ngày giờ, trùng tiêu đề, cảnh báo số tiền).
- Produces: các tài liệu `Draft` ở môi trường dev, mỗi tài liệu ứng với một hoặc hai câu `kn-xx` trong bảng mục 4.

- [ ] **Step 1: Chọn lô đầu 8 chủ đề** theo thứ tự câu hỏi hay gặp nhất của người dùng (nợ, tiết kiệm bao nhiêu %, chi tiêu bất thường, thuế, bảo hiểm y tế, lãi vay, thu nhập không ổn định, học phí/Tết). Mỗi chủ đề mở trang nguồn thật rồi mới viết, đúng quy tắc README mục 2.
- [ ] **Step 2: Kiểm tra khô:** `python scripts/submit_knowledge_drafts.py docs/knowledge-drafts/group-b-researched.json`. Kỳ vọng: 0 lỗi, 0 cảnh báo số tiền.
- [ ] **Step 3: Gửi bản nháp:** thêm `--apply --base-url http://localhost:8080` với token Staff. Chạy lại không tạo trùng.
- [ ] **Step 4: Duyệt** từng tài liệu ở web quản trị *Tri thức AI* sau khi bấm *Đọc nội dung*. Chỉ bản `Approved` có `effective_from` không ở tương lai mới được agent dùng.
- [ ] **Step 5: Lặp lại** cho lô sau tới khi phủ đủ các câu `kn-01…kn-40` trong bảng mục 4 (trừ câu đã thuộc nhóm A và D).
- [ ] **Step 6: Commit** `docs(knowledge): group B drafts with sources` (chỉ tệp JSON nhóm B).

## Task 3: Gắn nhãn nguồn và đo truy xuất

**Files:**
- Modify: `ai-service/agent_eval/dataset/questions.jsonl` (thêm `expect.relevant_sources` cho các câu `kind=knowledge`; hiện 0 dòng có trường này nên `retrieval_recall_at_5` đang `null`)
- Test: chạy `agent_eval`

**Interfaces:**
- Consumes: tên tài liệu đã duyệt ở Task 2 và nhóm A, D (đã có 34 bản `Approved`).
- Produces: số `retrieval_recall_at_5` thật cho 40 câu kiến thức.

- [ ] **Step 1: Với mỗi `kn-xx`** ghi `expect.relevant_sources` bằng đúng tiêu đề tài liệu trả lời nó (bảng mục 3 và 4 của README cho biết tài liệu nào phủ câu nào).
- [ ] **Step 2: Chạy** `python -m agent_eval.run --config full --kinds knowledge`. Kỳ vọng: `retrieval_recall_at_5` có giá trị (không còn `null`), `unsupported_claim_rate` = 0.
- [ ] **Step 3: Câu nào lọt top-5 thì** sửa tiêu đề hoặc câu đầu của tài liệu (từ khóa ở tiêu đề và câu đầu, README mục 2) rồi gửi bản mới, duyệt, gỡ bản cũ; không đổi thuật toán tìm kiếm để chiều một câu.
- [ ] **Step 4: Kiểm hai điểm Review Focus:** một tài liệu có `effective_to` quá khứ không được agent trích; chạy lại `--kinds out_of_scope` và `--kinds adversarial`, `refusal_recall` không giảm.
- [ ] **Step 5: Commit** `docs(agent-eval): knowledge relevant_sources labels` (chỉ `questions.jsonl`).

## Task 4: Bộ phân loại ý định — in ra câu sai, thêm ví dụ theo mẫu

**Files:**
- Modify: `ai-service/scripts/eval_intent.py` (thêm cờ `--show-wrong` in từng câu sai: nhãn mong đợi, nhãn đoán, văn bản)
- Modify: `ai-service/data/intent/examples.jsonl` (thêm ví dụ `split: "train"`)
- Test: `ai-service/tests/` (test hiện có về bộ phân loại), `eval_intent.py`

**Interfaces:**
- Consumes: `IntentClassifier.train(examples, c, class_weight)` và `parse_chat(text, nlu)` như `eval_intent.py` đang dùng; hai bộ độc lập `heldout_intent.jsonl` (138 câu) và `intent_coverage.jsonl` (212 câu).
- Produces: `python scripts/eval_intent.py --show-wrong` in thêm danh sách câu sai; mốc hiện tại 125/138 và 206/212, mục tiêu ≥ mốc Rasa cũ 130/138 và 209/212.

- [ ] **Step 1: Test.** Test cho hàm in/gom câu sai (nhận danh sách `(text, expected, got)`, trả dòng theo cặp nhãn, nhóm theo cặp nhãn sai nhiều nhất trước); chạy, kỳ vọng FAIL vì chưa có hàm.
- [ ] **Step 2: Cài** cờ `--show-wrong` trong `eval_intent.py` dùng hàm đó; chạy test, kỳ vọng PASS.
- [ ] **Step 3: Chạy** `python scripts/eval_intent.py --show-wrong`, lưu danh sách 13 + 6 câu sai. Gom theo **mẫu câu** chứ không theo từng câu (ví dụ "lương về 15 triệu" thuộc mẫu "thu nhập đến + số tiền", nhãn `add_income`).
- [ ] **Step 4: Với mỗi mẫu** viết 10–20 ví dụ đa dạng (cách nói, đơn vị tiền "15 triệu / 15tr / 15 củ", không dấu) vào `examples.jsonl` với `split: "train"`, nhãn đúng. Không dùng lại nguyên văn câu sai trong hai bộ độc lập.
- [ ] **Step 5: Đo lại** `python scripts/eval_intent.py`; ghi cả hai bộ. Kỳ vọng: không bộ nào giảm so với mốc, tổng đạt ≥ 130/138 và 209/212. Nếu một ý định khác tụt thì xem lại mẫu vừa thêm.
- [ ] **Step 6: Chạy** `pytest tests -q` trong `ai-service` (container thử, mount lexicon như các lần trước). Kỳ vọng: không test nào đỏ mới.
- [ ] **Step 7: Commit** `feat(intent): examples for income-arrives phrasing and the other measured misses` (chỉ `eval_intent.py`, test và `examples.jsonl`).

## Task 5: Tài khoản demo cho thử `simulate_purchase`

**Files:**
- Create: `backend/FinMate.Infrastructure/Persistence/Seed/DemoUserSeeder.cs`
- Modify: `backend/FinMate.API/Program.cs` (gọi seeder khi `IHostEnvironment.IsDevelopment()`)
- Test: `backend/FinMate.Tests/Integration/Controllers/DemoUserSeederTests.cs`

**Interfaces:**
- Consumes: `FinMateDbContext` (người dùng, ví, giao dịch, `recurring_items`), `AdminUserSeeder` làm mẫu cho cách seed idempotent.
- Produces: `Task DemoUserSeeder.SeedAsync(CancellationToken ct)`; tài khoản `demo@finmate.local` với một ví, ≥ 6 tháng giao dịch có quy luật (lương cố định, tiền trọ, ăn uống), hai khoản định kỳ. Chạy lại không tạo trùng. Không chạy ngoài Development.

- [ ] **Step 1: Viết test:** `SeedsOnlyInDevelopment` (môi trường Production → không có người dùng nào), `IsIdempotent` (gọi hai lần → một người dùng, số giao dịch không đổi), `AgentSimulatePurchaseReturnsEvidenceForDemoUser` (gọi `POST /api/v1/agent/ask` bằng token demo với "mua laptop 20 triệu hôm nay được không" → có bằng chứng từ `simulate_purchase`, không phải `insufficient_data`).
- [ ] **Step 2: Chạy** test, kỳ vọng FAIL.
- [ ] **Step 3: Cài** `DemoUserSeeder.SeedAsync` sinh dữ liệu xác định (cùng hạt giống, không ngẫu nhiên) để test và lần thử lặp lại được.
- [ ] **Step 4: Chạy lại** test, kỳ vọng PASS.
- [ ] **Step 5: Thử thật** trên backend eval cổng 8090 (cờ eval bật): đăng nhập demo, hỏi 3 câu mua hàng. Ghi kết luận `feasible / tight / infeasible` của từng câu vào mô tả commit.
- [ ] **Step 6: Commit** `feat(dev): deterministic demo user so simulate_purchase can be tried locally`. Tab thử của admin giữ ngoài commit như trước.

## Task 6: Đo lại và chốt

**Files:**
- Modify: `docs/agent-eval.md` (điền bảng số mới), `docs/agent-content-checklist.md` (đánh dấu mục đã xong)

- [ ] **Step 1: Chạy** `python -m agent_eval.run --config full` đủ 370 câu (ước tính ≤ 2 USD theo `prices.json`; vượt thì dừng).
- [ ] **Step 2: So với mốc** `agent_eval/results/20261108-122801-full-369730.json` (plan_accuracy 1.0, tool_accuracy 1.0, unsupported_claim_rate 0.0, numeric_faithfulness 1.0, refusal precision/recall 1.0 trên 28 câu) và với số của Task 1 và 3. Kỳ vọng: không chỉ số an toàn nào xấu đi; `retrieval_recall_at_5` có số.
- [ ] **Step 3: Ghi vào** `docs/agent-eval.md` ngày, cấu hình, số, và những câu còn sai kèm nguyên nhân.
- [ ] **Step 4: Commit** `docs(agent-eval): measured results after assumptions, knowledge and intent data`.

## Thứ tự và phụ thuộc

Task 1, 2, 4, 5 độc lập, làm song song được (4 và 5 là mã, 1 và 2 là việc của người). Task 3 cần Task 2 đã duyệt; Task 6 cần cả bốn. Việc của người (Task 1–2) là đường găng: mã chỉ vài giờ, còn đọc trang nguồn và duyệt từng tài liệu mới chiếm thời gian.
