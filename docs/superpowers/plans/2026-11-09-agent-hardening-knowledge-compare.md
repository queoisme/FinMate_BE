# AI Financial Agent — Sửa nốt việc hoãn, nạp tri thức, so sánh chi tiêu, đo lại toàn bộ

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Đóng các lỗi đã hoãn ở đợt trước, cho agent có tri thức để trả lời (nháp lấy từ chính catalog phương pháp, người duyệt), thêm khả năng "so sánh chi tiêu giữa hai kỳ", và có bảng số đo đầy đủ với LLM thật (kể cả đo khi tắt Rasa) để chốt các quyết định còn treo.

**Architecture:** Không đổi khung Gate → Plan → Execute → Compose → Verify. Sửa nhỏ ở cổng phạm vi (dữ liệu), cấu hình admin và `plan_budget`; thêm một lệnh admin tạo **bản nháp** tri thức từ `finance_methods` (người vẫn duyệt); thêm công cụ `compare_spending` trên nguồn dữ liệu theo tháng; sau cùng chạy bộ đánh giá thật và một lượt đo Rasa-tắt.

**Tech Stack:** ASP.NET Core 9 (`FinMate.Application/Agent`, `FinMate.API`), Python 3.11 (`ai-service/app/agent`, `agent_eval`), xUnit, pytest, PostgreSQL + pgvector, Gemini.

**Spec:** `docs/agent-api.md`, `docs/agent-eval.md` (mục "Bảng số cuối và chọn mô hình" đang trống), `docs/agent-content-checklist.md`, kế hoạch trước `docs/superpowers/plans/2026-11-08-agent-spending-and-budget-tools.md`, ghi chú Rasa trong `ai-service/rasa/README.md` và `docs/rasa-coverage-report.md`.

## Quyết định cần chủ dự án chốt TRƯỚC khi chạy (mỗi mục có đề xuất mặc định)

1. **Phạm vi đợt này.** Đề xuất: Wave 1 (sửa việc hoãn, Task 1–5), Wave 2 (nháp tri thức, Task 6–7), Wave 3 (so sánh chi tiêu, Task 8–10), Wave 4 (đo lại, Task 11–12). KHÔNG gồm: màn hình chat (repo FE), dịch vụ/voucher/gói trả phí, vai trò chuyên gia, agent tự thực hiện thao tác, nhập sao kê, xóa Rasa.
2. **Rasa:** chỉ ĐO khi tắt (Task 11), không xóa. Quyết định xóa hay giữ để sau khi có số.
3. **bp-23** ("lương 8 triệu muốn tiết kiệm 10 triệu", agent hỏi lại thay vì chạy công cụ): thử một câu chỉ dẫn trong mô tả công cụ (Task 5). Nếu vẫn hỏi lại sau khi đo, đổi nhãn câu hỏi sang `clarify`: hỏi lại với người muốn tiết kiệm nhiều hơn thu nhập là chấp nhận được.
4. **Nháp tri thức từ catalog** chỉ lấy nội dung do chính hệ thống quản lý (phương pháp, tham số mặc định). Kiến thức tài chính chung có nguồn ngoài (quỹ khẩn cấp, thuế...) và giả định kinh tế vẫn do Staff soạn, OpsManager duyệt (Task 7, việc của người).
5. **Ngân sách đo:** Task 12 chạy 3 cấu hình × 370 câu + một model so sánh; ước tính ≤ 2 USD với giá hiện có trong `prices.json` (`gemini-3.1-flash-lite`, `gemini-3-flash-preview`). Vượt thì dừng.

## Global Constraints

