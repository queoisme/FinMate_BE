# Kế hoạch tài chính ban đầu từ khảo sát — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Làm xong khảo sát là người dùng có bản nháp kế hoạch tháng (thu → cố định → hạn mức từng danh mục → dư), sửa được, áp dụng thành ngân sách theo danh mục.

**Architecture:** Khảo sát thêm loại câu `amount` và vai trò kế hoạch (`income/fixed/flexible/savings_balance`) trong DB. Mỗi phương pháp có cột `allocation` (nhóm % → danh mục). Hàm thuần `FinancialPlanCalculator` tính kế hoạch; `GET /me/financial-plan/draft` ráp dữ liệu và gọi nó; app áp dụng qua `POST /forecast/categories/apply` có sẵn. AI không đụng tới số tiền.

**Tech Stack:** ASP.NET Core 9 + EF Core (Postgres, xUnit + FluentAssertions); Flutter (Riverpod, go_router, Dio) ở `E:\Ki 9\Capstone_Project\FinMate_FE` nhánh `dev/tuan`; React/Vite ở `E:\Ki 9\Capstone_Project\FinMate_Admin` (không phải git).

**Spec:** `docs/superpowers/specs/2026-11-23-onboarding-financial-plan-design.md`

## Global Constraints

- Tiền là `long` đồng VND (cột `*_cents`/`*_vnd` đều là đồng), không float/decimal. Mọi số trong kế hoạch làm tròn **xuống** 1.000 đồng.
- Không log số tiền khảo sát; không gửi số tiền sang ai-service (AI chỉ nhận signals như cũ).
- Không viết cứng danh mục, khoảng %, tỉ lệ nhóm trong code: nằm trong seed + DB, admin sửa được. Seeder chỉ thêm cái chưa có, không ghi đè.
- Mã (`code`) câu hỏi, đáp án, phương pháp là bất biến.
- Repository lọc theo `userId` lấy từ JWT.
- Backend chạy trên nhánh mới `feat/onboarding-financial-plan` cắt từ `dev/tuan` của `E:\Ki 9\Model AI\FinMate_BE`. Không build lại stack dev dùng chung (cổng 8080).
- Chỉ `git add` từng đường dẫn cụ thể; không commit file thử tạm.
- Mobile: tuân `FinMate_FE/CLAUDE.md` (token `AppColors/AppSpacing/AppTypography`, icon `lucide_icons_flutter`, skill UI).

## Review Focus

1. **Người dùng bỏ qua câu thu nhập** → draft trả lỗi `FINANCE_PLAN_NO_INCOME`, app hiện lời mời nhập thu nhập, không crash, không ra kế hoạch toàn số 0. (Test ở Task 6.)
2. **Khai chi linh hoạt > 100% thu nhập** (chọn ≥30% cho nhiều danh mục) → kế hoạch vẫn ra, các nhóm có trần bị co, nhóm không trần giữ nguyên nên `surplus` có thể âm và có cờ `deficit`. (Test ở Task 5.)
3. **Admin tắt một câu flexible hoặc một danh mục hệ thống sau khi người dùng đã trả lời** → câu trả lời cũ vẫn được tính nếu danh mục còn; danh mục đã tắt bị bỏ khỏi kế hoạch, không 500. (Test ở Task 6.)
4. **Gửi lại khảo sát với số tiền âm hoặc chữ** → 400/422 `SURVEY_ANSWER_INVALID`, không lưu gì. (Test ở Task 2.)
5. **Phương pháp đang dùng chưa có `allocation`** (DB cũ, admin chưa nhập) → coi như một nhóm `spend` không trần chứa mọi danh mục: kế hoạch = chi khai báo, không lỗi. (Test ở Task 5 và 6.)

---

### Task 1: Schema và hằng số

