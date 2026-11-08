using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>
/// Nguồn dữ liệu thật của mọi công cụ agent. Chuyển khoản nội bộ và khoản cho vay/mượn đã bị <c>ReportRepository.GetDailySpendAsync</c> loại sẵn
/// (chỉ cộng Debit/Credit, bỏ slug cho vay), nên ở đây không kiểm lại mà kiểm cách gom tháng và cửa sổ thời gian.
/// </summary>
public class RepositoryFinancialDataSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(7));
    private static readonly Guid UserId = Guid.NewGuid();

    private static DailySpendPoint Point(int month, int day, long spent, long income)
        => new(new DateOnly(2026, month, day), spent, income, 1);

    private static RepositoryFinancialDataSource Build(
        IEnumerable<DailySpendPoint> points,
        IEnumerable<FinancialAccount>? accounts = null,
        string? overrideValue = null,
        Mock<IReportRepository>? report = null)
    {
        report ??= new Mock<IReportRepository>();
        report.Setup(r => r.GetDailySpendAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(points.ToList());

        var accountRepo = new Mock<IFinancialAccountRepository>();
        accountRepo.Setup(a => a.GetListByUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((accounts ?? []).ToList());

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IncomeProfileOverride = overrideValue });

        return new RepositoryFinancialDataSource(report.Object, accountRepo.Object, userRepo.Object);
    }

    [Fact]
    public async Task window_is_the_last_full_months_and_excludes_the_current_partial_month()
    {
        var report = new Mock<IReportRepository>();
        var source = Build([Point(10, 25, 0, 10_000_000)], report: report);

        await source.GetMonthlySeriesAsync(UserId, 6, Now);

        report.Verify(r => r.GetDailySpendAsync(UserId, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 31), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task months_before_the_first_activity_are_skipped()
    {
        var source = Build([Point(8, 25, 2_000_000, 10_000_000), Point(9, 25, 2_000_000, 10_000_000), Point(10, 25, 2_000_000, 10_000_000)]);

        var series = await source.GetMonthlySeriesAsync(UserId, 6, Now);

        series.IncomeVnd.Should().Equal(10_000_000L, 10_000_000L, 10_000_000L);
        series.SpendVnd.Should().Equal(2_000_000L, 2_000_000L, 2_000_000L);
    }

    [Fact]
    public async Task a_zero_income_month_after_the_first_activity_counts_as_zero()
    {
        var source = Build([Point(8, 25, 1_000_000, 10_000_000), Point(9, 12, 3_000_000, 0), Point(10, 25, 1_000_000, 8_000_000)]);

        var series = await source.GetMonthlySeriesAsync(UserId, 6, Now);

        series.IncomeVnd.Should().Equal(10_000_000L, 0L, 8_000_000L);
    }

    [Fact]
    public async Task days_of_the_same_month_are_summed_in_vietnam_calendar_months()
    {
        var source = Build([Point(10, 1, 100, 1_000), Point(10, 31, 200, 2_000)]);

        var series = await source.GetMonthlySeriesAsync(UserId, 6, Now);

        series.IncomeVnd.Should().Equal(3_000L);
        series.SpendVnd.Should().Equal(300L);
    }

    [Fact]
    public async Task no_transactions_gives_an_empty_series()
    {
        var series = await Build([]).GetMonthlySeriesAsync(UserId, 6, Now);

        series.IncomeVnd.Should().BeEmpty();
        series.SpendVnd.Should().BeEmpty();
    }

    [Fact]
    public async Task balance_is_the_sum_of_account_balances()
    {
        var source = Build([], [new FinancialAccount { BalanceCents = 3_000_000 }, new FinancialAccount { BalanceCents = 2_500_000 }]);

        (await source.GetTotalBalanceAsync(UserId)).Should().Be(5_500_000);
    }

    [Theory]
    [InlineData("stable", IncomeSegment.Stable)]
    [InlineData("variable", IncomeSegment.Variable)]
    [InlineData(null, null)]
    [InlineData("garbage", null)]
    public async Task income_override_is_read_from_the_user(string? stored, IncomeSegment? expected)
        => (await Build([], overrideValue: stored).GetIncomeSegmentOverrideAsync(UserId)).Should().Be(expected);
}