- LLM không sinh số tiền; mọi số tiền = đúng một giá trị bằng chứng (`Evidence.ValueVnd`, `long`); phần trăm chỉ nằm trong `Text`; không `decimal/double` cho tiền.
- `userId` chỉ lấy từ `ToolContext`; công cụ chỉ đọc số đã tổng hợp, không trả giao dịch lẻ, ghi chú hay tên người nhận cho LLM.
- Dữ liệu đổi được (tri thức, tỉ lệ, cụm từ phạm vi, quyền) nằm trong DB/cấu hình admin; không hard-code. Nháp tri thức chỉ nhận nội dung đọc từ DB, không chèn câu chữ viết sẵn trong code.
- Evidence `id` và `key` ≤ 64 ký tự, duy nhất; tối đa 12 bằng chứng mỗi công cụ (bộ soạn dự phòng chỉ giữ 12).
- Không đụng backend 8090 và stack Docker đang chạy của chủ dự án; mọi môi trường đo dựng riêng, tên container tiền tố `finmate-t12-`, cổng chưa dùng (8080, 8090, 8000, 5433, 5434, 6379, 5005 đã bị chiếm), dọn sạch khi xong.
- Khóa API chỉ qua biến môi trường nạp từ tệp có sẵn, không in, không ghi vào repo/báo cáo/log.
- **Không chạy `--help` hay tham số lạ trên script chưa đọc mã** trong `ai-service/scripts/`: một số script không có argparse và `--help` từng chạy hàm export, xóa các CSV của bộ phân loại danh mục. Đọc nguồn trước; script nhắc tới `nlu_*`, `eval_*`, `audit_*` chỉ chạy theo cú pháp ghi trong README của chúng.
- Không commit `ai-service/agent_eval/results/`, tệp tạm, `.env`; `git add` từng đường dẫn; không push.
- Làm trong worktree riêng (nhánh mới từ `dev/tuan`), chạy `dotnet test` chỉ trong worktree vì backend 8090 khóa DLL ở checkout chính.

## Review Focus

1. Cụm từ cổng phạm vi sau khi siết: câu lạc đề chứa tên người/"nhóm chị"/"tổng chỉ huy" phải bị từ chối, nhưng "Tổng chi tháng này", "đi đâu hết tiền", "nhóm chi nhiều nhất" vẫn qua.
2. Admin lưu mẫu tỉ lệ sai (tổng ≠ 100, trùng slug, số không nguyên): phải bị từ chối ngay khi lưu với thông báo tiếng Việt, và mẫu rỗng vẫn lưu được.
3. Nhóm chi có slug rỗng (tên toàn emoji/chữ lạ) cùng hơn 7 nhóm: id/khóa bằng chứng vẫn duy nhất, tổng khoản chia + tiết kiệm = thu nhập.
4. Lệnh sinh nháp tri thức chạy hai lần: không tạo bản trùng; phương pháp đã tắt (`is_active = false`) không được sinh nháp; bản nháp không bao giờ tự thành Approved.
5. `compare_spending` khi kỳ trước không có dữ liệu, khi một danh mục mới xuất hiện (kỳ trước = 0, không chia cho 0), khi tổng hai kỳ bằng nhau (thay đổi 0): câu trả lời không bịa phần trăm và không hứa "giảm" khi không giảm.

---

## WAVE 1 — Sửa các việc đã hoãn

### Task 1: Siết cụm từ cổng phạm vi

**Files:**
- Modify: `ai-service/app/data/agent_scope.json` (cụm `tien minh`, `nhom chi`, nằm quanh dòng 131–134)
- Test: `ai-service/tests/unit/test_agent_gate.py`

**Interfaces:**
- Consumes: `gate.evaluate(text, default_rules(), None)`; các câu `sp-05` ("tổng chi"), `sp-07` ("nhóm chi"), `sp-10` ("tiền … đi đâu hết") trong `ai-service/agent_eval/dataset/questions.jsonl`.
- Produces: `in_scope` không còn `tien minh`; `nhom chi` thay bằng `nhom chi lon` và `nhom chi nhieu`; thêm `di dau het`; giữ `tong chi`.