**Files:**
- Modify: `backend/FinMate.Domain/Entities/SurveyQuestion.cs`, `SurveyOption.cs`, `UserSurveyAnswer.cs`, `FinanceMethod.cs`
- Modify: `backend/FinMate.Domain/Entities/FinanceSignals.cs` (thêm hằng)
- Modify: `backend/FinMate.Infrastructure/Persistence/Configurations/SurveyQuestionConfiguration.cs`, `SurveyOptionConfiguration.cs`, `UserSurveyAnswerConfiguration.cs`, và configuration của `FinanceMethod`
- Create: migration `AddSurveyAmountsAndMethodAllocation`
- Test: `backend/FinMate.Tests/Integration/FinanceMethodSchemaTests.cs`

**Interfaces — Produces:**
- `SurveyQuestion.AnswerType: string` (mặc định `"choice"`), `SurveyQuestion.CategorySlug: string?`, `SurveyQuestion.PlanRole: string?`
- `SurveyOption.ShareMinPct: int?`, `SurveyOption.ShareMaxPct: int?`
- `UserSurveyAnswer.AmountVnd: long?`; `OptionCode` vẫn `string` (rỗng `""` khi là câu amount)
- `FinanceMethod.Allocation: string` jsonb, mặc định `"[]"`
- `static class SurveyAnswerTypes { Choice = "choice"; Amount = "amount"; }`
- `static class SurveyPlanRoles { Income = "income"; Fixed = "fixed"; Flexible = "flexible"; SavingsBalance = "savings_balance"; All }`
- `static class AllocationKinds { Spend = "spend"; Save = "save"; }`

- [ ] **Step 1: Test schema** — trong `FinanceMethodSchemaTests` thêm `NewSurveyAndAllocationColumns_HaveTheirDefaults`: insert một `SurveyQuestion` không đặt `AnswerType` → đọc lại `"choice"`; insert `FinanceMethod` không đặt `Allocation` → `"[]"`; insert `UserSurveyAnswer` với `OptionCode = ""`, `AmountVnd = 12_000_000` → đọc lại đúng.
- [ ] **Step 2:** `dotnet test --filter FullyQualifiedName~FinanceMethodSchemaTests` → FAIL (thuộc tính chưa có).
- [ ] **Step 3:** Thêm thuộc tính + hằng + cấu hình EF (`answer_type varchar(16) not null default 'choice'`, `category_slug varchar(64) null`, `plan_role varchar(32) null`, `share_min_pct int null`, `share_max_pct int null`, `amount_vnd bigint null`, `allocation jsonb not null default '[]'`); CHECK `amount_vnd >= 0`. Tạo migration bằng `dotnet ef migrations add AddSurveyAmountsAndMethodAllocation --project FinMate.Infrastructure --startup-project FinMate.API`.
- [ ] **Step 4:** Chạy lại test → PASS; `dotnet build` sạch.
- [ ] **Step 5:** Commit `feat(survey): add amount answers, plan roles and method allocation columns`.

### Task 2: Khảo sát nhận câu trả lời số tiền

**Files:**
- Modify: `backend/FinMate.Application/FinanceMethods/FinanceDtos.cs`, `SurveyService.cs`, `backend/FinMate.API/Controllers/SurveyController.cs`
- Modify: `backend/FinMate.Application/Common/Exceptions/FinanceErrorCodes.cs` (nếu cần mã mới)
- Test: `backend/FinMate.Tests/Integration/Controllers/FinanceMethodsControllerTests.cs`

**Interfaces — Consumes:** Task 1. **Produces:**
- `SurveyAnswerDto(string QuestionCode, string? OptionCode, long? AmountVnd = null)`
- `SurveyQuestionDto(..., string AnswerType, string? CategorySlug, string? PlanRole)`; `SurveyOptionDto(string Code, string Text, int? ShareMinPct, int? ShareMaxPct)`
- `GET /api/v1/survey` trả thêm các trường trên; `PUT /survey/answers` nhận `amountVnd`.

