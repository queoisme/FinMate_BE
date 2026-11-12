# AI Financial Agent — Thống kê chi tiêu, chia ngân sách, nạp tri thức

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agent trả lời được "tháng này tôi tiêu vào đâu?" và "thu 2 triệu, muốn tiết kiệm 1 triệu thì chia tiền thế nào?" bằng số do công cụ tính (không phải LLM), thay vì từ chối `insufficient_data`.

**Architecture:** Thêm hai công cụ vào danh sách cho phép của backend (`spending_summary`, `plan_budget`). Cả hai chỉ đọc số đã tổng hợp qua `IFinancialDataSource` (thêm một hàm theo danh mục), trả bằng chứng như các công cụ cũ; bộ lập kế hoạch Python tự biết công cụ mới qua `ToolSpec` (không hard-code tên). Song song, nạp tri thức qua admin để RAG có gì để trích.

**Tech Stack:** ASP.NET Core 9 (`FinMate.Application/Agent`), Python 3.11 (`ai-service/app/agent`, `agent_eval`), pytest, xUnit, PostgreSQL.

**Spec:** `docs/agent-api.md` (hợp đồng, bảng công cụ), `docs/agent-eval.md` (chỉ số), `docs/agent-content-checklist.md` (nội dung cần nạp), kế hoạch trước `docs/superpowers/plans/2026-11-08-ai-agent-measurement.md`.

## Quyết định đã chốt (2026-11-08: chủ dự án chấp nhận đề xuất của cả ba mục)

1. **Cơ sở chia tiền khi người dùng chưa có lịch sử theo danh mục** (ca sinh viên mới của Moni). Đề xuất: ưu tiên tỉ lệ chi theo danh mục của chính người dùng (≥ 1 tháng đầy đủ); không có thì dùng **mẫu tỉ lệ do admin sửa được** (khóa cấu hình hệ thống `agent.budgetTemplate`, JSON `{slug: phần trăm}`, tổng = 100); không có cả hai thì `insufficient_data` (`no_budget_basis`). Không hard-code tỉ lệ vào code.
2. **Thống kê có gồm tháng hiện tại chưa trọn không?** Đề xuất: có (người dùng hỏi "tháng này"), và ghi rõ trong bằng chứng là "đến hôm nay". Khác với dự báo, vốn loại tháng chưa trọn.
3. Báo cáo/thống kê từng bị đưa ra ngoài phạm vi của luồng chính (`finmate-financial-management-flow-decisions`). Kế hoạch này coi "hỏi trong chat" là một khả năng của agent, không thêm màn hình báo cáo. Nếu bạn muốn giữ nguyên quyết định cũ thì bỏ Task 1–2.

## Global Constraints

- LLM không sinh số tiền; mọi số tiền = giá trị đúng một bằng chứng (`Evidence.ValueVnd`, `long`, không `decimal/double`). Tỉ lệ phần trăm đi trong `Text`, không phải số tiền.
- `userId` chỉ lấy từ `ToolContext`; công cụ mới không nhận tham số danh tính. Mọi truy vấn lọc theo `userId`.
- Công cụ chỉ đọc số đã tổng hợp; không trả giao dịch lẻ, ghi chú hay tên người nhận cho LLM.
- Dữ liệu đổi được (tỉ lệ mẫu, cụm từ phạm vi, tri thức) nằm trong DB/cấu hình admin, không hard-code.
- Đơn vị tiền theo quy ước hiện có: giá trị từ `IReportRepository` (`SpentCents`) được dùng nguyên làm đồng VND, như `RepositoryFinancialDataSource` đang làm.
- Không đụng DB/backend dùng chung (8080); đo trên backend eval cổng 8090. Không commit kết quả thô trong `agent_eval/results/`.
- Không commit tệp tạm; `git add` từng đường dẫn cụ thể.

## Review Focus