- [ ] **Step 1: Viết test** `test_offtopic_names_and_phrases_are_refused_after_tightening` (các câu "Tiến Minh là ai?", "Nhóm chị em đi chơi ở đâu vui?", "Tổng chỉ huy quân đội là ai?" đều bị từ chối), và `test_new_spending_questions_still_pass` (mọi câu kind `spending`/`budget_plan` của bộ dữ liệu vẫn `in_scope`).
- [ ] **Step 2: Chạy** `cd ai-service && python -m pytest tests/unit/test_agent_gate.py -q`. Expected: test đầu FAIL.
- [ ] **Step 3: Sửa** `agent_scope.json` theo Interfaces; nếu một câu `sp-*` rơi ra ngoài thì thêm đúng cụm cần (không nới thêm cụm ngắn dễ trùng sau khi bỏ dấu).
- [ ] **Step 4: Chạy lại** Step 2 và `-k agent`. Expected: PASS.
- [ ] **Step 5: Ghi chú vận hành** một dòng trong `docs/agent-eval.md`: cụm đã seed vào DB dùng chung không tự biến mất; tắt bằng `is_active` ở trang admin.
- [ ] **Step 6: Commit** `fix(agent): tighten scope gate phrases that matched names and unrelated words`.

### Task 2: Admin lưu mẫu tỉ lệ sai thì bị từ chối

**Files:**
- Create: `backend/FinMate.Application/Agent/Tools/BudgetTemplate.cs`
- Modify: `backend/FinMate.Application/Agent/Tools/PlanBudgetTool.cs` (bỏ `ParseTemplate` riêng, dùng lớp mới), `backend/FinMate.Application/Admin/SystemConfig/SystemConfigDocumentValidator.cs` (dòng `RuleFor(x => x.Agent)`)
- Test: `backend/FinMate.Tests/Unit/Application/Agent/PlanBudgetToolTests.cs`, và test của validator trong `backend/FinMate.Tests/` (tìm bằng `grep -rl SystemConfigDocumentValidator`)

**Interfaces:**
- Produces: `public static class BudgetTemplate { public static IReadOnlyList<(string Slug, int Percent)>? TryParse(string? json); }` — trả `null` khi rỗng/null (không có mẫu) hoặc sai; cùng quy tắc hiện tại (JSON object, slug không rỗng không trùng, phần trăm nguyên dương, tổng đúng 100). Thêm `public static bool IsValidOrEmpty(string? json)`.
- Validator: `Agent.BudgetTemplate` phải thỏa `IsValidOrEmpty`, thông báo "Mẫu tỉ lệ ngân sách phải là JSON {slug: phần trăm nguyên dương}, tổng bằng 100".

- [ ] **Step 1: Viết test** `Validator_RejectsTemplateWhoseSumIsNot100`, `Validator_AcceptsEmptyTemplate`, `Validator_AcceptsValidTemplate`, `Validator_RejectsDuplicateSlugAndNonIntegerPercent`; giữ nguyên các test `PlanBudgetToolTests` hiện có về mẫu không hợp lệ (chúng phải vẫn xanh).
- [ ] **Step 2: Chạy** `cd backend && dotnet test FinMate.Tests --filter "FullyQualifiedName~PlanBudgetToolTests|FullyQualifiedName~SystemConfig"`. Expected: test validator FAIL.
- [ ] **Step 3: Cài** `BudgetTemplate` bằng cách chuyển nguyên logic `ParseTemplate`; nối vào validator và `PlanBudgetTool`.
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `fix(admin): reject an invalid agent budget template at save time`.

### Task 3: Nhóm gộp "khác" không dùng slug rỗng làm dấu hiệu

**Files:**
- Modify: `backend/FinMate.Application/Agent/Tools/PlanBudgetTool.cs` (`OtherSlug`, quanh dòng 24, 72–110)
- Test: `backend/FinMate.Tests/Unit/Application/Agent/PlanBudgetToolTests.cs`

