using FinMate.Application.FinanceMethods;
using FinMate.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.FinanceMethods;

/// <summary>
/// Kế hoạch tháng từ khảo sát (đặc tả 2026-11-23-onboarding-financial-plan-design.md §6): code tính, không LLM. Số ví dụ: thu 10 triệu, chia 50/30/20.
/// </summary>
public class FinancialPlanCalculatorTests
{
    private const long Income = 10_000_000;

    private static readonly PlanGroup[] FiftyThirtyTwenty =
    [
        new("needs_pct", "Nhu cầu", AllocationKinds.Spend, ["housing", "bills", "food", "transport", "health", "family"], 50),
        new("wants_pct", "Mong muốn", AllocationKinds.Spend, ["shopping", "entertainment", "education"], 30),
        new("goals_pct", "Mục tiêu", AllocationKinds.Save, [], 20),
    ];

    private static PlanInput Input(
        long income,
        Dictionary<string, long> fixedByCategory,
        Dictionary<string, long> declared,
        IReadOnlyList<PlanGroup> groups,
        long fixedOther = 0)
        => new(income, fixedByCategory, fixedOther, declared, groups);

    private static PlanCategoryLine Line(FinancialPlan plan, string slug) => plan.Categories.Single(c => c.Slug == slug);

    private static PlanInput UnderTheCapInput(long food = 1_500_000) => Input(
        Income,
        new() { ["housing"] = 2_500_000, ["bills"] = 500_000 },
        new() { ["food"] = food, ["transport"] = 500_000, ["shopping"] = 1_000_000 },
        FiftyThirtyTwenty);

    [Fact]
    public void UnderTheCap_KeepsWhatTheUserDeclared()
    {
        var plan = FinancialPlanCalculator.Calculate(UnderTheCapInput());

        plan.Categories.Should().OnlyContain(c => c.LimitVnd == c.DeclaredVnd && c.ReducedByVnd == 0);
        Line(plan, "housing").LimitVnd.Should().Be(2_500_000);
        Line(plan, "food").GroupKey.Should().Be("needs_pct");
        plan.FixedVnd.Should().Be(3_000_000);
        plan.PlannedSpendVnd.Should().Be(6_000_000);
        plan.SurplusVnd.Should().Be(4_000_000);
        plan.SaveTargetVnd.Should().Be(2_000_000);
        plan.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void OverTheCap_ShrinksOnlyFlexibleSpendingInThatGroup()
    {
        var plan = FinancialPlanCalculator.Calculate(UnderTheCapInput(food: 2_500_000));

        Line(plan, "food").LimitVnd.Should().Be(1_666_000);
        Line(plan, "food").ReducedByVnd.Should().Be(834_000);
        Line(plan, "transport").LimitVnd.Should().Be(333_000);
        Line(plan, "transport").ReducedByVnd.Should().Be(167_000);
        Line(plan, "housing").LimitVnd.Should().Be(2_500_000);
        Line(plan, "shopping").LimitVnd.Should().Be(1_000_000);
        plan.SurplusVnd.Should().Be(4_001_000);
        plan.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void FixedCostsAboveTheCap_ZeroFlexibleAndWarn()
    {
        var plan = FinancialPlanCalculator.Calculate(Input(
            Income,
            new() { ["housing"] = 6_000_000 },
            new() { ["food"] = 1_500_000, ["transport"] = 500_000 },
            FiftyThirtyTwenty));

        Line(plan, "food").LimitVnd.Should().Be(0);
        Line(plan, "transport").LimitVnd.Should().Be(0);
        Line(plan, "housing").LimitVnd.Should().Be(6_000_000, "khoản cố định không bao giờ bị co");
        plan.Warnings.Should().Contain("fixed_exceeds_group:needs_pct");
    }

    [Fact]
    public void SpendingAboveIncome_StillReturnsAPlanFlaggedDeficit()
    {
        var plan = FinancialPlanCalculator.Calculate(Input(
            5_000_000,
            new() { ["housing"] = 3_000_000 },
            new(),
            [],
            fixedOther: 3_000_000));

        plan.SurplusVnd.Should().Be(-1_000_000);
        plan.FixedVnd.Should().Be(6_000_000);
        plan.Warnings.Should().Contain("deficit").And.NotContain("below_save_target");
    }

    [Fact]
    public void DeclaringMoreThanTheIncome_ShrinksCappedGroups_AndKeepsUncappedOnes()
    {
        // Ba danh mục khai ≥ 30% mỗi cái: nhóm có trần bị co, nhóm "phần còn lại" giữ nguyên nên vẫn có thể thâm hụt.
        PlanGroup[] groups =
        [
            new("living_pct", "Sinh hoạt", AllocationKinds.Spend, ["food", "shopping"], 70),
            new(null, "Còn lại", AllocationKinds.Spend, ["entertainment"], null),
        ];
        var plan = FinancialPlanCalculator.Calculate(Input(
            Income, new(), new() { ["food"] = 4_000_000, ["shopping"] = 4_000_000, ["entertainment"] = 4_000_000 }, groups));

        (Line(plan, "food").LimitVnd + Line(plan, "shopping").LimitVnd).Should().BeLessThanOrEqualTo(7_000_000);
        Line(plan, "entertainment").LimitVnd.Should().Be(4_000_000);
        plan.SurplusVnd.Should().BeNegative();
        plan.Warnings.Should().Contain("deficit");
    }

    [Fact]
    public void AnEmptyAllocation_KeepsDeclaredSpending()
    {
        var plan = FinancialPlanCalculator.Calculate(Input(Income, new() { ["housing"] = 2_000_000 }, new() { ["food"] = 3_000_000 }, []));

        Line(plan, "food").LimitVnd.Should().Be(3_000_000);
        Line(plan, "food").GroupKey.Should().BeNull();
        plan.SurplusVnd.Should().Be(5_000_000);
        plan.SaveTargetVnd.Should().Be(0);
        plan.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void SurplusBelowTheSaveTarget_Warns()
    {
        PlanGroup[] groups =
        [
            new(null, "Chi tiêu", AllocationKinds.Spend, ["food", "shopping"], null),
            new("saving_pct", "Tiết kiệm", AllocationKinds.Save, [], 20),
        ];
        var plan = FinancialPlanCalculator.Calculate(Input(Income, new(), new() { ["food"] = 6_000_000, ["shopping"] = 3_000_000 }, groups));

        plan.SurplusVnd.Should().Be(1_000_000);
        plan.SaveTargetVnd.Should().Be(2_000_000);
        plan.Warnings.Should().Equal("below_save_target");
    }

    [Fact]
    public void Categories_AreSortedByLimitDescending_ThenBySlug()
    {
        var plan = FinancialPlanCalculator.Calculate(Input(
            Income, new(), new() { ["transport"] = 500_000, ["food"] = 1_000_000, ["bills"] = 500_000 }, FiftyThirtyTwenty));

        plan.Categories.Select(c => c.Slug).Should().Equal("food", "bills", "transport");
    }

    [Theory]
    [InlineData(1_500_000)]
    [InlineData(2_500_000)]
    [InlineData(9_999_999)]
    public void TotalsAlwaysAddUpToIncome(long food)
    {
        var plan = FinancialPlanCalculator.Calculate(UnderTheCapInput(food));

        (plan.PlannedSpendVnd + plan.SurplusVnd).Should().Be(plan.IncomeVnd);
        plan.Categories.Sum(c => c.LimitVnd).Should().Be(plan.PlannedSpendVnd);
        plan.Categories.Should().OnlyContain(c => c.LimitVnd % 1_000 == 0);
    }
}