1. Mục tiêu tiết kiệm ≥ thu nhập (`save_vnd ≥ income_vnd`): phải `insufficient_data` (`goal_exceeds_income`), không ra bảng chi tiêu bằng 0 hay âm.
2. Tổng các khoản chia + khoản tiết kiệm phải bằng đúng thu nhập (làm tròn không được làm lệch vài nghìn đồng), vì đây chính là lỗi Moni mắc phải.
3. Người dùng nói "2 triệu" mà LLM điền `income_vnd = 20000000`: bộ lập kế hoạch phải sửa theo số đọc được từ câu hỏi hoặc hỏi lại, không tin số LLM.
4. Giao dịch chưa phân loại (`CategoryId = null`): phải gom vào một nhóm "Chưa phân loại", không rơi mất khỏi tổng.
5. Người dùng chưa có giao dịch nào: `spending_summary` ra `insufficient_data`, không trả "0 đồng".

---

### Task 1: Nguồn dữ liệu theo danh mục

**Files:**
- Modify: `backend/FinMate.Application/Agent/Forecasting/IFinancialDataSource.cs`, `RepositoryFinancialDataSource.cs`, `SwitchableFinancialDataSource.cs`, `InMemoryFinancialDataSource.cs`
- Test: `backend/FinMate.Tests/Unit/Application/Agent/RepositoryFinancialDataSourceTests.cs`

**Interfaces:**
- Produces: `public sealed record CategorySpendVnd(string Slug, string Name, long SpentVnd);` và `Task<IReadOnlyList<CategorySpendVnd>> GetCategorySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)` trên `IFinancialDataSource`, sắp theo `SpentVnd` giảm dần. `InMemoryFinancialDataSource` nhận thêm tham số cuối `IReadOnlyList<CategorySpendVnd>? categorySpend = null`.

- [ ] **Step 1: Viết test** `CategorySpend_MapsNullCategoryToUncategorized`, `CategorySpend_SortsByAmountDescending`, `CategorySpend_PassesUserIdAndRangeToRepository` (mock `IReportRepository.GetCategorySpendAsync`).
- [ ] **Step 2: Chạy** `cd backend && dotnet test --filter FullyQualifiedName~RepositoryFinancialDataSourceTests`. Expected: FAIL (hàm chưa có).
- [ ] **Step 3: Cài** hàm trên bằng `IReportRepository.GetCategorySpendAsync`; `CategoryId == null` → `("uncategorized", "Chưa phân loại")`; slug null thì dùng `"category"`. `Switchable` chuyển tiếp, `InMemory` trả danh sách cấu hình (mặc định rỗng, bỏ qua `userId` như hai hàm cũ).
- [ ] **Step 4: Chạy lại** lệnh Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(agent): per-category spend on the agent data source`.

### Task 2: Công cụ `spending_summary`

**Files:**
- Create: `backend/FinMate.Application/Agent/Tools/SpendingSummaryTool.cs`
- Modify: `backend/FinMate.API/Program.cs` (cạnh dòng đăng ký `RunwayTool`)
- Test: `backend/FinMate.Tests/Unit/Application/Agent/SpendingSummaryToolTests.cs`

**Interfaces:**
- Consumes: Task 1; `VietnamTime.YearMonthOf(DateTimeOffset)`; `IAgentTool`, `ToolResult`, `Evidence` (theo `RunwayTool`).
- Produces: công cụ tên `spending_summary`, tham số `months` (`int`, bắt buộc, 1..12 = số tháng gần nhất tính cả tháng này). Bằng chứng: `spend_total` (key `spend_total`, `Confirmed`), tối đa 5 nhóm `cat_1..cat_5` (key `category_spend:<slug>`, `Text` = `"<tên>: <n>% tổng chi"` với `n` là số nguyên) và `cat_other` khi còn nhóm khác.

- [ ] **Step 1: Viết test** `ReturnsTotalAndTopFiveCategories`, `GroupsRemainingCategoriesIntoOther`, `CategoryAmountsPlusOtherEqualTotal` (khẳng định tổng bằng nhau bằng số cụ thể), `NoTransactionsReturnsInsufficientNoSpendingHistory`, `RejectsMonthsOutsideRange` (qua `ToolExecutor`, `months = 0` và `13` → `Rejected("invalid_argument:months")`).
- [ ] **Step 2: Chạy** `dotnet test --filter FullyQualifiedName~SpendingSummaryToolTests`. Expected: FAIL.
- [ ] **Step 3: Cài** `SpendingSummaryTool(IFinancialDataSource source)`; khoảng ngày = ngày 1 của tháng `now − (months−1)` đến hôm nay theo giờ Việt Nam; tổng ≤ 0 → `Insufficient("no_spending_history")`; `Text` của `spend_total` nói rõ "đến hôm nay" khi gồm tháng hiện tại. Đăng ký DI.
- [ ] **Step 4: Chạy lại** lệnh Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(agent): spending_summary tool`.