**Interfaces:**
- Produces: nhóm gộp được đánh dấu bằng cờ (`bool IsOther`) trong bản ghi chia sẻ, không bằng `slug == ""`; id `alloc_other`, khóa `budget_alloc_other` giữ nguyên.

- [ ] **Step 1: Viết test** `EmptySlugCategoryDoesNotCollideWithFoldedBucket`: 9 nhóm trong đó một nhóm slug `""`; khẳng định id/khóa duy nhất, ≤ 64 ký tự, tổng khoản chia = `remaining` đúng.
- [ ] **Step 2: Chạy** `dotnet test --filter FullyQualifiedName~PlanBudgetToolTests`. Expected: FAIL.
- [ ] **Step 3: Cài**: thay sentinel bằng cờ; slug rỗng của nhóm thật được gán `uncategorized-<n>` hoặc `category-<n>` theo thứ tự (quyết định một cách, ghi vào báo cáo) để khóa vẫn duy nhất.
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `fix(agent): folded budget bucket flagged explicitly, not by an empty slug`.

### Task 4: Không lộ mẫu tỉ lệ qua `/app-config`

**Files:**
- Modify: `backend/FinMate.API/Controllers/AppConfigController.cs` (dòng tạo `PublicAppConfig`, thêm `Agent = new()` cạnh `Monitoring = ...`)
- Test: `backend/FinMate.Tests/Integration/Controllers/` (test `AppConfig` hiện có, `grep -l app-config`)

- [ ] **Step 1: Viết test** `AppConfig_DoesNotExposeAgentBudgetTemplate`: đặt mẫu hợp lệ qua admin, gọi `GET /api/v1/app-config` không đăng nhập, khẳng định phản hồi không chứa `budgetTemplate` và các section khác vẫn còn.
- [ ] **Step 2: Chạy** `dotnet test --filter FullyQualifiedName~AppConfig`. Expected: FAIL.
- [ ] **Step 3: Cài** theo mô tả; kiểm `ETag` vẫn đổi khi phiên bản cấu hình đổi.
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `fix(config): keep the agent section out of the anonymous app config`.

### Task 5: Chỉ dẫn cho planner khi mục tiêu ≥ thu nhập (bp-23)

**Files:**
- Modify: `backend/FinMate.Application/Agent/Tools/PlanBudgetTool.cs` (`Spec.Description`)
- Test: `backend/FinMate.Tests/Unit/Application/Agent/PlanBudgetToolTests.cs`

- [ ] **Step 1: Viết test** `SpecTellsPlannerToCallEvenWhenSaveIsNotBelowIncome`: mô tả công cụ chứa chỉ dẫn gọi công cụ kể cả khi `save_vnd` lớn hơn hoặc bằng `income_vnd` (công cụ sẽ trả `goal_exceeds_income`).
- [ ] **Step 2: Chạy** `dotnet test --filter FullyQualifiedName~PlanBudgetToolTests`. Expected: FAIL.
- [ ] **Step 3: Thêm** một câu tiếng Việt vào `Spec.Description`.
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS. Hiệu quả thật chỉ biết sau Task 12; nếu `bp-23` vẫn hỏi lại thì đổi nhãn dòng đó sang `clarify` ở Task 12.
- [ ] **Step 5: Commit** `feat(agent): tell the planner to call plan_budget even when the goal is not below income`.

---

## WAVE 2 — Tri thức cho RAG

### Task 6: Lệnh tạo nháp tri thức từ catalog phương pháp

**Files:**
- Create: `backend/FinMate.Application/Admin/Commands/AdminKnowledgeMethodSync.cs`
- Modify: `backend/FinMate.API/Controllers/Admin/AdminKnowledgeController.cs` (thêm endpoint), `backend/FinMate.API/Program.cs` (DI)
- Test: `backend/FinMate.Tests/Unit/Application/Admin/AdminKnowledgeMethodSyncTests.cs`

