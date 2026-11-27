using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinanceMethods;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>
/// Công cụ quản lý của agent: ba công cụ ĐỌC (ngân sách, mục tiêu, khoản định kỳ) và một công cụ ĐỀ XUẤT hạn mức. Số liệu đi thành bằng chứng có id, nên câu trả lời trích dẫn và kiểm chứng được;
/// công cụ đề xuất chỉ sinh thẻ, không đặt gì.
/// </summary>
public class ManagementToolsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly ToolContext Context = new(Guid.NewGuid(), Now);

    private static JsonElement Args(object args) => JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement.Clone();

    private static BudgetForecastDto Budget(string? name, long limit, long spent, int percent, string status)
        => new(Guid.NewGuid(), null, name, limit, spent, spent, percent, status, 0, 10, "habit");

    private static async Task<ToolResult> RunBudgets(params BudgetForecastDto[] rows)
    {
        var service = new Mock<IBudgetForecastService>();
        service.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        return await new BudgetStatusTool(service.Object).RunAsync(Context, Args(new { }));
    }

    [Fact]
    public async Task BudgetStatusKeepsTheFourMostStretchedBudgetsWithSpentAndLimitEachAsEvidence()
    {
        var result = await RunBudgets(
            Budget("Ăn uống", 3_000_000, 2_700_000, 120, "at_risk"), Budget("Di chuyển", 1_000_000, 200_000, 40, "ok"), Budget("Nhà ở", 5_000_000, 5_500_000, 110, "over"),
            Budget("Giải trí", 500_000, 100_000, 30, "ok"), Budget("Mua sắm", 800_000, 100_000, 20, "ok"));

        result.Status.Should().Be(ToolStatus.Ok);
        result.Evidence.Should().HaveCount(8);
        result.Evidence.Select(e => e.Id).Should().Equal(
            "budget_1_spent", "budget_1_limit", "budget_2_spent", "budget_2_limit", "budget_3_spent", "budget_3_limit", "budget_4_spent", "budget_4_limit");
        result.Evidence[0].ValueVnd.Should().Be(2_700_000);
        result.Evidence[0].Text.Should().Contain("Ăn uống").And.Contain("90%").And.Contain("có nguy cơ vượt");
        result.Evidence[1].ValueVnd.Should().Be(3_000_000);
        result.Evidence[2].Text.Should().Contain("Nhà ở").And.Contain("đã vượt hạn mức");
        result.Evidence.Should().NotContain(e => e.Text!.Contains("Mua sắm"), "ngân sách thứ năm bị cắt");
        result.Evidence.Select(e => e.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task NoBudgetsIsInsufficientData()
    {
        var result = await RunBudgets();

        result.Status.Should().Be(ToolStatus.InsufficientData);
        result.Code.Should().Be("no_budgets");
    }

    [Fact]
    public async Task GoalsProgressListsActiveGoalsNearestDeadlineFirstWithSavedAndTarget()
    {
        var goals = new Mock<ISavingGoalRepository>();
        goals.Setup(g => g.GetListForUserAsync(Context.UserId, SavingGoalStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SavingGoal { Name = "Mua xe", TargetCents = 600_000_000, SavedCents = 150_000_000, Deadline = null },
            new SavingGoal { Name = "Du lịch", TargetCents = 20_000_000, SavedCents = 5_000_000, Deadline = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.FromHours(7)) },
        ]);

        var result = await new GoalsProgressTool(goals.Object).RunAsync(Context, Args(new { }));

        result.Evidence.Select(e => e.Id).Should().Equal("goal_1_saved", "goal_1_target", "goal_2_saved", "goal_2_target");
        result.Evidence[0].Text.Should().Contain("Du lịch").And.Contain("25%").And.Contain("01/03/2027");
        result.Evidence[0].ValueVnd.Should().Be(5_000_000);
        result.Evidence[3].Text.Should().Contain("Mua xe");
        result.Evidence[2].Text.Should().Contain("không đặt hạn");
    }

    [Fact]
    public async Task NoGoalsIsInsufficientData()
    {
        var goals = new Mock<ISavingGoalRepository>();
        goals.Setup(g => g.GetListForUserAsync(It.IsAny<Guid>(), It.IsAny<SavingGoalStatus?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await new GoalsProgressTool(goals.Object).RunAsync(Context, Args(new { }));

        result.Code.Should().Be("no_goals");
    }

    [Fact]
    public async Task UpcomingItemsKeepsOnlyActiveItemsDueWithinFourteenDaysEarliestFirst()
    {
        var items = new Mock<IRecurringItemRepository>();
        items.Setup(i => i.ListAsync(Context.UserId, RecurringStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new RecurringItem { Name = "Tiền net", Direction = RecurringDirection.Outflow, AmountCents = 220_000, NextDueDate = new DateOnly(2026, 11, 20) },
            new RecurringItem { Name = "Lương", Direction = RecurringDirection.Inflow, AmountCents = 15_000_000, NextDueDate = new DateOnly(2026, 11, 12) },
            new RecurringItem { Name = "Quá xa", Direction = RecurringDirection.Outflow, AmountCents = 1, NextDueDate = new DateOnly(2026, 12, 25) },
            new RecurringItem { Name = "Đã qua", Direction = RecurringDirection.Outflow, AmountCents = 1, NextDueDate = new DateOnly(2026, 11, 9) },
        ]);

        var result = await new UpcomingItemsTool(items.Object).RunAsync(Context, Args(new { }));

        result.Evidence.Select(e => e.Id).Should().Equal("upcoming_1", "upcoming_2");
        result.Evidence[0].Text.Should().Be("Lương (thu) đến hạn 12/11/2026");
        result.Evidence[0].ValueVnd.Should().Be(15_000_000);
        result.Evidence[1].Text.Should().Be("Tiền net (chi) đến hạn 20/11/2026");
    }

    private static Mock<ICategoryRepository> Categories(params Category[] list)
    {
        var categories = new Mock<ICategoryRepository>();
        categories.Setup(c => c.GetListForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(list.ToList());
        return categories;
    }

    private static Category Cat(string name, string slug, string kind = Category.KindExpense) => new() { Name = name, Slug = slug, Kind = kind, IsActive = true };

    [Theory]
    [InlineData("ăn uống")]
    [InlineData("an uong")]
    [InlineData("ĂN UỐNG")]
    public async Task ProposingALimitFindsTheCategoryIgnoringDiacriticsAndCaseAndOnlyProposes(string said)
    {
        var categories = Categories(Cat("Ăn uống", "an-uong"), Cat("Di chuyển", "di-chuyen"));

        var result = await new ProposeBudgetLimitTool(categories.Object).RunAsync(Context, Args(new { category_name = said, limit_vnd = 3_000_000 }));

        var action = result.Actions!.Single();
        action.Kind.Should().Be(SuggestedActionKinds.SetBudgetLimit);
        (action.CategorySlug, action.CategoryName, action.AmountVnd).Should().Be(("an-uong", "Ăn uống", (long?)3_000_000));
        action.Title.Should().Contain("3.000.000 đồng");
        result.Evidence.Single(e => e.Id == action.EvidenceIds.Single()).ValueVnd.Should().Be(3_000_000);
        categories.Verify(c => c.SetHiddenAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task APartialNameMatchesWhenExactlyOneCategoryContainsIt()
    {
        var categories = Categories(Cat("Ăn uống", "an-uong"), Cat("Di chuyển", "di-chuyen"));

        var result = await new ProposeBudgetLimitTool(categories.Object).RunAsync(Context, Args(new { category_name = "chuyển", limit_vnd = 1_000_000 }));

        result.Actions!.Single().CategorySlug.Should().Be("di-chuyen");
    }

    [Theory]
    [InlineData("xe cộ", "category_not_found")]
    [InlineData("ăn", "category_ambiguous")]
    public async Task UnknownOrAmbiguousCategoryAsksInsteadOfGuessing(string said, string code)
    {
        var categories = Categories(Cat("Ăn uống", "an-uong"), Cat("Ăn vặt", "an-vat"), Cat("Lương", "luong", Category.KindIncome));

        var result = await new ProposeBudgetLimitTool(categories.Object).RunAsync(Context, Args(new { category_name = said, limit_vnd = 1_000_000 }));

        result.Status.Should().Be(ToolStatus.InsufficientData);
        result.Code.Should().Be(code);
        result.Actions.Should().BeNull();
    }

    [Fact]
    public async Task IncomeCategoriesAreNeverProposedAsBudgets()
    {
        var categories = Categories(Cat("Lương", "luong", Category.KindIncome));

        var result = await new ProposeBudgetLimitTool(categories.Object).RunAsync(Context, Args(new { category_name = "lương", limit_vnd = 1_000_000 }));

        result.Code.Should().Be("category_not_found");
    }

    private IReadOnlyList<CategorySpendVnd>? _categories;
    private IReadOnlyDictionary<string, int> _surveyShare = new Dictionary<string, int>();
    private IReadOnlyDictionary<string, long> _surveySpend = new Dictionary<string, long>();
    private UserSurveyAnswer[] _surveyAnswers = [];

    private GoalPlanTool GoalPlan(long[] income, long[] spend, params SavingGoal[] active) => GoalPlan(income, spend, declaredIncome: null, method: null, active);

    private GoalPlanTool GoalPlan(long[] income, long[] spend, long? declaredIncome, params SavingGoal[] active) => GoalPlan(income, spend, declaredIncome, method: null, active);

    /// <param name="method">Cách quản lý chính người dùng đã chọn từ khảo sát: tên và giá trị đang có hiệu lực của các tham số; null = chưa chọn cách nào.</param>
    private GoalPlanTool GoalPlan(long[] income, long[] spend, long? declaredIncome, (string Name, Dictionary<string, long> Effective)? method, params SavingGoal[] active)
    {
        var goals = new Mock<ISavingGoalRepository>();
        goals.Setup(g => g.GetListForUserAsync(It.IsAny<Guid>(), SavingGoalStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync(active.ToList());
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new User { MonthlyIncomeCents = declaredIncome });
        var mine = new List<UserMethodDto>();
        if (method is { } chosen)
        {
            mine.Add(new UserMethodDto(Guid.NewGuid(), new MethodDto("code", chosen.Name, "", "primary", "TrackOnly", "", [], []), "primary", "survey", new Dictionary<string, long>(), chosen.Effective, Now));
        }

        var methods = new Mock<IUserMethodService>();
        methods.Setup(m => m.GetMineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<UserMethodDto>)mine);
        var surveys = new Mock<ISurveyRepository>();
        surveys.Setup(r => r.GetAnswersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(_surveyAnswers.ToList());
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new SystemConfigSnapshot(1, new SystemConfigDocument { Agent = new AgentSettings { SurveySavingShare = _surveyShare, SurveySpendEstimate = _surveySpend } }, null));
        var preliminary = new PreliminaryCapacityService(users.Object, methods.Object, surveys.Object, config.Object);
        return new GoalPlanTool(new InMemoryFinancialDataSource(income, spend, 0, null, _categories), goals.Object, preliminary, config.Object);
    }

    private static readonly long[] Income = [20_000_000, 20_000_000, 20_000_000, 20_000_000, 20_000_000, 20_000_000];
    private static readonly long[] Spend = [10_000_000, 10_000_000, 10_000_000, 10_000_000, 10_000_000, 10_000_000];

    [Fact]
    public async Task AnUnreachableGoalGetsTheMonthlyNeedAndYearRoundedLongerPlansEachWithItsMonthlyAmount()
    {
        // thu 20tr, chi 10tr: góp được 10tr/tháng. 500tr trong 3 năm cần 13.888.889/tháng: chưa khả thi
        var result = await GoalPlan(Income, Spend).RunAsync(Context, Args(new { target_vnd = 500_000_000, months = 36 }));

        result.Evidence.Single(e => e.Id == "plan_required").ValueVnd.Should().Be(13_888_889);
        result.Evidence.Single(e => e.Id == "capacity_base").ValueVnd.Should().Be(10_000_000);
        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: infeasible");
        // 500tr / 10tr = 50 tháng, làm tròn lên năm tròn = 60 tháng (5 năm) rồi 72 tháng (6 năm)
        result.Evidence.Single(e => e.Id == "alt_1_monthly").ValueVnd.Should().Be(8_333_334);
        result.Evidence.Single(e => e.Id == "alt_1_monthly").Text.Should().Contain("60 tháng (5 năm)");
        result.Evidence.Single(e => e.Id == "alt_2_monthly").ValueVnd.Should().Be(6_944_445);
        var actions = result.Actions!;
        actions.Select(a => a.Kind).Should().Equal(SuggestedActionKinds.CreateGoal, SuggestedActionKinds.CreateGoal);
        actions.Select(a => a.Date).Should().Equal("2031-11-10", "2032-11-10");
        actions.Should().OnlyContain(a => a.AmountVnd == 500_000_000);
        actions[0].Title.Should().Contain("5 năm").And.Contain("8.333.334 đồng/tháng");
        actions.SelectMany(a => a.EvidenceIds).Should().OnlyContain(id => result.Evidence.Any(e => e.Id == id));
    }

    [Fact]
    public async Task WhenTheGoalAlreadyExistsTheLongerPlansChangeItsDeadlineInsteadOfCreatingAnother()
    {
        var goal = new SavingGoal { Id = Guid.NewGuid(), Name = "Mua nhà", TargetCents = 500_000_000, CreatedAt = Now };

        var result = await GoalPlan(Income, Spend, goal).RunAsync(Context, Args(new { target_vnd = 500_000_000, months = 36 }));

        var first = result.Actions![0];
        first.Kind.Should().Be(SuggestedActionKinds.ChangeGoalDeadline);
        first.TargetId.Should().Be(goal.Id.ToString());
        first.Date.Should().Be("2031-11-10");
        first.AmountVnd.Should().Be(8_333_334, "số cần góp mỗi tháng theo hạn mới");
        first.Title.Should().Be("Kéo dài thành 5 năm · góp 8.333.334 đồng/tháng");
    }

    [Fact]
    public async Task AReachableGoalIsFeasibleAndSuggestsNothing()
    {
        var result = await GoalPlan(Income, Spend).RunAsync(Context, Args(new { target_vnd = 300_000_000, months = 36 }));

        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: feasible");
        result.Evidence.Should().NotContain(e => e.Id.StartsWith("alt_"));
        result.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task AGoalBetweenTheLowAndTypicalCapacityIsTightAndPlansLongerWithTheCautiousCapacity()
    {
        var spend = new long[] { 5_000_000, 10_000_000, 10_000_000, 10_000_000, 10_000_000, 15_000_000 };
        var probe = await GoalPlan(Income, spend).RunAsync(Context, Args(new { target_vnd = 1_000_000, months = 1 }));
        var cautious = probe.Evidence.Single(e => e.Id == "capacity_conservative").ValueVnd!.Value;
        var typical = probe.Evidence.Single(e => e.Id == "capacity_base").ValueVnd!.Value;
        cautious.Should().BeLessThan(typical);
        var months = 36;
        var target = ((cautious + typical) / 2) * months; // cần góp = trung bình hai mức: trên mức thận trọng, dưới mức điển hình

        var result = await GoalPlan(Income, spend).RunAsync(Context, Args(new { target_vnd = target, months }));

        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: tight");
        result.Actions.Should().NotBeEmpty("tight vẫn gợi ý kéo dài để có vùng đệm");
        result.Evidence.Single(e => e.Id == "alt_1_monthly").ValueVnd.Should().BeLessThanOrEqualTo(cautious, "phương án dài hơn góp vừa trong mức thận trọng");
    }

    [Fact]
    public async Task NoSpareMoneyMeansNoLongerPlanBecauseWaitingLongerCannotHelp()
    {
        var result = await GoalPlan(Income, [20_000_000, 20_000_000, 20_000_000, 20_000_000, 20_000_000, 20_000_000])
            .RunAsync(Context, Args(new { target_vnd = 500_000_000, months = 36 }));

        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: infeasible");
        result.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task TooLittleHistoryAndNoDeclaredIncomeIsInsufficientData()
    {
        var result = await GoalPlan([20_000_000], [10_000_000]).RunAsync(Context, Args(new { target_vnd = 500_000_000, months = 36 }));

        result.Status.Should().Be(ToolStatus.InsufficientData);
        result.Code.Should().Be("history_too_short");
    }

    [Fact]
    public async Task TooLittleHistoryButADeclaredIncomeAndOneMonthOfSpendingGivesAPreliminaryEstimateWithLongerPlans()
    {
        // khai thu nhập 20tr, mới ghi 1 tháng chi 10tr: góp được ~10tr/tháng; 500tr trong 3 năm cần 13.888.889 → khó đạt, kéo dài 5 và 6 năm
        var result = await GoalPlan([20_000_000], [10_000_000], declaredIncome: 20_000_000).RunAsync(Context, Args(new { target_vnd = 500_000_000, months = 36 }));

        result.Status.Should().Be(ToolStatus.Ok);
        result.Evidence.Single(e => e.Id == "income_declared").ValueVnd.Should().Be(20_000_000);
        result.Evidence.Single(e => e.Id == "plan_required").Text.Should().Contain("69%", "13.888.889 / 20.000.000");
        result.Evidence.Single(e => e.Id == "spend_avg").ValueVnd.Should().Be(10_000_000);
        result.Evidence.Single(e => e.Id == "capacity_base").ValueVnd.Should().Be(10_000_000);
        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: infeasible");
        result.Evidence.Single(e => e.Id == "prelim_note").Text.Should().StartWith("Chỉ để tham khảo").And.Contain("1 tháng đã ghi").And.Contain("≥ 3 tháng");
        result.Evidence.Single(e => e.Id == "prelim_note").Key.Should().Be(GoalPlanTool.PreliminaryEvidenceKey, "app tìm bằng khóa này để hiện ghi chú cố định");
        result.Evidence.Single(e => e.Id == "alt_1_monthly").ValueVnd.Should().Be(8_333_334);
        result.Actions!.Select(a => a.Date).Should().Equal("2031-11-10", "2032-11-10");
    }

    [Fact]
    public async Task PreliminaryNeverSaysFeasibleAndFlagsTheReachableCaseAsTightOnly()
    {
        var result = await GoalPlan([20_000_000], [10_000_000], declaredIncome: 20_000_000).RunAsync(Context, Args(new { target_vnd = 300_000_000, months = 36 }));

        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: tight", "dữ liệu còn mỏng: đủ ở mức điển hình nhưng chưa dám nói khả thi");
        result.Actions.Should().BeEmpty();
        result.Evidence.Should().Contain(e => e.Id == "prelim_note");
    }

    [Fact]
    public async Task DeclaredIncomeWithoutAnyRecordedSpendingStatesTheMonthlyNeedAndShareButGivesNoVerdict()
    {
        var result = await GoalPlan([], [], declaredIncome: 20_000_000).RunAsync(Context, Args(new { target_vnd = 500_000_000, months = 36 }));

        result.Status.Should().Be(ToolStatus.Ok);
        result.Evidence.Single(e => e.Id == "plan_required").ValueVnd.Should().Be(13_888_889);
        result.Evidence.Select(e => e.Id).Should().NotContain(["verdict", "capacity_base", "spend_avg"]);
        result.Evidence.Single(e => e.Id == "prelim_note").Text.Should().NotContain("chi tiêu trung bình");
        // chưa biết khả năng góp: chỉ thêm 1 và 2 năm vào hạn đã nêu, không nói là đủ
        result.Actions!.Select(a => a.Date).Should().Equal("2030-11-10", "2031-11-10");
        result.Evidence.Single(e => e.Id == "alt_1_monthly").ValueVnd.Should().Be(CeilDivForTest(500_000_000, 48));
    }

    [Fact]
    public async Task WithoutRecordedSpendingTheSavingShareOfTheChosenMethodGivesTheEstimateAndASurveyBasedNote()
    {
        // khảo sát chọn 50/30/20: phần mục tiêu 20% thu nhập khai 20tr = 4tr/tháng; 500tr trong 3 năm cần 13,9tr → khó đạt; kéo dài 11 năm (500tr / 4tr = 125 tháng → 132 tháng)
        var method = (Name: "Quy tắc 50/30/20", Effective: new Dictionary<string, long> { ["needs_pct"] = 50, ["wants_pct"] = 30, ["goals_pct"] = 20, ["t1_pct"] = 70 });

        var result = await GoalPlan([], [], 20_000_000, method).RunAsync(Context, Args(new { target_vnd = 500_000_000, months = 36 }));

        result.Evidence.Single(e => e.Id == "capacity_base").ValueVnd.Should().Be(4_000_000);
        result.Evidence.Single(e => e.Id == "capacity_base").Text.Should().Contain("Quy tắc 50/30/20").And.Contain("20%");
        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: infeasible");
        result.Evidence.Single(e => e.Id == "prelim_note").Text.Should().Contain("cách quản lý bạn đã chọn").And.Contain("Quy tắc 50/30/20").And.Contain("≥ 3 tháng");
        result.Evidence.Single(e => e.Id == "alt_1_monthly").Text.Should().Contain("132 tháng (11 năm)");
        result.Evidence.Should().NotContain(e => e.Id == "spend_avg");
    }

    [Fact]
    public async Task TheSavingShareSumsEveryConfiguredKeyOfTheMethodAndIsCappedAtOneHundred()
    {
        var jars = (Name: "6 hũ", Effective: new Dictionary<string, long> { ["necessities_pct"] = 55, ["financial_freedom_pct"] = 10, ["long_term_pct"] = 10, ["give_pct"] = 5 });

        var result = await GoalPlan([], [], 20_000_000, jars).RunAsync(Context, Args(new { target_vnd = 100_000_000, months = 36 }));

        result.Evidence.Single(e => e.Id == "capacity_base").ValueVnd.Should().Be(4_000_000, "tự do tài chính 10% + tiết kiệm dài hạn 10% = 20% của 20tr");
        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: tight", "2.777.778 cần góp ≤ 4tr nhưng dữ liệu mỏng nên chỉ \"cần cân nhắc\"");
    }

    [Fact]
    public async Task ARecordedSpendingBeatsTheMethodShareBecauseItIsRealData()
    {
        var method = (Name: "Quy tắc 50/30/20", Effective: new Dictionary<string, long> { ["goals_pct"] = 20 });

        var result = await GoalPlan([20_000_000], [15_000_000], 20_000_000, method).RunAsync(Context, Args(new { target_vnd = 500_000_000, months = 36 }));

        result.Evidence.Single(e => e.Id == "capacity_base").ValueVnd.Should().Be(5_000_000, "thu khai 20tr trừ chi thật 15tr, không phải 20% = 4tr");
        result.Evidence.Single(e => e.Id == "prelim_note").Text.Should().Contain("1 tháng đã ghi").And.NotContain("Quy tắc 50/30/20");
    }

    [Fact]
    public async Task AMethodWithoutAnySavingKeyLikeTrackingOnlyGivesNoEstimate()
    {
        var tracking = (Name: "Theo dõi chi tiêu tối giản", Effective: new Dictionary<string, long> { ["tracked_groups"] = 4, ["observe_weeks"] = 3 });

        var result = await GoalPlan([], [], 20_000_000, tracking).RunAsync(Context, Args(new { target_vnd = 500_000_000, months = 36 }));

        result.Evidence.Select(e => e.Id).Should().NotContain(["capacity_base", "verdict"]);
        result.Evidence.Should().Contain(e => e.Id == "prelim_note");
    }

    [Fact]
    public async Task PreliminaryWithNoSpareMoneyOffersNoLongerPlan()
    {
        var result = await GoalPlan([20_000_000], [25_000_000], declaredIncome: 20_000_000).RunAsync(Context, Args(new { target_vnd = 500_000_000, months = 36 }));

        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: infeasible");
        result.Actions.Should().BeEmpty();
    }

    private static long CeilDivForTest(long number, long divisor) => (number + divisor - 1) / divisor;

    [Theory]
    [InlineData(36, "3 năm")]
    [InlineData(42, "3 năm 6 tháng")]
    [InlineData(6, "6 tháng")]
    public void YearsReadsNaturally(int months, string text)
    {
        GoalPlanTool.Years(months).Should().Be(text);
    }

    [Fact]
    public async Task InfeasibleGoalSuggestsCuttingTheBiggestCategoryAndSettingItsLimit()
    {
        _categories = [new CategorySpendVnd("food", "Ăn uống", 9_000_000), new CategorySpendVnd("shopping", "Mua sắm", 3_000_000)];

        var result = await GoalPlan(Income, Spend).RunAsync(Context, Args(new { target_vnd = 2_000_000_000L, months = 36 }));

        // 3 tháng ăn uống 9tr → trung bình 3tr/tháng, giảm 10% = 300k; hạn mức mới 2.700.000
        result.Evidence.Should().Contain(e => e.Id == "cut_saving" && e.ValueVnd == 300_000);
        var action = result.Actions!.Single(a => a.Kind == SuggestedActionKinds.SetBudgetLimit);
        action.CategorySlug.Should().Be("food");
        action.AmountVnd.Should().Be(2_700_000);
        result.Evidence.Single(e => e.Id == "cut_limit").ValueVnd.Should().Be(2_700_000, "số trên nút Đặt hạn mức phải có trong Vì sao?");
        action.EvidenceIds.Should().Contain("cut_limit");
    }

    [Fact]
    public async Task WithNoSpendNoMethodTheSurveyAnswerSetsTheCapacity()
    {
        _surveyShare = new Dictionary<string, int> { ["month_end_balance:C"] = 5, ["saving_style:E"] = 0 };
        _surveyAnswers = [new UserSurveyAnswer { QuestionCode = "month_end_balance", OptionCode = "C" }];

        var result = await GoalPlan([], [], declaredIncome: 10_000_000).RunAsync(Context, Args(new { target_vnd = 100_000_000L, months = 36 }));

        result.Evidence.Should().Contain(e => e.Id == "capacity_base" && e.ValueVnd == 500_000); // 5% của 10 triệu
        result.Evidence.Single(e => e.Id == "prelim_note").Text.Should().Contain("khảo sát");
    }

    [Fact]
    public async Task TheDeclaredSpendRangeBeatsTheSurveyPercentage()
    {
        _surveySpend = new Dictionary<string, long> { ["avg_monthly_spend:B"] = 7_500_000 };
        _surveyShare = new Dictionary<string, int> { ["month_end_balance:C"] = 5 };
        _surveyAnswers = [new UserSurveyAnswer { QuestionCode = "avg_monthly_spend", OptionCode = "B" }, new UserSurveyAnswer { QuestionCode = "month_end_balance", OptionCode = "C" }];

        var result = await GoalPlan([], [], declaredIncome: 10_000_000).RunAsync(Context, Args(new { target_vnd = 100_000_000L, months = 36 }));

        result.Evidence.Should().Contain(e => e.Id == "spend_declared" && e.ValueVnd == 7_500_000);
        result.Evidence.Should().Contain(e => e.Id == "capacity_base" && e.ValueVnd == 2_500_000); // 10 triệu − 7,5 triệu
    }

    [Fact]
    public async Task TheLowestSurveyShareWinsAndNoConfiguredShareMeansNoEstimate()
    {
        _surveyShare = new Dictionary<string, int> { ["month_end_balance:A"] = 20, ["saving_style:E"] = 0 };
        _surveyAnswers = [new UserSurveyAnswer { QuestionCode = "month_end_balance", OptionCode = "A" }, new UserSurveyAnswer { QuestionCode = "saving_style", OptionCode = "E" }];

        var lowest = await GoalPlan([], [], declaredIncome: 10_000_000).RunAsync(Context, Args(new { target_vnd = 100_000_000L, months = 36 }));
        lowest.Evidence.Should().Contain(e => e.Id == "capacity_base" && e.ValueVnd == 0);

        _surveyShare = new Dictionary<string, int>();
        var none = await GoalPlan([], [], declaredIncome: 10_000_000).RunAsync(Context, Args(new { target_vnd = 100_000_000L, months = 36 }));
        none.Evidence.Should().NotContain(e => e.Id == "capacity_base");
    }

    [Fact]
    public async Task ForecastGoalAlsoReadsTheSurveyWhenHistoryIsShort()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new User { MonthlyIncomeCents = 10_000_000 });
        var methods = new Mock<IUserMethodService>();
        methods.Setup(m => m.GetMineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<UserMethodDto>)[]);
        var surveys = new Mock<ISurveyRepository>();
        surveys.Setup(r => r.GetAnswersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([new UserSurveyAnswer { QuestionCode = "month_end_balance", OptionCode = "A" }]);
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new SystemConfigSnapshot(1, new SystemConfigDocument { Agent = new AgentSettings { SurveySavingShare = new Dictionary<string, int> { ["month_end_balance:A"] = 20 } } }, null));
        var tool = new ForecastGoalTool(
            new InMemoryFinancialDataSource([], [], 0), new GetAssumptionsTool(Mock.Of<IEconomicAssumptionRepository>()),
            new PreliminaryCapacityService(users.Object, methods.Object, surveys.Object, config.Object), config.Object);

        var result = await tool.RunAsync(Context, Args(new { price_vnd = 600_000_000L, horizon_months = 24 }));

        result.Status.Should().Be(ToolStatus.Ok);
        result.Evidence.Should().Contain(e => e.Id == "capacity_base" && e.ValueVnd == 2_000_000); // 20% của 10 triệu
        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: infeasible"); // cần 25 triệu/tháng
    }

    [Fact]
    public async Task NoCategorySpendMeansNoCutSuggestion()
    {
        var result = await GoalPlan(Income, Spend).RunAsync(Context, Args(new { target_vnd = 2_000_000_000L, months = 36 }));

        result.Actions!.Should().NotContain(a => a.Kind == SuggestedActionKinds.SetBudgetLimit);
    }

    [Fact]
    public void AdminTextTemplatesReplaceDefaultsAndBadOnesFallBack()
    {
        var texts = new Dictionary<string, string> { ["duration_years"] = "{years} năm tròn", ["duration_months"] = "{oops} tháng" };

        GoalPlanTool.Years(36, texts).Should().Be("3 năm tròn");
        GoalPlanTool.Years(6, texts).Should().Be("6 tháng"); // mẫu sai chỗ trống → rơi về mặc định
        AgentTexts.Problem("duration_years", "{years} năm {months}").Should().NotBeNull();
        AgentTexts.Problem("khong_co", "x").Should().NotBeNull();
        AgentTexts.Problem("duration_years", "{years} năm").Should().BeNull();
    }

    [Fact]
    public void TheToolsDeclareTheirMoneyArgumentWithTheNameTheAiServiceGuards()
    {
        new ProposeBudgetLimitTool(Mock.Of<ICategoryRepository>()).Spec.Args.Select(a => a.Name).Should().Contain("limit_vnd");
        new BudgetStatusTool(Mock.Of<IBudgetForecastService>()).Spec.Args.Should().BeEmpty();
    }
}