### Task 3: Bộ lập kế hoạch kiểm mọi tham số tiền, không chỉ `price_vnd`

**Files:**
- Modify: `ai-service/app/agent/planner.py` (`_reconcile_prices` và nơi gọi nó)
- Test: `ai-service/tests/unit/test_agent_planner.py`

**Interfaces:**
- Produces: tập tên tham số tiền `MONEY_ARGS = ("price_vnd", "income_vnd", "save_vnd")`; mỗi tham số trong tập này, nếu có, phải trùng một số tiền đọc được từ câu hỏi/ngữ cảnh (cùng quy tắc `price_vnd` hiện nay: trùng thì giữ; đúng một số trong câu thì thay; còn lại bỏ bước và hỏi lại).

- [ ] **Step 1: Viết test** `test_income_vnd_from_llm_must_match_a_number_in_the_question` (câu "ba mẹ gửi 2 triệu, tiết kiệm 1 triệu", LLM điền `income_vnd=20000000` → sửa về 2000000 hoặc hỏi lại, không giữ 20000000), `test_save_vnd_not_in_question_is_dropped_and_clarified`, `test_price_vnd_behaviour_unchanged`.
- [ ] **Step 2: Chạy** `cd ai-service && pytest tests/unit/test_agent_planner.py -q`. Expected: FAIL ở hai test đầu.
- [ ] **Step 3: Cài**: tổng quát hóa `_reconcile_prices` theo `MONEY_ARGS`; với nhiều tham số tiền trong một bước thì mỗi tham số phải khớp một số *khác nhau* trong câu (hai số 2 triệu và 1 triệu không được cùng chỉ về một số). Giữ nguyên thông điệp hỏi lại hiện có, thêm câu hỏi lại cho thu nhập/mục tiêu bằng một hằng cạnh `CLARIFY_PRICE`.
- [ ] **Step 4: Chạy lại** lệnh Step 2. Expected: PASS, kèm cả bộ `pytest tests/unit -q -k agent` không hỏng thêm.
- [ ] **Step 5: Commit** `feat(agent): planner verifies every money argument against the question`.

### Task 4: Công cụ `plan_budget`

**Files:**
- Create: `backend/FinMate.Application/Agent/Tools/PlanBudgetTool.cs`
- Modify: `backend/FinMate.API/Program.cs`
- Test: `backend/FinMate.Tests/Unit/Application/Agent/PlanBudgetToolTests.cs`

**Interfaces:**
- Consumes: Task 1 (`GetCategorySpendAsync`), `IFinancialDataSource.GetMonthlySeriesAsync` (trung vị `MonthlySeries.Income` khi người dùng không nêu thu nhập); bộ đọc cấu hình hệ thống hiện có (tìm bằng `grep LimitSettings`) cho `agent.budgetTemplate`.
- Produces: công cụ `plan_budget`, tham số `save_vnd` (`int`, bắt buộc, 1.000..100.000.000.000) và `income_vnd` (`int`, tùy chọn, cùng khoảng). Bằng chứng: `income` (`UserEntered` nếu có tham số, `Estimated` nếu lấy từ lịch sử), `save` (`UserEntered`), một dòng `alloc_<slug>` cho mỗi nhóm (`Estimated`), `savings_rate` (số nguyên %, chỉ ở `Text`). Thứ tự nguồn tỉ lệ: chi theo danh mục của chính người dùng 3 tháng trọn gần nhất → mẫu admin → `Insufficient("no_budget_basis")`.