**Interfaces:**
- Consumes: `IFinanceMethodRepository` (danh sách phương pháp, `IsActive`, `Version`, `Name`, `Summary`, `WhyText`, `StarterSteps`, `ParamDefs`), `IAdminKnowledgeService.ListDocumentsAsync` và `SubmitAsync(adminId, body, ip, ct)` (body `{title, source_name, source_url?, as_of, effective_from, effective_to?, text}`).
- Produces: `POST /api/v1/admin/knowledge/sync-from-methods` (Staff trở lên, như `Submit`) trả `{created: [{methodCode, title}], skipped: [{methodCode, reason}]}`. Tiêu đề `"<Name> (v<Version>)"`, `source_name` `"FinMate — phương pháp <Name>"`, `as_of`/`effective_from` = hôm nay (giờ Việt Nam). `text` ghép từ `Summary`, `WhyText`, các bước `StarterSteps`, và các tham số mặc định đọc từ `ParamDefs` (nhãn + giá trị mặc định), tất cả lấy từ DB.

- [ ] **Step 1: Viết test**: `CreatesOneDraftPerActiveMethod`, `SecondRunSkipsExistingTitles`, `InactiveMethodsAreNotSynced`, `ChangedVersionCreatesNewDraft`, `TextContainsParamDefaultsFromDb` (dùng giá trị 50/30/20 trong dữ liệu thử, khẳng định từ chính `ParamDefs`), `NeverCallsApprove`.
- [ ] **Step 2: Chạy** `dotnet test --filter FullyQualifiedName~AdminKnowledgeMethodSyncTests`. Expected: FAIL.
- [ ] **Step 3: Cài** lệnh và endpoint; trùng được nhận biết bằng `ListDocumentsAsync` theo `search` = tiêu đề (mọi trạng thái, kể cả Retired).
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Cập nhật** `docs/agent-content-checklist.md` mục 2: đường nạp nhóm "của riêng FinMate" giờ qua lệnh này; người vẫn phải duyệt.
- [ ] **Step 6: Commit** `feat(admin): draft knowledge documents from the finance method catalog`.

### Task 7: Duyệt nội dung và gắn nhãn nguồn (việc của người, song song)

**Files:**
- Modify: `ai-service/agent_eval/dataset/questions.jsonl` (gắn `expect.relevant_sources` cho các câu `knowledge`)
- Không đổi mã: dùng trang admin *Tri thức* và *Giả định kinh tế*.

- [ ] **Step 1: Chạy** lệnh Task 6 trên môi trường dev, kiểm bản nháp (nội dung khớp app, không mâu thuẫn).
- [ ] **Step 2: Staff soạn** kiến thức chung có nguồn và giả định kinh tế theo `docs/agent-content-checklist.md` (mỗi số mở trang nguồn kiểm tra, `sourceUrl` là trang cụ thể, `asOf` là ngày số liệu).
- [ ] **Step 3: OpsManager/Admin duyệt** tất cả. Khóa/nháp chưa duyệt vẫn phải ra `knowledge_missing` / `assumption_missing`.
- [ ] **Step 4: Gắn `relevant_sources`** theo đúng tên tài liệu vừa duyệt cho từng câu `knowledge`; chạy `--kinds knowledge` ở Task 12 để có `retrieval_recall_at_5` thật.
- [ ] **Step 5: Commit** `docs(agent-eval): knowledge relevant_sources labels` (chỉ tệp dataset).

---

## WAVE 3 — So sánh chi tiêu giữa hai kỳ

### Task 8: Nguồn dữ liệu chi theo tháng và danh mục cho bộ đánh giá

**Files:**
- Modify: `backend/FinMate.Application/Agent/Forecasting/InMemoryFinancialDataSource.cs`, `backend/FinMate.API/Controllers/AgentEvalController.cs` (`SnapshotDto`), `ai-service/agent_eval/personas.py`, `ai-service/agent_eval/run.py`
- Test: `backend/FinMate.Tests/Unit/Application/Agent/InMemoryFinancialDataSourceTests.cs`, `backend/FinMate.Tests/Integration/Controllers/AgentEvalControllerTests.cs`, `ai-service/tests/unit/test_agent_personas.py`, `ai-service/tests/unit/test_agent_run.py`

