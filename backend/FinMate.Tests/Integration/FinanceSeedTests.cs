using System.Text.Json;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Dữ liệu gieo cho khảo sát, các cách quản lý và mẫu mục tiêu, chạy trên Postgres thật (cùng container với
/// <see cref="ReceiptScanDbFixture"/>, mỗi test một database riêng). Kiểm cả số lượng lẫn các điều dễ hỏng im lặng: sai chính tả một signal,
/// seeder ghi đè chỉnh sửa của admin, và người đang thiếu tiền mà không còn phương pháp nào để gợi ý.
/// </summary>
public class FinanceSeedTests : IClassFixture<ReceiptScanDbFixture>
{
    private const int ChoiceQuestionCount = 11;      // các câu chọn phương pháp, mỗi câu 5 đáp án
    private const int OptionsPerQuestion = 5;
    private const int FlexibleQuestionCount = 7;     // chi linh hoạt theo danh mục, mỗi câu 6 khoảng %
    private const int SharesPerFlexibleQuestion = 6;
    private const int AmountQuestionCount = 5;       // thu nhập, 3 khoản cố định, tiết kiệm đang có — không có đáp án
    private const int QuestionCount = ChoiceQuestionCount + FlexibleQuestionCount + AmountQuestionCount;
    private const int OptionCount = ChoiceQuestionCount * OptionsPerQuestion + FlexibleQuestionCount * SharesPerFlexibleQuestion;
    private static readonly string[] SpendCategories = ["housing", "bills", "food", "transport", "health", "family", "shopping", "entertainment", "education", "other"];
    private const int MethodCount = 13;
    private const int TemplateCount = 4;
    private const int LinkCount = 23;

    private readonly ReceiptScanDbFixture _fixture;

    public FinanceSeedTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<FinMateDbContext> NewSeededDbAsync()
    {
        var db = _fixture.NewContext(_fixture.ConnectionStringFor($"seed_{Guid.NewGuid():N}"));
        await db.Database.MigrateAsync();
        await SeedAllAsync(db);
        return db;
    }

    private static async Task SeedAllAsync(FinMateDbContext db)
    {
        await SurveySeeder.SeedAsync(db);
        await FinanceMethodSeeder.SeedAsync(db);
        await GoalTemplateSeeder.SeedAsync(db);   // phải SAU FinanceMethodSeeder
    }

    private static List<string> JsonKeys(string json, string? property = null)
    {
        using var doc = JsonDocument.Parse(json);
        var element = property is null ? doc.RootElement : doc.RootElement.GetProperty(property);
        return element.EnumerateObject().Select(p => p.Name).ToList();
    }

    private static List<string> JsonStrings(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    // ---- số lượng và idempotency ----

    [Fact]
    public async Task Seeds_TenQuestionsWithFiveOptions_ThirteenMethods_FourTemplates_AndTheirLinks()
    {
        await using var db = await NewSeededDbAsync();

        (await db.SurveyQuestions.CountAsync()).Should().Be(QuestionCount);
        (await db.SurveyOptions.CountAsync()).Should().Be(OptionCount);
        (await db.FinanceMethods.CountAsync()).Should().Be(MethodCount);
        (await db.GoalTemplates.CountAsync()).Should().Be(TemplateCount);
        (await db.MethodGoalTemplates.CountAsync()).Should().Be(LinkCount);

        var optionsPerQuestion = await db.SurveyOptions.GroupBy(o => o.QuestionId).Select(g => g.Count()).ToListAsync();
        optionsPerQuestion.Should().OnlyContain(n => n == OptionsPerQuestion || n == SharesPerFlexibleQuestion);

        var methodCodes = typeof(FinanceMethodCodes).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToList();
        (await db.FinanceMethods.Select(m => m.Code).ToListAsync()).Should().BeEquivalentTo(methodCodes);
    }

    [Fact]
    public async Task SeedingAgain_AddsNothing()
    {
        await using var db = await NewSeededDbAsync();

        await SeedAllAsync(db);

        (await db.SurveyQuestions.CountAsync()).Should().Be(QuestionCount);
        (await db.SurveyOptions.CountAsync()).Should().Be(OptionCount);
        (await db.FinanceMethods.CountAsync()).Should().Be(MethodCount);
        (await db.GoalTemplates.CountAsync()).Should().Be(TemplateCount);
        (await db.MethodGoalTemplates.CountAsync()).Should().Be(LinkCount);
    }

    [Fact]
    public async Task SeedingAgain_NeverOverwritesOrRevivesWhatAnAdminChanged()
    {
        await using var db = await NewSeededDbAsync();

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE finance_methods SET name = 'Tên do admin sửa', is_active = false, version = 7 WHERE code = 'minimal_tracking'");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE survey_options SET text = 'Đáp án do admin sửa' WHERE code = 'A' AND question_id = (SELECT id FROM survey_questions WHERE code = 'income_pattern')");
        await db.Database.ExecuteSqlRawAsync("UPDATE goal_templates SET is_active = false WHERE code = 'specific_target_goal'");
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM method_goal_templates WHERE method_id = (SELECT id FROM finance_methods WHERE code = 'envelope_budget') "
            + "AND template_id = (SELECT id FROM goal_templates WHERE code = 'irregular_expenses_fund')");

        await SeedAllAsync(db);

        var method = await db.FinanceMethods.AsNoTracking().SingleAsync(m => m.Code == FinanceMethodCodes.MinimalTracking);
        method.Name.Should().Be("Tên do admin sửa");
        method.IsActive.Should().BeFalse();
        method.Version.Should().Be(7);

        var incomeQuestionId = await db.SurveyQuestions.Where(q => q.Code == "income_pattern").Select(q => q.Id).SingleAsync();
        (await db.SurveyOptions.AsNoTracking().SingleAsync(o => o.Code == "A" && o.QuestionId == incomeQuestionId))
            .Text.Should().Be("Đáp án do admin sửa");
        (await db.GoalTemplates.AsNoTracking().SingleAsync(t => t.Code == GoalTemplateCodes.SpecificTargetGoal)).IsActive.Should().BeFalse();

        // Liên kết admin đã gỡ không được thêm lại.
        (await db.MethodGoalTemplates.CountAsync()).Should().Be(LinkCount - 1);
    }

    [Fact]
    public async Task MoneyQuestions_AreSeededAfterTheOldOnes_WithRolesAndShares()
    {
        await using var db = await NewSeededDbAsync();
        var questions = await db.SurveyQuestions.AsNoTracking().OrderBy(q => q.SortOrder).ToListAsync();
        var options = await db.SurveyOptions.AsNoTracking().ToListAsync();
        await CategorySeeder.SeedAsync(db);
        var systemSlugs = await db.Categories.Where(c => c.UserId == null).Select(c => c.Slug).ToListAsync();

        var money = questions.Where(q => q.GroupCode == "money").ToList();
        money.Should().HaveCount(FlexibleQuestionCount + AmountQuestionCount);
        money.Min(q => q.SortOrder).Should().BeGreaterThan(questions.Where(q => q.GroupCode != "money").Max(q => q.SortOrder));

        var income = money.Single(q => q.Code == "income_amount");
        income.AnswerType.Should().Be(SurveyAnswerTypes.Amount);
        income.PlanRole.Should().Be(SurveyPlanRoles.Income);
        money.Where(q => q.AnswerType == SurveyAnswerTypes.Amount).Should().OnlyContain(q => !options.Any(o => o.QuestionId == q.Id));
        money.Where(q => q.CategorySlug != null).Select(q => q.CategorySlug!).Should().OnlyContain(slug => systemSlugs.Contains(slug));

        var food = money.Single(q => q.Code == "flex_food");
        food.PlanRole.Should().Be(SurveyPlanRoles.Flexible);
        food.CategorySlug.Should().Be("food");
        var shares = options.Where(o => o.QuestionId == food.Id).OrderBy(o => o.SortOrder)
            .Select(o => (o.Code, o.ShareMinPct, o.ShareMaxPct)).ToList();
        shares.Should().Equal(("A", 0, 0), ("B", 0, 5), ("C", 5, 10), ("D", 10, 20), ("E", 20, 30), ("F", 30, (int?)null));
    }

    [Fact]
    public async Task EveryMethod_HasAnAllocationThatReferencesItsOwnPercentParams()
    {
        await using var db = await NewSeededDbAsync();

        foreach (var method in await db.FinanceMethods.AsNoTracking().ToListAsync())
        {
            using var defs = JsonDocument.Parse(method.ParamDefs);
            var percentKeys = defs.RootElement.EnumerateArray()
                .Where(p => p.GetProperty("type").GetString() == FinanceParamTypes.Percent)
                .Select(p => p.GetProperty("key").GetString()).ToHashSet();

            using var allocation = JsonDocument.Parse(method.Allocation);
            var groups = allocation.RootElement.EnumerateArray().ToList();
            groups.Should().NotBeEmpty(method.Code);

            var seen = new HashSet<string>();
            foreach (var group in groups)
            {
                var key = group.GetProperty("key").ValueKind == JsonValueKind.Null ? null : group.GetProperty("key").GetString();
                var kind = group.GetProperty("kind").GetString();
                var categories = group.GetProperty("categories").EnumerateArray().Select(c => c.GetString()!).ToList();
                if (key is not null)
                {
                    percentKeys.Should().Contain(key, method.Code);
                }

                if (kind == AllocationKinds.Save)
                {
                    key.Should().NotBeNull(method.Code);
                    categories.Should().BeEmpty(method.Code);
                }

                foreach (var category in categories)
                {
                    seen.Add(category).Should().BeTrue($"{method.Code}: {category} chỉ được thuộc một nhóm");
                }
            }

            groups.Count(g => g.GetProperty("kind").GetString() == AllocationKinds.Spend && g.GetProperty("key").ValueKind == JsonValueKind.Null)
                .Should().BeLessThanOrEqualTo(1, method.Code);
        }

        var fiftyThirtyTwenty = await db.FinanceMethods.AsNoTracking().SingleAsync(m => m.Code == FinanceMethodCodes.CategoryBudget);
        fiftyThirtyTwenty.Allocation.Should().Contain("needs_pct").And.Contain("wants_pct").And.Contain("goals_pct");
        var rule8020 = await db.FinanceMethods.AsNoTracking().SingleAsync(m => m.Code == FinanceMethodCodes.Rule8020);
        using var doc8020 = JsonDocument.Parse(rule8020.Allocation);
        doc8020.RootElement.EnumerateArray().First(g => g.GetProperty("kind").GetString() == AllocationKinds.Spend)
            .GetProperty("categories").EnumerateArray().Select(c => c.GetString()).Should().BeEquivalentTo(SpendCategories);
    }

    [Fact]
    public async Task Seeder_FillsOnlyEmptyAllocations()
    {
        await using var db = await NewSeededDbAsync();
        const string adminAllocation = """[{"key":null,"label":"Admin","kind":"spend","categories":["food"]}]""";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE finance_methods SET allocation = CAST({adminAllocation} AS jsonb) WHERE code = {FinanceMethodCodes.CategoryBudget}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE finance_methods SET allocation = '[]'::jsonb WHERE code = {FinanceMethodCodes.SixJars}");
        db.ChangeTracker.Clear();   // seeder chạy lúc khởi động trên một context mới, không thấy bản cũ trong bộ nhớ

        await SeedAllAsync(db);

        (await db.FinanceMethods.AsNoTracking().SingleAsync(m => m.Code == FinanceMethodCodes.CategoryBudget)).Allocation
            .Should().Contain("Admin");
        (await db.FinanceMethods.AsNoTracking().SingleAsync(m => m.Code == FinanceMethodCodes.SixJars)).Allocation
            .Should().Contain("necessities_pct");
    }

    // ---- tính toàn vẹn của dữ liệu gieo ----

    [Fact]
    public async Task EverySignalInTheSeedData_IsADeclaredSignal_WithAWeightOfOneToThree()
    {
        await using var db = await NewSeededDbAsync();
        var used = new HashSet<string>();

        // Câu chỉ lấy số liệu cho dự báo (không dùng để chọn cách quản lý) thì không có signal: xem SurveySeeder.
        // Câu số liệu của kế hoạch tháng (plan_role khác null) cũng vậy.
        var dataOnly = await db.SurveyQuestions.AsNoTracking().Where(q => q.Code == "avg_monthly_spend" || q.PlanRole != null).Select(q => q.Id).ToListAsync();
        foreach (var option in (await db.SurveyOptions.AsNoTracking().ToListAsync()).Where(o => !dataOnly.Contains(o.QuestionId)))
        {
            var signals = JsonKeys(option.Signals);
            signals.Should().NotBeEmpty($"đáp án {option.Code} phải sinh ra ít nhất một signal");
            used.UnionWith(signals);

            using var doc = JsonDocument.Parse(option.Signals);
            doc.RootElement.EnumerateObject().Select(p => p.Value.GetInt32()).Should().OnlyContain(w => w >= 1 && w <= 3);
        }

        foreach (var method in await db.FinanceMethods.AsNoTracking().ToListAsync())
        {
            used.UnionWith(JsonKeys(method.Eligibility, "weights"));
            used.UnionWith(JsonStrings(method.Eligibility, "excludes"));
        }

        foreach (var link in await db.MethodGoalTemplates.AsNoTracking().ToListAsync())
        {
            used.UnionWith(JsonStrings(link.Conditions!, "excludes"));
        }

        used.Should().BeSubsetOf(FinanceSignals.All, "một signal sai chính tả làm phương pháp không bao giờ khớp mà không có gì báo lỗi");
    }

    [Fact]
    public async Task Methods_AreWellFormed()
    {
        await using var db = await NewSeededDbAsync();

        foreach (var method in await db.FinanceMethods.AsNoTracking().ToListAsync())
        {
            JsonStrings("{\"s\":" + method.StarterSteps + "}", "s").Should().HaveCount(3, $"{method.Code} có 3 bước bắt đầu");
            method.Summary.Should().NotBeNullOrWhiteSpace();
            method.WhyText.Should().NotBeNullOrWhiteSpace();

            using var doc = JsonDocument.Parse(method.ParamDefs);
            var defs = doc.RootElement.EnumerateArray().ToList();
            defs.Select(d => d.GetProperty("key").GetString()).Should().OnlyHaveUniqueItems();

            foreach (var def in defs)
            {
                var min = def.GetProperty("min").GetInt64();
                var value = def.GetProperty("default").GetInt64();
                var max = def.GetProperty("max").GetInt64();
                (min <= value && value <= max).Should().BeTrue($"{method.Code}.{def.GetProperty("key").GetString()}: mặc định phải nằm trong [min, max]");
                def.GetProperty("userEditable").GetBoolean().Should().BeTrue();
            }

            var t1 = defs.SingleOrDefault(d => d.GetProperty("key").GetString() == FinanceParamKeys.T1Pct);
            var t2 = defs.SingleOrDefault(d => d.GetProperty("key").GetString() == FinanceParamKeys.T2Pct);
            if (t1.ValueKind != JsonValueKind.Undefined)
            {
                t2.ValueKind.Should().NotBe(JsonValueKind.Undefined, "T1 luôn đi cùng T2");
                t1.GetProperty("default").GetInt64().Should().Be(70);
                t2.GetProperty("default").GetInt64().Should().Be(90);
            }
        }
    }

    [Fact]
    public async Task SomeoneInCashShortfallOrDebt_IsStillOfferedAPrimaryMethod_AndNeverASavingsOne()
    {
        await using var db = await NewSeededDbAsync();
        var methods = await db.FinanceMethods.AsNoTracking().ToListAsync();

        bool IsExcluded(FinanceMethod m, HashSet<string> signals) => JsonStrings(m.Eligibility, "excludes").Any(signals.Contains);

        var shortfall = new HashSet<string> { FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations, FinanceSignals.TightCashFlow };
        var eligible = methods.Where(m => !IsExcluded(m, shortfall)).ToList();

        eligible.Where(m => m.Role is FinanceMethodRole.Primary or FinanceMethodRole.Both)
            .Select(m => m.Code).Should().Contain(FinanceMethodCodes.CashFlowBudget);
        eligible.Select(m => m.Code).Should().NotContain(
            new[] { FinanceMethodCodes.GoalFund, FinanceMethodCodes.EmergencyBuffer, FinanceMethodCodes.CategoryBudget },
            "file .md: thiếu hụt dòng tiền thì xử lý dòng tiền trước, không đẩy tiết kiệm hay tỷ lệ cứng");

        var debt = new HashSet<string> { FinanceSignals.DebtPressure };
        methods.Where(m => !IsExcluded(m, debt)).Select(m => m.Code).Should().Contain(FinanceMethodCodes.DebtRepayment);
    }

    [Fact]
    public async Task SavingTemplates_AreExcludedForSomeoneInCashShortfall_AndTrackingOnlyMethodsGetNone()
    {
        await using var db = await NewSeededDbAsync();
        var links = await db.MethodGoalTemplates.AsNoTracking().ToListAsync();

        links.Should().OnlyContain(l => JsonStrings(l.Conditions!, "excludes").Contains(FinanceSignals.CashShortfall));

        // Phương pháp theo dõi tối giản và kế hoạch trả nợ cố ý không gợi ý mục tiêu tiết kiệm nào.
        var noGoals = await db.FinanceMethods.AsNoTracking()
            .Where(m => m.Code == FinanceMethodCodes.MinimalTracking || m.Code == FinanceMethodCodes.DebtRepayment)
            .Select(m => m.Id).ToListAsync();
        links.Select(l => l.MethodId).Should().NotIntersectWith(noGoals);
    }

    [Fact]
    public async Task SurveyConditions_ReferToEarlierQuestionsAndRealOptions()
    {
        await using var db = await NewSeededDbAsync();
        var questions = await db.SurveyQuestions.AsNoTracking().OrderBy(q => q.SortOrder).ToListAsync();
        var options = await db.SurveyOptions.AsNoTracking().ToListAsync();

        questions.Select(q => q.SortOrder).Should().Equal(Enumerable.Range(1, QuestionCount));
        questions.Where(q => q.ShowIf is not null).Select(q => q.Code).Should().Equal("saving_style");

        foreach (var question in questions.Where(q => q.ShowIf is not null))
        {
            using var doc = JsonDocument.Parse(question.ShowIf!);
            foreach (var condition in doc.RootElement.GetProperty("all").EnumerateArray())
            {
                var code = condition.GetProperty("question").GetString();
                var target = questions.Single(q => q.Code == code);
                target.SortOrder.Should().BeLessThan(question.SortOrder, "chỉ được điều kiện theo câu đứng TRƯỚC");

                var listed = (condition.TryGetProperty("in", out var inList) ? inList : condition.GetProperty("not_in"))
                    .EnumerateArray().Select(e => e.GetString()!).ToList();
                options.Where(o => o.QuestionId == target.Id).Select(o => o.Code).Should().Contain(listed);
            }
        }
    }
}