- [ ] **Step 1: Viết test**: `AllocationsPlusSavingEqualIncome` (thu 2.000.000, tiết kiệm 1.000.000, ba nhóm với tỉ lệ cho trước → tổng các `alloc_*` = 1.000.000 đúng), `RoundsToThousandAndAbsorbsRemainderInLargestBucket`, `SavingEqualOrAboveIncomeIsInsufficientGoalExceedsIncome`, `UsesUserOwnShareBeforeTemplate`, `FallsBackToAdminTemplate`, `InvalidTemplateSumIsTreatedAsMissing` (tổng ≠ 100), `NoBasisIsInsufficientNoBudgetBasis`, `MissingIncomeFallsBackToMedianIncome`.
- [ ] **Step 2: Chạy** `dotnet test --filter FullyQualifiedName~PlanBudgetToolTests`. Expected: FAIL.
- [ ] **Step 3: Cài** `PlanBudgetTool`: `còn = income − save` (≤ 0 → `goal_exceeds_income`); mỗi nhóm = `⌊còn × tỉ lệ / 100 / 1000⌋ × 1000`; phần dư dồn vào nhóm lớn nhất. Đọc mẫu từ cấu hình, kiểm tổng = 100 và slug không rỗng; sai thì coi như thiếu. Đăng ký DI. Thêm khóa `agent.budgetTemplate` vào nơi khai báo cấu hình hệ thống hiện có (cùng cách `limits.*`); **không** seed số mặc định trong code (theo quyết định 1; nhập qua admin).
- [ ] **Step 4: Chạy lại** lệnh Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(agent): plan_budget tool, shares from user history or admin template`.

### Task 5: Nối vào bộ đánh giá và cổng phạm vi

**Files:**
- Modify: `backend/FinMate.API/Controllers/AgentEvalController.cs` (`SnapshotDto` thêm `List<CategorySpendDto>? CategorySpend`), `ai-service/agent_eval/personas.py`, `ai-service/agent_eval/run.py` (gửi `categorySpend`), `ai-service/agent_eval/dataset/questions.jsonl`
- Test: `ai-service/tests/unit/test_agent_personas.py`, `ai-service/tests/unit/test_agent_dataset.py`, `ai-service/tests/unit/test_agent_gate.py`, `backend/FinMate.Tests/Integration/Controllers/AgentEvalControllerTests.cs`

**Interfaces:**
- Consumes: Task 1 (`InMemoryFinancialDataSource` nhận `categorySpend`), Task 4 (tên công cụ).
- Produces (backend): `public sealed record CategorySpendDto(string Slug, string Name, long SpentVnd);` trong `AgentEvalController`; `SnapshotDto` thêm tham số cuối `List<CategorySpendDto>? CategorySpend = null`, và controller đổi sang `CategorySpendVnd` khi dựng `InMemoryFinancialDataSource`.
- Produces: `persona.snapshot["category_spend"] = [{"slug", "name", "spent_vnd"}]` (tổng `spent_vnd` bằng tổng chi 3 tháng gần nhất của hồ sơ); hai nhóm câu hỏi mới `spending` (≥ 20) và `budget_plan` (≥ 20) với `expect.tools` là `["spending_summary"]` / `["plan_budget"]`; câu `bp-01` là đúng câu Moni ("Ba mẹ gửi 2 triệu/ tháng … tiết kiệm được 1.000.000đ?") với `numeric_truth: {"income_vnd": 2000000, "save_vnd": 1000000}`.

- [ ] **Step 1: Viết test**: `test_persona_category_spend_sums_to_recent_spend`, `test_dataset_has_spending_and_budget_plan_kinds_with_expected_tools`, `test_gate_lets_new_question_kinds_through` (chạy `gate.evaluate` với `default_rules()` trên mọi câu hai nhóm mới), `snapshot_with_category_spend_reaches_the_tool` (controller test: snapshot có `categorySpend` → `spending_summary` đọc đúng số).
- [ ] **Step 2: Chạy** `cd ai-service && pytest tests/unit/test_agent_personas.py tests/unit/test_agent_dataset.py tests/unit/test_agent_gate.py -q` và `cd backend && dotnet test --filter FullyQualifiedName~AgentEvalControllerTests`. Expected: FAIL.
- [ ] **Step 3: Cài** theo các Interfaces trên. Nếu `gate.evaluate` chặn câu mới thì thêm cụm từ qua bảng `agent_scope_cues` (migration/seed dữ liệu qua đường admin hiện có), không sửa regex trong code.
- [ ] **Step 4: Chạy lại** hai lệnh Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(agent-eval): category spend in snapshots, spending and budget_plan questions`.