**Interfaces:**
- Produces: `InMemoryFinancialDataSource` nhận thêm tham số cuối tùy chọn `IReadOnlyDictionary<DateOnly, IReadOnlyList<CategorySpendVnd>>? categorySpendByMonth` (khóa là ngày 1 của tháng). Khi có, `GetCategorySpendAsync(userId, from, to)` cộng các tháng có ngày 1 nằm trong `[from, to]`, gộp theo `Slug`, sắp theo `SpentVnd` giảm dần rồi `Slug` ordinal; khi không có, giữ nguyên hành vi hiện tại. `SnapshotDto` thêm tham số cuối tùy chọn `List<MonthlyCategorySpendDto>? CategorySpendByMonth` (`record MonthlyCategorySpendDto(string Month /*yyyy-MM*/, List<CategorySpendDto> Items)`). Persona thêm `category_spend_by_month` cho 12 tháng trọn gần nhất tính từ ngày chạy (giờ Việt Nam), tổng mỗi tháng đúng bằng `monthly_spend` tương ứng làm tròn nghìn.

- [ ] **Step 1: Viết test**: `ByMonthSumsMonthsInsideRangeAndSortsDeterministically`, `WithoutByMonthKeepsLegacyBehaviour`, `PersonaMonthlyCategorySpendMatchesMonthlySpend` (3 hồ sơ × 30 hạt giống, tổng đúng), `RunSendsCategorySpendByMonth`, `SnapshotWithByMonthReachesTheSource`.
- [ ] **Step 2: Chạy** backend `dotnet test --filter "FullyQualifiedName~InMemoryFinancialDataSourceTests|FullyQualifiedName~AgentEvalControllerTests"` và Python `python -m pytest tests/unit/test_agent_personas.py tests/unit/test_agent_run.py -q`. Expected: FAIL.
- [ ] **Step 3: Cài** theo Interfaces; dùng RNG riêng cho phần danh mục theo tháng để các chuỗi thu/chi và dữ liệu hiện có của persona không đổi (như đã làm với `category_spend`).
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(agent-eval): monthly category spend in simulated snapshots`.

### Task 9: Công cụ `compare_spending`

**Files:**
- Create: `backend/FinMate.Application/Agent/Tools/CompareSpendingTool.cs`
- Modify: `backend/FinMate.API/Program.cs`
- Test: `backend/FinMate.Tests/Unit/Application/Agent/CompareSpendingToolTests.cs`

**Interfaces:**
- Consumes: Task 8 (`GetCategorySpendAsync` theo khoảng), `VietnamTime.DateOf(context.Now)`, `EvidenceKey.Bounded` (khóa ≤ 64 ký tự, đã có ở `Evidence.cs`).
- Produces: công cụ `compare_spending`, tham số `months` (`int`, bắt buộc, 1..6). Kỳ hiện tại = `months` tháng TRỌN gần nhất (không gồm tháng đang dở); kỳ trước = `months` tháng trọn ngay trước đó. Bằng chứng: `period_current` (key `period_current`, tổng chi), `period_previous` (key `period_previous`), `period_change` (key `period_change`, giá trị tuyệt đối của chênh lệch, `Text` nói rõ "tăng" hay "giảm" hay "không đổi" và % nguyên làm tròn nửa lên khi kỳ trước > 0), tối đa 3 nhóm đổi nhiều nhất `chg_1..chg_3` (key `category_change:<slug>` giới hạn 64, giá trị = chênh lệch tuyệt đối của nhóm, `Text` = "<Tên>: tăng|giảm|mới xuất hiện …"). Mã thiếu dữ liệu: `no_previous_period` khi tổng kỳ trước ≤ 0, `no_spending_history` khi cả hai kỳ trống.

- [ ] **Step 1: Viết test** (số cụ thể): `ReportsIncreaseWithAbsoluteChangeAndPercent`, `ReportsDecrease`, `EqualTotalsSayUnchangedWithoutPercent`, `NewCategoryHasNoPercentAndNoDivisionByZero`, `TopThreeCategoriesByAbsoluteChange` (sắp ổn định theo `Slug` khi bằng nhau), `NoPreviousPeriodIsInsufficient`, `RejectsMonthsOutsideRange` (qua `ToolExecutor`, 0 và 7), `CurrentMonthIsExcluded` (khẳng định hai khoảng ngày đúng).
- [ ] **Step 2: Chạy** `dotnet test --filter FullyQualifiedName~CompareSpendingToolTests`. Expected: FAIL.
- [ ] **Step 3: Cài** `CompareSpendingTool(IFinancialDataSource source)` và đăng ký DI cạnh các công cụ khác; mô tả công cụ nói rõ chỉ so các tháng trọn.
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(agent): compare_spending tool`.