Luật: câu `choice` cần `optionCode` hợp lệ và không có `amountVnd`; câu `amount` cần `0 ≤ amountVnd ≤ 100_000_000_000` và không có `optionCode`; sai → `SurveyAnswerInvalid`. Câu `income` được gửi → ghi `users.monthly_income_cents = amountVnd` cùng lần lưu (và xóa cache hồ sơ như `UpdateUserProfileCommand` đang làm). `DELETE /survey` xóa cả dòng amount (đã là xóa toàn bộ answers — chỉ cần test chứng minh).

- [ ] **Step 1: Tests** (seed câu hỏi bằng dữ liệu Task 3 chưa có → trong test tự thêm 1 câu `amount`/`income` và 1 câu `choice`/`flexible` qua `FinMateDbContext`, giống cách file đang seed):
  - `SurveyAmountAnswer_IsSavedReturnedAndSetsProfileIncome`: gửi `{questionCode:"income_amount", amountVnd:10000000}` → `GET /survey` trả lại `amountVnd 10000000`; `GET /users/me` có `monthlyIncomeCents 10000000`.
  - `SurveyAmountAnswer_RejectsNegativeMissingOrMixedValues`: `amountVnd:-1`, thiếu `amountVnd`, câu amount mà gửi `optionCode:"A"`, câu choice mà gửi `amountVnd` → mỗi ca 4xx mã `SURVEY_ANSWER_INVALID`, `GET /survey` vẫn rỗng.
  - `DeletingTheSurvey_RemovesAmountAnswers`.
- [ ] **Step 2:** Chạy `dotnet test --filter FullyQualifiedName~FinanceMethodsControllerTests` → 3 test mới FAIL.
- [ ] **Step 3:** Sửa DTO, map, và vòng kiểm trong `SurveyService.SubmitAsync` theo luật trên. `kept` đổi thành `Dictionary<string, SurveyAnswerDto>`; `ShowIfEvaluator` vẫn chỉ nhận mã đáp án của câu choice. Signals không đổi (câu amount không có đáp án nên không sinh signal).
- [ ] **Step 4:** Chạy lại cả file test → PASS (kể cả các test cũ).
- [ ] **Step 5:** Commit `feat(survey): accept amount answers and write income to profile`.

### Task 3: Seed câu hỏi số liệu và bảng phân bổ 13 phương pháp

**Files:**
- Modify: `backend/FinMate.Infrastructure/Persistence/Seed/SurveySeeder.cs`, `FinanceMethodSeeder.cs`
- Test: `backend/FinMate.Tests/Integration/FinanceSeedTests.cs`

**Interfaces — Produces:** các mã câu `income_amount`, `fixed_housing`, `fixed_bills`, `fixed_debt`, `flex_food`, `flex_transport`, `flex_shopping`, `flex_entertainment`, `flex_health`, `flex_education`, `flex_family`, `savings_balance` (nhóm `money`, đứng sau câu hiện có); `allocation` của mọi phương pháp.

Đáp án mỗi câu flexible (mã / chữ / min / max): `A` "Không chi" 0/0 · `B` "Dưới 5%" 0/5 · `C` "5–10%" 5/10 · `D` "10–20%" 10/20 · `E` "20–30%" 20/30 · `F` "Từ 30%" 30/null. Signals `{}`.

`QuestionSeed` thêm `AnswerType`, `CategorySlug`, `PlanRole`; `OptionSeed` thêm `ShareMinPct`, `ShareMaxPct`. Seeder vẫn chỉ thêm câu/đáp án chưa có.

Allocation (key = tham số percent có sẵn trong `param_defs`):

