using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinanceMethods;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;
using static FinMate.Tests.Unit.Application.FinanceMethods.FinanceTestData;

namespace FinMate.Tests.Unit.Application.FinanceMethods;

/// <summary>Mục tiêu theo phương pháp: gợi ý mẫu, tiền khả dụng dùng chung và xem trước tính khả thi.</summary>
public class GoalPlanningServiceTests
{
    private readonly Mock<IFinanceMethodRepository> _methods = new();
    private readonly Mock<IUserMethodRepository> _userMethods = new();
    private readonly Mock<ISurveyRepository> _surveys = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IBudgetRepository> _budgets = new();
    private readonly Mock<ISavingGoalRepository> _goals = new();
    private readonly Mock<IFinanceSnapshotRepository> _snapshots = new();
    private readonly GoalPlanningService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly User _user;

    public GoalPlanningServiceTests()
    {
        _user = new User { Id = _userId };
        _users.Setup(u => u.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _userMethods.Setup(r => r.GetActiveAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _surveys.Setup(s => s.GetAnswersAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _surveys.Setup(s => s.GetQuestionsAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _surveys.Setup(s => s.GetOptionsAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _goals.Setup(g => g.GetListForUserAsync(_userId, SavingGoalStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _budgets.Setup(b => b.GetListForUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _snapshots.Setup(s => s.GetGoalDepositsAsync(_userId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, long>());

        _service = new GoalPlanningService(
            _methods.Object, _userMethods.Object, _surveys.Object, _users.Object, _budgets.Object, _goals.Object, _snapshots.Object);
    }

    // ---- tiền khả dụng ----

    private SavingGoal Goal(long target, long saved, int daysToDeadline)
    {
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Name = "Laptop",
            TargetCents = target,
            SavedCents = saved,
            Status = SavingGoalStatus.Active,
            Deadline = daysToDeadline > 0 ? DateTimeOffset.UtcNow.AddDays(daysToDeadline) : null,
        };
        _goals.Setup(g => g.GetListForUserAsync(_userId, SavingGoalStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync([goal]);
        return goal;
    }

    private void UsePrimary(GoalMode mode)
    {
        var method = Method("primary", goalMode: mode);
        var userMethod = new UserMethod { Id = Guid.NewGuid(), UserId = _userId, MethodId = method.Id, Role = UserMethodRole.Primary, Status = UserMethodStatus.Active };
        _userMethods.Setup(r => r.GetActiveAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([userMethod]);
        _methods.Setup(m => m.GetMethodByIdAsync(method.Id, It.IsAny<CancellationToken>())).ReturnsAsync(method);
    }

    private void Month(long spent, params (Guid Goal, long Amount)[] deposits)
    {
        _snapshots.Setup(s => s.GetDebitTotalAsync(_userId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(spent);
        _snapshots.Setup(s => s.GetGoalDepositsAsync(_userId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deposits.ToDictionary(d => d.Goal, d => d.Amount));
    }

    [Fact]
    public async Task Spendable_WithoutDeclaredIncome_IsUnknownNotZero()
    {
        Month(spent: 2_000_000);

        var spendable = await _service.GetSpendableAsync(_userId);

        spendable.IncomeCents.Should().BeNull();
        spendable.SpendableCents.Should().BeNull("'chưa biết' không phải 'bằng 0'");
        spendable.SpentCents.Should().Be(2_000_000);
    }

    [Fact]
    public async Task Spendable_WithoutAMethod_GoalsDoNotChangeIt()
    {
        _user.MonthlyIncomeCents = 20_000_000;
        var goal = Goal(12_000_000, 0, 120);
        Month(spent: 5_000_000, (goal.Id, 1_000_000));

        var spendable = await _service.GetSpendableAsync(_userId);

        spendable.GoalMode.Should().BeNull();
        spendable.GoalSetAsideCents.Should().Be(0);
        spendable.SpendableCents.Should().Be(15_000_000);
    }

    [Fact]
    public async Task Spendable_ReserveFirst_SetsAsideTheRequiredMonthlyAmountUpfront()
    {
        _user.MonthlyIncomeCents = 20_000_000;
        UsePrimary(GoalMode.ReserveFirst);
        var goal = Goal(12_000_000, 0, 120);   // 4 tháng → 3 triệu/tháng
        Month(spent: 5_000_000);

        var spendable = await _service.GetSpendableAsync(_userId);

        spendable.GoalMode.Should().Be("reserve_first");
        spendable.Goals.Should().ContainSingle().Which.RequiredPerMonthCents.Should().Be(3_000_000);
        spendable.GoalSetAsideCents.Should().Be(3_000_000);
        spendable.SpendableCents.Should().Be(20_000_000 - 5_000_000 - 3_000_000);
    }

    [Fact]
    public async Task Spendable_ReserveFirst_DepositingMoreThanRequiredCountsTheDeposit_NeverBoth()
    {
        _user.MonthlyIncomeCents = 20_000_000;
        UsePrimary(GoalMode.ReserveFirst);
        var goal = Goal(12_000_000, 0, 120);
        Month(spent: 0, (goal.Id, 5_000_000));

        var spendable = await _service.GetSpendableAsync(_userId);

        spendable.GoalSetAsideCents.Should().Be(5_000_000, "đã nạp nhiều hơn mức cần góp thì trừ số đã nạp, không trừ thêm lần nữa");
    }

    [Fact]
    public async Task Spendable_AvailableToSpend_OnlyDeductsWhatWasActuallyDeposited()
    {
        _user.MonthlyIncomeCents = 20_000_000;
        UsePrimary(GoalMode.AvailableToSpend);
        var goal = Goal(12_000_000, 0, 120);
        Month(spent: 5_000_000, (goal.Id, 1_000_000));

        var spendable = await _service.GetSpendableAsync(_userId);

        spendable.GoalSetAsideCents.Should().Be(1_000_000);
        spendable.SpendableCents.Should().Be(14_000_000);
    }

    [Fact]
    public async Task Spendable_TrackOnly_GoalsNeverAffectIt()
    {
        _user.MonthlyIncomeCents = 20_000_000;
        UsePrimary(GoalMode.TrackOnly);
        var goal = Goal(12_000_000, 0, 120);
        Month(spent: 5_000_000, (goal.Id, 1_000_000));

        (await _service.GetSpendableAsync(_userId)).SpendableCents.Should().Be(15_000_000);
    }

    [Fact]
    public async Task Spendable_AGoalWithoutDeadline_ReservesNothingUpfront()
    {
        _user.MonthlyIncomeCents = 20_000_000;
        UsePrimary(GoalMode.ReserveFirst);
        Goal(12_000_000, 0, daysToDeadline: 0);
        Month(spent: 0);

        (await _service.GetSpendableAsync(_userId)).GoalSetAsideCents.Should().Be(0);
    }

    [Fact]
    public async Task Spendable_CanGoNegative_WhenTheUserHasOverspent()
    {
        _user.MonthlyIncomeCents = 10_000_000;
        Month(spent: 12_000_000);

        (await _service.GetSpendableAsync(_userId)).SpendableCents.Should().Be(-2_000_000);
    }

    // ---- gợi ý mẫu mục tiêu ----

    private (FinanceMethod Primary, FinanceMethod Support, GoalTemplate A, GoalTemplate B, GoalTemplate C) ArrangeSuggestions()
    {
        var primary = Method("main");
        var support = Method("tool", FinanceMethodRole.Supporting);
        var a = new GoalTemplate { Id = Guid.NewGuid(), Code = "a", Name = "A", Description = "dA", DefaultParams = """{"weeks":1}""", IsActive = true };
        var b = new GoalTemplate { Id = Guid.NewGuid(), Code = "b", Name = "B", Description = "dB", DefaultParams = "{}", IsActive = true };
        var c = new GoalTemplate { Id = Guid.NewGuid(), Code = "c", Name = "C", Description = "dC", DefaultParams = "{}", IsActive = false };

        UserMethod Mine(FinanceMethod m, UserMethodRole role) =>
            new() { Id = Guid.NewGuid(), UserId = _userId, MethodId = m.Id, Role = role, Status = UserMethodStatus.Active };

        _userMethods.Setup(r => r.GetActiveAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Mine(support, UserMethodRole.Supporting), Mine(primary, UserMethodRole.Primary)]);
        _methods.Setup(m => m.GetMethodsByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([primary, support]);
        _methods.Setup(m => m.GetTemplatesAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync([a, b]);   // c đã tắt nên repository không trả
        _methods.Setup(m => m.GetLinksForMethodsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new MethodGoalTemplate { MethodId = primary.Id, TemplateId = a.Id, SortOrder = 1, Conditions = """{"excludes":["cash_shortfall"]}""" },
            new MethodGoalTemplate { MethodId = primary.Id, TemplateId = c.Id, SortOrder = 2 },
            new MethodGoalTemplate { MethodId = support.Id, TemplateId = a.Id, SortOrder = 1 },
            new MethodGoalTemplate { MethodId = support.Id, TemplateId = b.Id, SortOrder = 2 },
        ]);
        return (primary, support, a, b, c);
    }

    private void UserHasSignals(string signalsJson)
    {
        var qid = Guid.NewGuid();
        _surveys.Setup(s => s.GetAnswersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserSurveyAnswer { UserId = _userId, QuestionCode = "q", OptionCode = "A" }]);
        _surveys.Setup(s => s.GetQuestionsAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync([new SurveyQuestion { Id = qid, Code = "q" }]);
        _surveys.Setup(s => s.GetOptionsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SurveyOption { QuestionId = qid, Code = "A", Signals = signalsJson }]);
    }

    [Fact]
    public async Task Suggestions_ComeFromTheActiveMethods_PrimaryFirst_WithoutDuplicates_AndSkipInactiveTemplates()
    {
        ArrangeSuggestions();

        var suggestions = await _service.GetSuggestionsAsync(_userId);

        suggestions.Select(s => s.TemplateCode).Should().Equal("a", "b");
        suggestions[0].MethodCode.Should().Be("main", "mẫu 'a' có ở cả hai cách nhưng được tính cho cách chính");
        suggestions[0].DefaultParams!.Value.GetProperty("weeks").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Suggestions_ASignalInTheConditionsExcludesTheTemplate_ButAnotherMethodMayStillOfferIt()
    {
        ArrangeSuggestions();
        UserHasSignals("""{"cash_shortfall":3}""");

        var suggestions = await _service.GetSuggestionsAsync(_userId);

        suggestions.Select(s => (s.TemplateCode, s.MethodCode)).Should().Equal(("a", "tool"), ("b", "tool"));
    }

    [Fact]
    public async Task Suggestions_WithoutAnActiveMethod_AreEmpty()
        => (await _service.GetSuggestionsAsync(_userId)).Should().BeEmpty();

    // ---- xem trước tính khả thi ----

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(1_000_000, -1)]
    public async Task Preview_RejectsNonPositiveTargetOrNegativeSaved(long target, long saved)
    {
        var act = () => _service.PreviewFeasibilityAsync(_userId, target, saved, DateTimeOffset.UtcNow.AddDays(30));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(FinanceErrorCodes.FeasibilityInputInvalid);
    }

    [Fact]
    public async Task Preview_NothingLeftToSave_ReturnsNull()
        => (await _service.PreviewFeasibilityAsync(_userId, 5_000_000, 5_000_000, DateTimeOffset.UtcNow.AddDays(30))).Should().BeNull();

    [Fact]
    public async Task Preview_WithoutIncome_StillSaysHowMuchPerMonth_ButDoesNotConclude()
    {
        var result = await _service.PreviewFeasibilityAsync(_userId, 12_000_000, 0, DateTimeOffset.UtcNow.AddDays(120));

        result!.RequiredPerMonthCents.Should().Be(3_000_000);
        result.IsFeasible.Should().BeNull();
        _budgets.Verify(b => b.GetListForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never, "chưa biết thu nhập thì tổng hạn mức không dùng vào đâu");
    }

    [Fact]
    public async Task Preview_FeasibleAndInfeasible_UseIncomeMinusTheBudgetLimits()
    {
        _user.MonthlyIncomeCents = 10_000_000;
        _budgets.Setup(b => b.GetListForUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Budget { LimitCents = 6_000_000 }]);   // còn 4 triệu mỗi tháng

        var feasible = await _service.PreviewFeasibilityAsync(_userId, 12_000_000, 0, DateTimeOffset.UtcNow.AddDays(120));
        feasible!.IsFeasible.Should().BeTrue();
        feasible.AvailablePerMonthCents.Should().Be(4_000_000);

        var infeasible = await _service.PreviewFeasibilityAsync(_userId, 24_000_000, 0, DateTimeOffset.UtcNow.AddDays(120));
        infeasible!.IsFeasible.Should().BeFalse();
        infeasible.SuggestedDeadline.Should().NotBeNull("còn dư 4 triệu mỗi tháng nên kéo dài hạn là gợi ý hợp lý");
        infeasible.SuggestedTargetCents.Should().Be(16_000_000);
    }
}