### Task 10: Câu hỏi đánh giá, cổng phạm vi và tài liệu cho `compare_spending`

**Files:**
- Modify: `ai-service/agent_eval/dataset/questions.jsonl`, `ai-service/agent_eval/dataset/README.md`, `ai-service/app/data/agent_scope.json` (nếu cần), `docs/agent-api.md`
- Test: `ai-service/tests/unit/test_agent_dataset.py`, `ai-service/tests/unit/test_agent_gate.py`

**Interfaces:**
- Consumes: Task 8–9. Produces: kind mới `compare` (≥ 20 dòng, id `cp-01…`, `expect.tools = ["compare_spending"]`, `numeric_truth = {"months": N}`), gồm cách nói khác nhau ("tháng trước tôi chi nhiều hơn tháng kia không", "so với quý trước", không dấu, một vài câu hỏi lại khi không nói kỳ nào nếu bộ chấm biểu diễn được `clarify`). Dòng mới của `docs/agent-api.md` cho `compare_spending`.

- [ ] **Step 1: Viết test**: `test_dataset_has_compare_kind_with_expected_tools`, `test_gate_lets_compare_questions_through`.
- [ ] **Step 2: Chạy** `python -m pytest tests/unit/test_agent_dataset.py tests/unit/test_agent_gate.py -q`. Expected: FAIL.
- [ ] **Step 3: Thêm** các dòng dataset (giữ nguyên mọi dòng cũ, chỉ nối thêm), cập nhật README bảng kind; nếu cổng chặn câu hợp lệ, thêm cụm vào `agent_scope.json` (tránh cụm ngắn dễ trùng chữ khác khi bỏ dấu).
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(agent-eval): compare questions and docs for compare_spending`.

---

## WAVE 4 — Đo lại

### Task 11: Đo cổng phạm vi và chat parse khi tắt Rasa

**Files:**
- Create: `docs/rasa-off-measurement.md`
- Không đổi mã.

- [ ] **Step 1: Đọc mã** các script `ai-service/scripts/eval_nlu_pipeline.py`, `audit_overrides.py` và `ai-service/tests/unit/test_rasa_live_coverage.py` trước khi chạy (xem Global Constraints về `--help`); ghi cú pháp thật vào báo cáo.
- [ ] **Step 2: Chạy** các bộ có sẵn (`ai-service/tests/fixtures/eval/`: `rasa_coverage.jsonl`, `heldout_intent.jsonl`, `chat_cases.jsonl`, `money_cases.jsonl`, `period_cases.jsonl`...) với `RASA_URL` để trống (chỉ luật) và, để so, với Rasa bật nếu môi trường dựng được. Cổng phạm vi của agent: chạy `gate.evaluate` với `nlu=None` trên các câu `out_of_scope`, `adversarial` và các câu hợp lệ của bộ đánh giá agent.
- [ ] **Step 3: Viết** `docs/rasa-off-measurement.md`: bảng chính xác từng bộ (chỉ luật so với luật + Rasa), nhóm câu an toàn (xóa dữ liệu, tiêm lệnh, tư vấn đầu tư) phải bị chặn 100%, danh sách câu hợp lệ bị từ chối chỉ vì thiếu Rasa. Kết luận chỉ nêu điều số liệu chứng minh; không đề xuất xóa nếu nhóm an toàn dưới 100%. Nếu số liệu cần Rasa chạy mà không dựng được, ghi "Chưa đo" và lý do, không điền số.
- [ ] **Step 4: Commit** `docs(rasa): rules-only measurement` (chỉ tệp tài liệu).

### Task 12: Đo thật toàn bộ và chốt bảng số

**Files:**
- Modify: `docs/agent-eval.md` (điền bảng "Bảng số cuối và chọn mô hình", cập nhật mục kết quả 2026-11-09 với số đo lại), `ai-service/agent_eval/dataset/questions.jsonl` (chỉ để đổi nhãn `bp-23` nếu cần)

**Interfaces:**
- Consumes: Task 1–10, môi trường riêng `finmate-t12-*` (backend + ai-service chạy mã mới từ worktree, Postgres/pgvector/Redis tạm), khóa Gemini nạp từ tệp có sẵn vào biến môi trường (không in).

- [ ] **Step 1: Dựng môi trường riêng** như đợt đo trước: tiền tố `finmate-t12-`, cổng chưa dùng, `AGENT_EVAL_ENABLED=true` cho cả backend và ai-service; kiểm 401 khi không có khóa, 200 khi có.
- [ ] **Step 2: Nạp dữ liệu thật cho đo tri thức**: duyệt xong Task 7 trong DB đo (nếu Task 7 chưa xong thì bỏ qua `knowledge` và `retrieval_recall_at_5` và ghi "Chưa đo").
- [ ] **Step 3: Chạy thử** `python -m agent_eval.run --config full --limit 10 --pause-seconds 15 --backend-url <backend-đo>`; sau đó `--config full` đủ 370+ câu; rồi `no_verifier`, `no_rag` (nếu RAG có tài liệu), `rasa_baseline`; một lượt `full` với model so sánh trong `prices.json`. Dùng `--resume` khi 429/ReadTimeout; dừng nếu vượt ngân sách trong Quyết định 5.
- [ ] **Step 4: Ghi bảng**: mỗi ô có ngày, model, số câu; tách theo kind và theo hồ sơ (`stable_worker`, `freelancer`, `student`); nêu rõ tool/plan accuracy có và không có các dòng `bp-23`/`bp-24` nếu còn lệch; theo dõi: số lần verifier từ chối (`number_unsupported`) sau khi thêm bằng chứng `remaining`, tỉ lệ rơi về câu mẫu, p50/p95, chi phí thật.
- [ ] **Step 5: Nếu `bp-23` vẫn hỏi lại** sau Task 5 thì đổi nhãn dòng đó sang `clarify` và ghi lý do trong README dataset.
- [ ] **Step 6: Chọn mô hình** theo quy tắc đã ghi trong `docs/agent-eval.md` (rẻ nhất mà đạt mọi ngưỡng); không đổi cấu hình khi chưa có số.
- [ ] **Step 7: Dọn dẹp** bắt buộc: dừng tiến trình backend đo, `docker rm -f` chỉ các `finmate-t12-*`, xóa mạng/image/volume cùng tiền tố, xóa tệp môi trường tạm; kiểm `docker ps -a` còn nguyên các container của chủ dự án.
- [ ] **Step 8: Commit** `docs(agent-eval): full measured results and model choice` (chỉ `docs/agent-eval.md` và dataset nếu đổi nhãn).