| Phương pháp | Nhóm |
|---|---|
| category_budget | spend `needs_pct` [housing, bills, food, transport, health, family] · spend `wants_pct` [shopping, entertainment, education] · save `goals_pct` |
| six_jars | spend `necessities_pct` [food, housing, bills, transport, health, family] · spend `education_pct` [education] · spend `play_pct` [entertainment, shopping] · save `financial_freedom_pct` · save `long_term_pct` · save `give_pct` |
| rule_70_20_10 | spend `living_pct` [mọi danh mục chi] · save `saving_pct` · save `debt_goal_pct` |
| rule_80_20, goal_fund | spend `null` [mọi danh mục chi] · save `saving_pct` |
| emergency_buffer | spend `null` [mọi] · save `contribution_pct` |
| còn lại (minimal_tracking, available_to_spend, cash_flow_budget, envelope_budget, debt_repayment, low_effort_automation, zero_based_budget) | spend `null` [mọi] |

"Mọi danh mục chi" = housing, bills, food, transport, health, family, shopping, entertainment, education, other. Seeder đặt `Allocation` khi **thêm** phương pháp mới, và thêm một bước: phương pháp đã có mà `allocation = '[]'` thì điền (chỉ khi rỗng — không ghi đè bản admin đã sửa).

- [ ] **Step 1: Tests:** `MoneyQuestions_AreSeededAfterTheOldOnes_WithRolesAndShares` (12 câu, `income_amount` là amount/income, `flex_food` có 6 đáp án với `F` max null, mọi `category_slug` là danh mục hệ thống có thật); `EveryMethod_HasAValidAllocation` (gọi validator của Task 4 cho từng phương pháp — nếu làm Task 3 trước Task 4 thì kiểm thủ công: key là tham số percent của chính nó, không danh mục nào thuộc hai nhóm); `SeederFillsOnlyEmptyAllocations` (đặt allocation tùy ý cho một phương pháp, chạy seeder lần hai → giữ nguyên).
- [ ] **Step 2:** `dotnet test --filter FullyQualifiedName~FinanceSeedTests` → FAIL.
- [ ] **Step 3:** Cài seed như bảng trên.
- [ ] **Step 4:** Chạy lại → PASS; chạy `MethodSelectionOnSeedDataTests` để chắc câu mới (không signal) không đổi kết quả chọn phương pháp.
- [ ] **Step 5:** Commit `feat(survey): seed money questions and method allocations`.

### Task 4: Admin sửa câu hỏi số liệu và allocation

**Files:**
- Modify: `backend/FinMate.Application/Admin/FinanceCatalog/AdminFinanceCatalogModels.cs`, `AdminFinanceCatalogService.cs`, `FinanceCatalogValidation.cs`
- Modify: `backend/FinMate.API/Controllers/Admin/AdminSurveyQuestionsController.cs` và controller admin phương pháp (request records)
- Test: `backend/FinMate.Tests/Unit/Application/Admin/` (file validation hiện có cho finance catalog) + một test tích hợp trong `FinanceMethodsControllerTests`

**Interfaces — Produces:**
- `FinanceCatalogValidation.ValidateAllocation(JsonElement allocation, JsonElement paramDefs, IReadOnlySet<string> activeSystemSlugs)` — ném lỗi kèm tên trường như các validator hiện có.
- `AdminSurveyQuestionDto` thêm `AnswerType, CategorySlug, PlanRole`; `AdminSurveyOptionDto` thêm `ShareMinPct, ShareMaxPct`; `AdminFinanceMethodDto` thêm `JsonElement Allocation`. Request tạo/sửa nhận các trường tương ứng (tùy chọn khi sửa).

Luật allocation: mảng ≤ 8 phần tử; mỗi phần tử `{key: string|null, label: 1–40 ký tự, kind: spend|save, categories: [slug]}`; `key` khác null phải là tham số `percent` của phương pháp; `kind=save` thì `key` bắt buộc và `categories` rỗng; slug phải là danh mục hệ thống đang bật; một slug thuộc tối đa một nhóm; tối đa một nhóm `spend` có `key=null`.
Luật câu hỏi: `amount` thì không có đáp án đang bật; `plan_role` ∈ `SurveyPlanRoles.All` hoặc null; `flexible` cần `category_slug` hợp lệ và mọi đáp án đang bật có `share_min_pct` (0–100) và `share_max_pct` null hoặc ≥ min; `income` và `savings_balance` phải là `amount`.

