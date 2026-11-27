using FinMate.Application.FinanceMethods;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.FinanceMethods;

/// <summary>Mức chi an toàn hôm nay: phần còn lại của tháng tính đến đầu ngày chia đều cho số ngày còn lại kể cả hôm nay.</summary>
public class SafeTodayTests
{
    private static readonly DateTimeOffset Nov27NoonVn = new(2026, 11, 27, 12, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void RemainingOfTheMonthIsSplitOverTheDaysLeftIncludingToday()
        => GoalPlanningService.SafeToday(10_000_000, 1_000_000, 5_000_000, Nov27NoonVn).Should().Be(1_000_000); // (10tr − 1tr − 5tr) ÷ 4 ngày (27..30)

    [Fact]
    public void TotalBudgetSmallerThanIncomeCapsTheMonth()
        => GoalPlanningService.SafeToday(17_000_000, 0, 0, Nov27NoonVn, 5_000_000).Should().Be(1_250_000); // min(17tr, 5tr) ÷ 4 ngày

    [Fact]
    public void TotalBudgetLargerThanIncomeDoesNotRaiseTheMonth()
        => GoalPlanningService.SafeToday(4_000_000, 0, 0, Nov27NoonVn, 9_000_000).Should().Be(1_000_000);

    [Fact]
    public void BudgetAloneIsEnoughWhenIncomeIsUnknown()
        => GoalPlanningService.SafeToday(null, 0, 0, Nov27NoonVn, 8_000_000).Should().Be(2_000_000);

    [Fact]
    public void OverspentMonthIsNegativeAndNoIncomeIsUnknown()
    {
        GoalPlanningService.SafeToday(10_000_000, 0, 20_938_000, Nov27NoonVn).Should().BeNegative();
        GoalPlanningService.SafeToday(null, 0, 0, Nov27NoonVn).Should().BeNull();
    }
}
