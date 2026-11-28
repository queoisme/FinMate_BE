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
        // Phase 92 (D5): dịch vụ đọc thêm khoản đã hoàn thành để loại trừ; test cũ không có khoản nào như vậy.
        _items.Setup(r => r.ListAsync(_userId, RecurringStatus.Completed, It.IsAny<CancellationToken>())).ReturnsAsync([]);
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

    // ---- Phase 97: thu không định kỳ vào đường số dư ----

    /// <summary>Chi mỗi ngày trong 200 ngày (đủ lịch sử, đủ tháng trọn vẹn) + khoản thu dự án 2.800.000đ mỗi 14 ngày.</summary>
    private static List<ForecastTransaction> FreelancerHistory()
    {
        var history = Enumerable.Range(1, 200).Select(i => Debit(Today.AddDays(-i), 50_000, "Quán ăn")).ToList();
        history.AddRange(Enumerable.Range(1, 200).Where(i => i % 14 == 0).Select(i => Credit(Today.AddDays(-i), 2_800_000, "Khách hàng A")));
        return history;
    }

    private void History(List<ForecastTransaction> history)
        => _data.Setup(r => r.GetTransactionsAsync(_userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, DateOnly from, DateOnly to, CancellationToken _) => history.Where(t => t.Date >= from && t.Date <= to).ToList());

    [Fact]
    public async Task Cashflow_ProjectsIrregularIncome_FromHistory()
    {
        _accounts.Setup(r => r.GetListByUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([MainWallet(10_000_000)]);
        History(FreelancerHistory());

        var result = await _service.GetCashflowAsync(_userId, null, Today.AddDays(14));

        result.IncomeBasis.Should().Be(SpendingBaseline.BasisHistory);
        result.Days.Sum(d => d.IrregularIncomeCents).Should().BeGreaterThan(0);
        result.Days[^1].ExpectedBalanceCents.Should().BeGreaterThan(10_000_000 - (15 * 50_000), "thu dự án bù lại chi");
    }

    [Fact]
    public async Task Cashflow_SalaryArrivingAfterThePaymentWindow_IsNeitherIrregularIncomeNorAnUnpaidItem()
    {
        // S3: hạn ngày Today−12, cửa sổ nhận thanh toán tới Today−5 (ân hạn 7 ngày); tiền về Today−3, đúng số tiền.
        var due = Today.AddDays(-12);
        var salary = SalaryItem(15_000_000, due);
        _items.Setup(r => r.ListAsync(_userId, RecurringStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync([salary]);
        _accounts.Setup(r => r.GetListByUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([MainWallet(10_000_000)]);
        var history = Enumerable.Range(1, 200).Select(i => Debit(Today.AddDays(-i), 50_000, "Quán ăn")).ToList();
        history.Add(Credit(Today.AddDays(-3), 15_000_000, "Lương công ty"));
        History(history);

        var result = await _service.GetCashflowAsync(_userId, null, Today.AddDays(14));

        result.Days.Should().OnlyContain(d => d.IrregularIncomeCents == 0, "khoản lương về trễ vẫn khớp khoản định kỳ nên không vào thu linh hoạt");
        result.Days.SelectMany(d => d.Events).Should().NotContain(e => e.Kind == CashflowEventKinds.RecurringInflow, "kỳ đã quá hạn không còn được chiếu");
    }

    [Fact]
    public async Task Cashflow_LateSalaryAtANewAmountBeyondTolerance_IsCountedAsIrregularIncome_ButTheOverduePeriodIsNotProjected()
    {
        // S3, ca còn lại: lương về trễ quá cửa sổ nhận VÀ đổi số tiền quá dung sai (20 triệu so với 15 triệu) không khớp khoản định kỳ theo cả hai luật
        // (dung sai; người trả + thời điểm), nên nó vào thu linh hoạt. Kỳ quá hạn vẫn không được chiếu, nên trong CÙNG một dự báo không có cả hai.
        // Lương đổi số tiền mà về trong cửa sổ thì đã được luật người trả + thời điểm chặn (test ngay dưới).
        var salary = SalaryItem(15_000_000, Today.AddDays(-12));
        _items.Setup(r => r.ListAsync(_userId, RecurringStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync([salary]);
        _accounts.Setup(r => r.GetListByUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([MainWallet(10_000_000)]);
        var history = Enumerable.Range(1, 200).Select(i => Debit(Today.AddDays(-i), 50_000, "Quán ăn")).ToList();
        history.Add(Credit(Today.AddDays(-3), 20_000_000, "Lương công ty"));
        History(history);

        var result = await _service.GetCashflowAsync(_userId, null, Today.AddDays(14));

        result.Days.Sum(d => d.IrregularIncomeCents).Should().BeGreaterThan(0);
        result.Days.SelectMany(d => d.Events).Should().NotContain(e => e.Kind == CashflowEventKinds.RecurringInflow);
    }

    [Fact]
    public async Task Cashflow_SalaryRaisedBeyondTolerance_ItsOldPriceCreditsInHistoryAreNotIrregularIncome()
    {
        // Luật người trả + thời điểm (K2): lương tăng từ 15 lên 19,5 triệu; các lần nhận giá cũ đúng kỳ trong lịch sử là lương, không phải thu linh hoạt.
        var nextDue = Today.AddDays(3);
        _items.Setup(r => r.ListAsync(_userId, RecurringStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync([SalaryItem(19_500_000, nextDue)]);
        _accounts.Setup(r => r.GetListByUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([MainWallet(10_000_000)]);
        var history = Enumerable.Range(1, 200).Select(i => Debit(Today.AddDays(-i), 50_000, "Quán ăn")).ToList();
        history.AddRange(Enumerable.Range(1, 6).Select(m => Credit(nextDue.AddMonths(-m), 15_000_000, "Lương công ty")));
        History(history);

        var result = await _service.GetCashflowAsync(_userId, null, Today.AddDays(14));

        result.Days.Should().OnlyContain(d => d.IrregularIncomeCents == 0);
        result.Days.SelectMany(d => d.Events).Should().ContainSingle(e => e.Kind == CashflowEventKinds.RecurringInflow).Which.AmountCents.Should().Be(19_500_000);
    }

    [Fact]
    public async Task Spendable_IsUnchanged_ItStillAddsOnlyTheConservativeMonthlyIncome()
    {
        // D12: spendable không đi qua CashflowEngine.Project, nên thu không định kỳ không bị cộng hai lần.
        _accounts.Setup(r => r.GetListByUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([MainWallet(10_000_000)]);
        var history = FreelancerHistory();
        History(history);
        var estimate = IncomeEstimator.Irregular(history.Where(t => t.Date <= Today).ToList(), [], Today, new ForecastSettings().IrregularIncomeMonths, Rules)!;

        var result = await _service.GetSpendableAsync(_userId);

        result.Incomes.Should().ContainSingle().Which.Label.Should().Be(SpendableCalculator.IrregularIncomeLabel);
        result.ExpectedIncomeCents.Should().Be(Math.Max(0, estimate.ConservativeMonthlyCents - estimate.ReceivedThisMonthCents));
    }

    private FinancialAccount MainWallet(long balance)
        => new() { Id = Account, UserId = _userId, AccountName = "Ví", BalanceCents = balance, IncludeInForecast = true };

    private RecurringItem SalaryItem(long amount, DateOnly due) => new()
    {
        Id = Guid.NewGuid(), UserId = _userId, Direction = RecurringDirection.Inflow, Name = "Lương công ty", CounterpartyKey = CounterpartyKey.From("Lương công ty"),
        FinancialAccountId = Account, AmountCents = amount, Cadence = RecurringCadence.Monthly, NextDueDate = due, AnchorDay = due.Day,
        Status = RecurringStatus.Active, Source = RecurringSource.Manual,
    };
}