- [ ] **Step 1: Tests** dạng `[Theory]` như `InvalidJarLayouts_AreRefusedWithTheFieldThatIsWrong`: mỗi luật trên một ca sai → mã lỗi + tên trường; một ca hợp lệ (allocation của category_budget) → không lỗi. Tích hợp: `Admin_UpdatesMethodAllocation_AndReadsItBack`.
- [ ] **Step 2:** Chạy → FAIL.
- [ ] **Step 3:** Cài validator và nối vào `CreateMethodAsync/UpdateMethodAsync/CreateQuestionAsync/UpdateQuestionAsync/AddOptionAsync/UpdateOptionAsync`; ghi audit như các trường khác.
- [ ] **Step 4:** Chạy → PASS.
- [ ] **Step 5:** Commit `feat(admin): edit survey money questions and method allocation`.

### Task 5: Hàm tính kế hoạch

**Files:**
- Create: `backend/FinMate.Application/FinanceMethods/FinancialPlanCalculator.cs`
- Test: `backend/FinMate.Tests/Unit/Application/FinanceMethods/FinancialPlanCalculatorTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record PlanGroup(string? Key, string Label, string Kind, IReadOnlyList<string> Categories, int? Pct);
public sealed record PlanInput(
    long IncomeVnd,
    IReadOnlyDictionary<string, long> FixedByCategory,   // slug → đồng (fixed_housing, fixed_bills)
    long FixedOtherVnd,                                   // fixed_debt, không thuộc danh mục
    IReadOnlyDictionary<string, long> DeclaredFlexible,   // slug → đồng, đã làm tròn
    IReadOnlyList<PlanGroup> Groups);                     // rỗng = một nhóm spend không trần
public sealed record PlanCategoryLine(string Slug, string? GroupKey, long FixedVnd, long DeclaredVnd, long LimitVnd, long ReducedByVnd);
public sealed record FinancialPlan(long IncomeVnd, long FixedVnd, long PlannedSpendVnd, long SurplusVnd, long SaveTargetVnd,
    IReadOnlyList<PlanCategoryLine> Categories, IReadOnlyList<string> Warnings);
public static class FinancialPlanCalculator { public static FinancialPlan Calculate(PlanInput input); }
```
`DeclaredVnd` = cố định + linh hoạt khai báo của danh mục; `LimitVnd` = cố định + linh hoạt sau khi co; `FixedVnd` (tổng) = tổng `FixedByCategory` + `FixedOtherVnd`. Cảnh báo: `"fixed_exceeds_group:<key>"`, `"below_save_target"`, `"deficit"`.

Thuật toán (không suy ra được từ chữ ký):
```
cap(g)      = floor(I * g.Pct / 100 / 1000) * 1000          // chỉ nhóm spend có Key
flex(g)     = Σ DeclaredFlexible[c] cho c ∈ g
fixed(g)    = Σ FixedByCategory[c]  cho c ∈ g
if fixed(g) + flex(g) > cap(g):
    room = max(0, cap(g) - fixed(g)); if fixed(g) > cap(g): warn fixed_exceeds_group:key
    mỗi c: flexLimit = floor(Declared[c] * room / flex(g) / 1000) * 1000   // Int128 cho phép nhân
danh mục không thuộc nhóm nào và nhóm Key=null: giữ nguyên
PlannedSpend = Σ LimitVnd + FixedOtherVnd ; Surplus = I - PlannedSpend
SaveTarget   = Σ floor(I * pct / 100 / 1000) * 1000 cho nhóm save
Surplus < 0  → warn deficit ; 0 ≤ Surplus < SaveTarget → warn below_save_target
Categories sắp theo LimitVnd giảm dần, bằng nhau theo slug.
```

