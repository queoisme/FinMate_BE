using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinanceMethods;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class ForecastServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IFinancialAccountRepository> _accounts = new();
    private readonly Mock<IRecurringItemRepository> _items = new();
    private readonly Mock<IForecastDataRepository> _data = new();
    private readonly Mock<IBudgetRepository> _budgets = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IGoalPlanningService> _goals = new();
    private readonly Mock<ISystemConfigProvider> _config = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly ForecastService _service;
    private readonly User _user;

    public ForecastServiceTests()
    {
        _user = new User { Id = _userId, MonthlyIncomeCents = 20_000_000 };
        _users.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(0, new SystemConfigDocument(), null));
        _items.Setup(r => r.ListAsync(_userId, RecurringStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _data.Setup(r => r.GetTransactionsAsync(_userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _budgets.Setup(r => r.GetListForUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _goals.Setup(g => g.GetSpendableAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpendableDto(null, null, 0, 0, null, Array.Empty<SpendableGoalDto>()));

        var now = new DateTimeOffset(Today.ToDateTime(new TimeOnly(9, 0)), VietnamTime.Offset);
        _service = new ForecastService(
            _accounts.Object, _items.Object, _data.Object, _budgets.Object, _users.Object, _goals.Object, _config.Object, _cache.Object, () => now);
    }

    private FinancialAccount Wallet(long balance, bool included = true)
        => new() { Id = Guid.NewGuid(), UserId = _userId, AccountName = "Ví", BalanceCents = balance, IncludeInForecast = included };

    [Fact]
    public async Task Cashflow_SumsOnlyAccountsIncludedInTheForecast()
    {
        _accounts.Setup(r => r.GetListByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Wallet(3_000_000), Wallet(50_000_000, included: false)]);

        var result = await _service.GetCashflowAsync(_userId, null, null);

        result.StartBalanceCents.Should().Be(3_000_000);
        result.To.Should().Be(new DateOnly(2026, 11, 30));
    }

    [Fact]
    public async Task Cashflow_SomeoneElsesAccount_IsNotFound()
    {
        var foreign = Guid.NewGuid();
        _accounts.Setup(r => r.GetByIdAsync(foreign, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((FinancialAccount?)null);

        var act = () => _service.GetCashflowAsync(_userId, foreign, null);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task Cashflow_HorizonOutsideTheAllowedRange_IsRejected(int offsetDays)
    {
        var act = () => _service.GetCashflowAsync(_userId, null, Today.AddDays(offsetDays));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(ForecastErrorCodes.HorizonInvalid);
    }

    [Fact]
    public async Task Scenario_OutsideThePeriod_IsRejected()
    {
        _accounts.Setup(r => r.GetListByUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([Wallet(1_000_000)]);
        var command = new CashflowScenarioCommand(_userId, null, Today.AddDays(5), [new ScenarioItem(Today.AddDays(9), 500_000, RecurringDirection.Outflow, "Giày")]);

        var act = () => _service.RunScenarioAsync(command);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(ForecastErrorCodes.ScenarioInvalid);
    }

    [Fact]
    public async Task Scenario_ShiftsTheBalance_WithoutTouchingStoredData()
    {
        _accounts.Setup(r => r.GetListByUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([Wallet(1_000_000)]);
        var command = new CashflowScenarioCommand(_userId, null, Today.AddDays(5), [new ScenarioItem(Today.AddDays(2), 500_000, RecurringDirection.Outflow, "Giày")]);

        var result = await _service.RunScenarioAsync(command);

        result.EndBalanceCents.Should().Be(500_000);
        _items.Verify(r => r.UpdateAsync(It.IsAny<RecurringItem>(), It.IsAny<CancellationToken>()), Times.Never);
        _accounts.Verify(r => r.UpdateAsync(It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Spendable_DefaultReserveIsFivePercentOfIncome()
    {
        _accounts.Setup(r => r.GetListByUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([Wallet(10_000_000)]);

        var result = await _service.GetSpendableAsync(_userId);

        result.ReserveCents.Should().Be(1_000_000);
        result.SpendableCents.Should().Be(9_000_000);
    }

    [Fact]
    public async Task Spendable_UserReserveOverridesTheDefault()
    {
        _user.ForecastReserveCents = 0;
        _accounts.Setup(r => r.GetListByUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([Wallet(10_000_000)]);

        (await _service.GetSpendableAsync(_userId)).ReserveCents.Should().Be(0);
    }

    [Fact]
    public void MonthlyBudgetTotal_PrefersTheOverallBudget_SoCategoriesAreNotCountedTwice()
    {
        var budgets = new List<Budget>
        {
            new() { CategoryId = null, LimitCents = 10_000_000 },
            new() { CategoryId = Guid.NewGuid(), LimitCents = 3_000_000 },
            new() { CategoryId = Guid.NewGuid(), LimitCents = 2_000_000 },
        };

        ForecastService.MonthlyBudgetTotal(budgets).Should().Be(10_000_000);
        ForecastService.MonthlyBudgetTotal(budgets.Skip(1).ToList()).Should().Be(5_000_000);
    }
}