### Task 6: Chạy thật, ghi số, cập nhật hợp đồng

**Files:**
- Modify: `docs/agent-api.md` (bảng công cụ: thêm hai hàng, cột "Thiếu dữ liệu" ghi `no_spending_history`, `goal_exceeds_income`, `no_budget_basis`), `docs/agent-eval.md` (mục Kết quả)

**Interfaces:**
- Consumes: Task 5, backend eval cổng 8090 (xem memory `finmate-agent-adhoc-eval-run`), khóa qua biến môi trường.

- [ ] **Step 1: Chạy thử 5 câu** `python -m agent_eval.run --config full --kinds budget_plan --limit 5 --pause-seconds 15 --backend-url http://localhost:8090`. Expected: không lỗi 404/401; xem tay câu `bp-01`: có bảng chia tiền, tổng bằng 2.000.000.
- [ ] **Step 2: Chạy đủ** hai nhóm mới (`--kinds spending,budget_plan`). Expected: `unsupported_claim_rate = 0`, `numeric_faithfulness = 1`, `tool_accuracy ≥ 0,9`; vượt hạn mức token đã thỏa thuận thì dừng.
- [ ] **Step 3: Ghi bảng số** vào `docs/agent-eval.md` (ngày, model, số câu, lỗi `ReadTimeout` nếu có) và cập nhật `docs/agent-api.md`.
- [ ] **Step 4: Commit** `docs(agent): spending_summary and plan_budget, first measured results`.

### Task 7: Nạp tri thức cho RAG và đo truy xuất (làm song song, việc của người)

**Files:**
- Modify: `ai-service/agent_eval/dataset/questions.jsonl` (gắn `expect.relevant_sources` cho các câu `knowledge`)
- Không có thay đổi mã: nạp qua trang admin *Tri thức* và *Giả định kinh tế* (`/api/v1/admin/knowledge`, `/api/v1/admin/assumptions`).

- [ ] **Step 1: Soạn nháp** (Staff) các tài liệu thuộc nhóm "của riêng FinMate" trong `docs/agent-content-checklist.md` mục 2, lấy nội dung các phương pháp từ chính catalog trong hệ thống; mỗi số lấy từ trang nguồn do người nhập mở, không điền theo trí nhớ.
- [ ] **Step 2: Duyệt** (OpsManager hoặc Admin). Khóa còn nháp phải vẫn ra `knowledge_missing`/`assumption_missing`.
- [ ] **Step 3: Gắn `relevant_sources`** theo đúng tên tài liệu đã duyệt cho từng câu `knowledge`.
- [ ] **Step 4: Chạy** `python -m agent_eval.run --config full --kinds knowledge --pause-seconds 15 --backend-url http://localhost:8090`. Expected: `retrieval_recall_at_5` là một số thật (không `n/a`, không 1,0 do chia cho 0); ghi vào `docs/agent-eval.md`.
- [ ] **Step 5: Commit** `docs(agent-eval): knowledge labels and retrieval recall`.