- [ ] **Step 1: Tests** (I = 10.000.000; nhóm 50/30/20 như Task 3):
  - `UnderTheCap_KeepsWhatTheUserDeclared`: housing cố định 2.500.000, bills 500.000, food 1.500.000, transport 500.000, shopping 1.000.000 → mọi `LimitVnd == DeclaredVnd`, `PlannedSpendVnd 6_000_000`, `SurplusVnd 4_000_000`, `SaveTargetVnd 2_000_000`, không cảnh báo.
  - `OverTheCap_ShrinksOnlyFlexibleSpendingInThatGroup`: như trên nhưng food 2.500.000 → food limit `1_666_000` (giảm `834_000`), transport `333_000` (giảm `167_000`), housing vẫn `2_500_000`, `SurplusVnd 4_001_000`.
  - `FixedCostsAboveTheCap_ZeroFlexibleAndWarn`: housing 6.000.000 → food/transport limit 0, cảnh báo chứa `fixed_exceeds_group:needs_pct`.
  - `SpendingAboveIncome_StillReturnsAPlanFlaggedDeficit`: I = 5.000.000, không nhóm, housing 3.000.000, `FixedOtherVnd` 3.000.000 → `SurplusVnd -1_000_000`, cảnh báo `deficit`. (Review Focus 2, 5)
  - `SurplusBelowTheSaveTarget_Warns`: nhóm spend null + save 20%, khai 9.000.000 → `SurplusVnd 1_000_000`, `below_save_target`.
  - `TotalsAlwaysAddUpToIncome`: với mọi ca trên, `PlannedSpendVnd + SurplusVnd == IncomeVnd`.
- [ ] **Step 2:** `dotnet test --filter FullyQualifiedName~FinancialPlanCalculatorTests` → FAIL.
- [ ] **Step 3:** Cài `Calculate` theo thuật toán.
- [ ] **Step 4:** Chạy → PASS.
- [ ] **Step 5:** Commit `feat(plan): add financial plan calculator`.

### Task 6: API bản nháp kế hoạch

**Files:**
- Create: `backend/FinMate.Application/FinanceMethods/FinancialPlanService.cs` (interface `IFinancialPlanService` cùng file hoặc trong `IFinanceServices.cs` theo lệ hiện có)
- Modify: `backend/FinMate.API/Controllers/MyFinanceController.cs`, DI trong `Program.cs`/extension đăng ký dịch vụ finance, `FinanceErrorCodes.cs`, `FinanceDtos.cs`
- Test: `backend/FinMate.Tests/Integration/Controllers/FinanceMethodsControllerTests.cs`

**Interfaces — Consumes:** Task 2 (câu trả lời), Task 3 (vai trò, allocation), Task 5 (`FinancialPlanCalculator.Calculate`), `IUserMethodService.GetMineAsync` (phương pháp chính + `Effective`). **Produces:**
- `GET /api/v1/me/financial-plan/draft` → `FinancialPlanDto(string MethodCode, int MethodVersion, long IncomeVnd, long FixedVnd, long FixedOtherVnd, long PlannedSpendVnd, long SurplusVnd, long SaveTargetVnd, long? SavingsBalanceVnd, IReadOnlyList<FinancialPlanLineDto> Categories, IReadOnlyList<FinancialPlanGroupDto> Groups, IReadOnlyList<string> Warnings)`
- `FinancialPlanLineDto(Guid CategoryId, string Slug, string Name, string? GroupKey, long FixedVnd, long DeclaredVnd, long LimitVnd, long ReducedByVnd)`; `FinancialPlanGroupDto(string? Key, string Label, string Kind, int? Pct, long? CapVnd)`
- Mã lỗi mới: `FINANCE_PLAN_NO_ACTIVE_METHOD`, `FINANCE_PLAN_NO_INCOME` (ném `BusinessRuleException`, trạng thái HTTP theo middleware hiện có).

