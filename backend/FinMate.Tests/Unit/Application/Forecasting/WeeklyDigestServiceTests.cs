using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>
/// Bản tin tuần: dựng bằng code từ ngân sách và mục tiêu, không số tiền, mỗi người tối đa một bản mỗi tuần (khoá theo thứ Hai), không gửi khi không có gì để tổng kết.
/// </summary>
public class WeeklyDigestServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static BudgetForecastDto Budget(string? name, int percent, string status)
        => new(Guid.NewGuid(), null, name, 1_000_000, 900_000, 1_200_000, percent, status, 0, 5, "habit");

    private static (WeeklyDigestService Service, Mock<IForecastAlertDispatcher> Dispatcher) Build(BudgetForecastDto[] budgets, params SavingGoal[] goals)
    {
        var budgetService = new Mock<IBudgetForecastService>();
        budgetService.Setup(b => b.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(budgets);
        var goalRepo = new Mock<ISavingGoalRepository>();
        goalRepo.Setup(g => g.GetListForUserAsync(UserId, SavingGoalStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync(goals.ToList());
        var dispatcher = new Mock<IForecastAlertDispatcher>();
        dispatcher.Setup(d => d.RaiseAsync(It.IsAny<ForecastAlertRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ForecastAlert());
        return (new WeeklyDigestService(budgetService.Object, goalRepo.Object, dispatcher.Object), dispatcher);
    }

    [Fact]
    public async Task NamesTheRiskyBudgetsMostStretchedFirstAndTheNearestGoalWithPercentOnly()
    {
        var (service, dispatcher) = Build(
            [Budget("Ăn uống", 120, "at_risk"), Budget("Nhà ở", 140, "over"), Budget("Di chuyển", 30, "ok")],
            new SavingGoal { Name = "Du lịch", TargetCents = 20_000_000, SavedCents = 5_000_000, Deadline = DateTimeOffset.UtcNow.AddMonths(3) },
            new SavingGoal { Name = "Mua xe", TargetCents = 600_000_000, SavedCents = 1, Deadline = null });

        (await service.SendAsync(UserId)).Should().BeTrue();

        dispatcher.Verify(d => d.RaiseAsync(
            It.Is<ForecastAlertRequest>(r =>
                r.Kind == ForecastAlertKind.WeeklyDigest
                && r.Body == "2 ngân sách có nguy cơ vượt: Nhà ở, Ăn uống. Mục tiêu \"Du lịch\" đã đạt 25%."
                && r.DedupKey.StartsWith("weekly_digest:")
                && !r.Body.Any(c => c == '₫') && !r.Body.Contains("000.000")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoRiskyBudgetSaysSoInsteadOfStayingSilent()
    {
        var (service, dispatcher) = Build([Budget("Ăn uống", 40, "ok")]);

        await service.SendAsync(UserId);

        dispatcher.Verify(d => d.RaiseAsync(It.Is<ForecastAlertRequest>(r => r.Body == "Tuần này chưa ngân sách nào có nguy cơ vượt."), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NothingToSummariseSendsNothing()
    {
        var (service, dispatcher) = Build([]);

        (await service.SendAsync(UserId)).Should().BeFalse();

        dispatcher.Verify(d => d.RaiseAsync(It.IsAny<ForecastAlertRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("2026-11-16", "2026-11-16")]   // thứ Hai
    [InlineData("2026-11-19", "2026-11-16")]   // thứ Năm
    [InlineData("2026-11-22", "2026-11-16")]   // Chủ nhật vẫn thuộc tuần của thứ Hai trước đó
    public void TheDedupKeyIsTheMondayOfTheWeek(string day, string monday)
    {
        WeeklyDigestService.MondayOf(DateOnly.Parse(day)).Should().Be(DateOnly.Parse(monday));
    }

    [Fact]
    public async Task AlreadyRaisedThisWeekReportsFalse()
    {
        var (service, dispatcher) = Build([Budget("Ăn uống", 120, "at_risk")]);
        dispatcher.Setup(d => d.RaiseAsync(It.IsAny<ForecastAlertRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((ForecastAlert?)null);

        (await service.SendAsync(UserId)).Should().BeFalse();
    }
}