Ráp dữ liệu: thu nhập = `users.monthly_income_cents` (Task 2 ghi từ khảo sát), null/0 → `NO_INCOME`. Không có phương pháp `Primary` đang dùng → `NO_ACTIVE_METHOD`. Câu `fixed` có `category_slug` → `FixedByCategory`, không có → cộng vào `FixedOtherVnd`. Câu `flexible`: điểm giữa `(min+max)/2`, max null → `min`; `DeclaredFlexible[slug] = floor(I * pct / 100 / 1000) * 1000`. Câu trả lời của câu hỏi đã tắt vẫn tính; danh mục hệ thống đã tắt/không tồn tại thì bỏ. `Pct` của nhóm lấy từ `Effective[key]`. Cập nhật spec mục 7: lỗi là `BusinessRuleException` với hai mã trên thay cho "409".

- [ ] **Step 1: Tests:**
  - `Draft_FollowsTheChosenMethod`: người dùng gửi khảo sát (thu 10tr, nhà 2,5tr, food `D` 10–20% → 1.500.000), chọn `category_budget` → draft có `surplusVnd`, dòng food `limitVnd 1500000`, `categoryId` khớp danh mục `food`; đổi sang `rule_80_20` → `groups` khác, `saveTargetVnd 2000000`.
  - `Draft_WithoutIncome_ReturnsNoIncome` (Review Focus 1) và `Draft_WithoutAMethod_ReturnsNoActiveMethod`.
  - `Draft_SkipsDisabledCategories_ButKeepsAnswersOfDisabledQuestions` (Review Focus 3).
  - `Draft_WithAnEmptyAllocation_KeepsDeclaredSpending` (đặt allocation `[]` qua DB) (Review Focus 5).
  - `Draft_RequiresLogin_AndOnlyReadsTheCaller'sAnswers`.
  - `AppliedDraft_BecomesCategoryBudgets`: gửi các dòng `limitVnd > 0` vào `POST /forecast/categories/apply` → `GET /budgets` có hạn mức tương ứng tháng này.
- [ ] **Step 2:** Chạy → FAIL.
- [ ] **Step 3:** Cài service + endpoint + DI + sửa spec mục 7.
- [ ] **Step 4:** `dotnet test` toàn bộ → PASS (ngoài test đỏ có sẵn `rule_80_20` vs `cash_flow_budget` đã ghi trong CLAUDE.md).
- [ ] **Step 5:** Commit `feat(plan): GET /me/financial-plan/draft`.

### Task 7: Mobile — khảo sát phần số liệu

**Files (repo `E:\Ki 9\Capstone_Project\FinMate_FE`, nhánh `dev/tuan`):**
- Modify: `lib/features/survey/domain/survey.dart`, `data/survey_repository.dart`, `presentation/controllers/survey_controller.dart`, `presentation/screens/survey_screen.dart`
- Create: `lib/features/survey/domain/money_range.dart`, `lib/features/survey/presentation/widgets/money_section.dart`
- Test: `test/features/survey/money_range_test.dart`, `test/features/survey/survey_screen_money_test.dart`

**Interfaces — Consumes:** API Task 2. **Produces:**
- Model: `SurveyQuestion.answerType`, `categorySlug`, `planRole`; `SurveyOption.shareMinPct/shareMaxPct`; `SurveyAnswer.amountVnd`.
- `String moneyRangeLabel({required int? incomeVnd, required int minPct, int? maxPct})` — có thu nhập: `"1–2tr"` (định dạng triệu/nghìn kiểu VN, làm tròn xuống nghìn); `maxPct == null`: `"Từ 3tr"`; không thu nhập: `"10–20%"`.

- [ ] **Step 1: Tests:** `moneyRangeLabel(incomeVnd: 10000000, minPct: 10, maxPct: 20) == "1–2tr"`; `(…, minPct: 30, maxPct: null) == "Từ 3tr"`; `(incomeVnd: null, minPct: 5, maxPct: 10) == "5–10%"`; `(incomeVnd: 7500000, minPct: 0, maxPct: 5) == "0–375k"`. Widget: mọi câu nhóm `money` vẽ trên **một** màn; nhập thu nhập thì nhãn chip flexible đổi sang số tiền; ô số từ chối chữ và số âm; gửi đi có `amountVnd`.
- [ ] **Step 2:** `flutter test test/features/survey` → FAIL.
- [ ] **Step 3:** Cài (đọc skill UI theo CLAUDE.md của repo trước khi vẽ).
- [ ] **Step 4:** `flutter test test/features/survey` và `flutter analyze` → sạch.
- [ ] **Step 5:** Commit trong repo FE `feat(survey): money section with amount input and VND range chips`.

### Task 8: Mobile — màn "Kế hoạch tháng của bạn"

**Files:**
- Modify: `lib/features/finance_methods/data/finance_methods_repository.dart`, `domain/finance_method.dart`, `presentation/screens/b4_recommendations_screen.dart` (sau khi chọn → mở màn kế hoạch), router của app
- Create: `lib/features/finance_methods/presentation/screens/b8_financial_plan_screen.dart`, `presentation/controllers/financial_plan_controller.dart`
- Test: `test/features/finance_methods/b8_financial_plan_screen_test.dart`, thêm ca vào `finance_methods_repository_test.dart`

**Interfaces — Consumes:** `GET /me/financial-plan/draft` (Task 6), `POST /forecast/categories/apply` (`items: [{categoryId, limitCents}]`). **Produces:** route `/financial-plan`; `FinancialPlanController.editLimit(String slug, int limitVnd)` tính lại `plannedSpend`/`surplus` trên máy (`surplus = income − Σ limit − fixedOther`).

- [ ] **Step 1: Tests:** màn hiện dòng tổng thu → cố định → chi → **dư**; dòng bị co hiện "giảm X"; sửa hạn mức food từ 1.500.000 xuống 1.000.000 thì dư tăng đúng 500.000; `deficit` hiện thông điệp ổn định dòng tiền, không hiện mục tiêu tiết kiệm; lỗi `FINANCE_PLAN_NO_INCOME` hiện nút dẫn tới nhập thu nhập; "Áp dụng" gọi apply với đúng các dòng `limit > 0`; "Để sau" không gọi gì. Chữ cảnh báo lấy theo mã từ một bảng chữ (không rải chuỗi trong widget).
- [ ] **Step 2:** `flutter test test/features/finance_methods` → FAIL.
- [ ] **Step 3:** Cài màn, controller, repository, route; mở màn sau khi chọn phương pháp ở B4 và từ trang chủ phương pháp.
- [ ] **Step 4:** `flutter test` + `flutter analyze` → sạch.
- [ ] **Step 5:** Commit FE `feat(finance-methods): onboarding financial plan screen`.

### Task 9: Admin web

**Files (`E:\Ki 9\Capstone_Project\FinMate_Admin`, không phải git):**
- Modify: `src/shared/models.ts`, `src/app/SurveyPage.tsx`, `src/app/FinanceMethodsPage.tsx`
- Create: `src/app/AllocationEditor.tsx`

**Interfaces — Consumes:** DTO admin của Task 4.

- [ ] **Step 1:** Trang khảo sát: chọn loại câu (`choice`/`amount`), vai trò, danh mục (danh sách danh mục hệ thống từ API, không gõ tay); đáp án có ô `share min/max %`. Câu `amount` ẩn phần đáp án.
- [ ] **Step 2:** Trang phương pháp: `AllocationEditor` thêm/xóa nhóm, chọn `kind`, chọn `key` trong các tham số percent của phương pháp (hoặc "phần còn lại"), chọn danh mục; hiện lỗi trường do server trả về.
- [ ] **Step 3:** `npm run build` (và `npm run lint` nếu có) → sạch; mở trang bằng dev server, sửa allocation của `category_budget`, lưu, tải lại thấy giữ nguyên.
- [ ] **Step 4:** Không commit (thư mục không phải git) — ghi lại file đã đổi trong báo cáo cuối.
