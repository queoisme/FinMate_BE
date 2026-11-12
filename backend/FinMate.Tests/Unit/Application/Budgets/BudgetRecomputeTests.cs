using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Budgets;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Groups;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Budgets;

/// <summary>
/// Phase 81, D2: thao tác có dính hoàn/đảo TÍNH LẠI kỳ ngân sách thay vì cộng delta. Cộng delta rồi chặn ở 0 làm số lệch: hoàn ở tháng
/// chưa chi bị chặn mất, rồi xoá khoản hoàn đó lại cộng vào một khoản chi không có thật. BR-RF-05: hoàn không bao giờ sinh cảnh báo hay
/// reset cờ. R4: phía nhóm đi cùng một cách.
/// </summary>
public class BudgetRecomputeTests
{
    private static readonly DateTimeOffset Nov13 = new(2026, 11, 13, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Oct20 = new(2026, 10, 20, 5, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Category _shopping = new() { Id = Guid.NewGuid(), Name = "Mua sắm", Slug = "shopping", IsSystem = true };
    private readonly Mock<IBudgetRepository> _budgets = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IBudgetAlertNotifier> _notifier = new();
    private readonly Mock<IAlertThresholdProvider> _thresholds = new();

    public BudgetRecomputeTests()
    {
        _notifier.Setup(n => n.IsEnabledAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _thresholds.Setup(t => t.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(AlertThresholds.Default);
    }

    private BudgetPeriodService Service() => new(_budgets.Object, _notifier.Object, _thresholds.Object, _transactions.Object);

    private BudgetPeriod ArrangeBudget(long limit, long spent, DateTimeOffset? t2SentAt = null)
    {
        var budget = new Budget { Id = Guid.NewGuid(), UserId = _userId, CategoryId = _shopping.Id, Category = _shopping, LimitCents = limit };
        var period = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = budget.Id, LimitCents = limit, SpentCents = spent, AlertT2SentAt = t2SentAt };
        _budgets.Setup(r => r.GetMatchingBudgetsAsync(_userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        _budgets.Setup(r => r.GetPeriodAsync(budget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(period);
        return period;
    }

    private void DbNetIs(long net)
        => _transactions
            .Setup(r => r.SumNetSpendAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Transaction, bool>>>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(net);

    private Transaction Refund(long amount, DateTimeOffset at, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = _userId,
        CategoryId = _shopping.Id,
        Category = _shopping,
        AmountCents = amount,
        TransactionType = TransactionType.Credit,
        Relation = TransactionRelation.Refund,
        Status = TransactionStatus.Confirmed,
        TransactedAt = at,
        DeletedAt = deleted ? Nov13 : null,
    };

    [Fact]
    public async Task FullRefund_BringsSpentToZero_KeepsFlags_AndSendsNothing()
    {
        var sent = Nov13.AddDays(-1);
        var period = ArrangeBudget(limit: 1_000_000, spent: 900_000, t2SentAt: sent);
        DbNetIs(900_000);   // giày, chưa tính khoản hoàn đang xử lý
        var refund = Refund(900_000, Nov13);

        var alerts = await Service().RecomputeAsync(_userId, [refund], [(_shopping.Id, Nov13)]);

        period.SpentCents.Should().Be(0);
        period.AlertT2SentAt.Should().Be(sent, "BR-RF-05: không reset cờ đã gửi");
        alerts.Should().BeEmpty();
        _notifier.Verify(n => n.IsEnabledAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PartialRefund_LeavesTheNet()
    {
        var period = ArrangeBudget(limit: 1_000_000, spent: 900_000, t2SentAt: Nov13);
        DbNetIs(900_000);

        await Service().RecomputeAsync(_userId, [Refund(300_000, Nov13)], [(_shopping.Id, Nov13)]);

        period.SpentCents.Should().Be(600_000);
        period.AlertT2SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CrossMonthRefundThenDelete_DoesNotDrift()
    {
        // Mua tháng 10, hoàn tháng 11 khi tháng 11 chưa chi gì: số ròng của tháng 11 là −900k, kỳ giữ 0.
        var november = ArrangeBudget(limit: 1_000_000, spent: 0);
        DbNetIs(0);
        var refund = Refund(900_000, Nov13);
        await Service().RecomputeAsync(_userId, [refund], [(_shopping.Id, Nov13)]);
        november.SpentCents.Should().Be(0);

        // Xoá khoản hoàn: DB (đã bỏ chính nó) vẫn 0, khoản đã xoá không đóng góp gì → vẫn 0, KHÔNG thành 900k như đường delta cũ.
        var deleted = Refund(900_000, Nov13, deleted: true);
        deleted.Id = refund.Id;
        await Service().RecomputeAsync(_userId, [deleted], [(_shopping.Id, Nov13)]);
        november.SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task Reversal_RevertsTheOriginalInItsOwnPeriod()
    {
        // D1: đảo hoàn tác khoản gốc ở kỳ CỦA KHOẢN GỐC (tháng 10), không phải kỳ của lệnh đảo.
        var october = ArrangeBudget(limit: 1_000_000, spent: 900_000);
        DbNetIs(0);   // ngoài khoản gốc ra tháng 10 không có gì
        var original = new Transaction
        {
            Id = Guid.NewGuid(), UserId = _userId, CategoryId = _shopping.Id, Category = _shopping, AmountCents = 900_000,
            TransactionType = TransactionType.Debit, Status = TransactionStatus.Confirmed, TransactedAt = Oct20, IsReversed = true,
        };

        await Service().RecomputeAsync(_userId, [original], [(_shopping.Id, Oct20)]);

        october.SpentCents.Should().Be(0);
        _budgets.Verify(r => r.GetPeriodAsync(It.IsAny<Guid>(), new DateTimeOffset(2026, 9, 30, 17, 0, 0, TimeSpan.Zero), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task DeletingARefund_ThatPushesSpendBackUp_EvaluatesAlertsOnlyThen()
    {
        var period = ArrangeBudget(limit: 1_000_000, spent: 0);
        DbNetIs(900_000);   // giày còn đó; khoản hoàn đang bị xoá
        var deleted = Refund(900_000, Nov13, deleted: true);

        var alerts = await Service().RecomputeAsync(_userId, [deleted], [(_shopping.Id, Nov13)]);

        period.SpentCents.Should().Be(900_000);
        alerts.Should().ContainSingle("số tăng lại thật sự vượt 90%, và cờ T2 của kỳ chưa đóng");
    }

    [Fact]
    public async Task TouchingTheSameBudgetTwice_ComputesItOnce()
    {
        ArrangeBudget(limit: 1_000_000, spent: 900_000);
        DbNetIs(900_000);

        await Service().RecomputeAsync(_userId, [Refund(300_000, Nov13)], [(_shopping.Id, Nov13), (_shopping.Id, Nov13.AddDays(1))]);

        _transactions.Verify(
            r => r.SumNetSpendAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Transaction, bool>>>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- R4: phía nhóm ----

    private sealed class GroupHarness
    {
        public Mock<IGroupFinanceRepository> Finance { get; } = new();
        public Mock<IBudgetRepository> Budgets { get; } = new();
        public Mock<IGroupAlertNotifier> Alerts { get; } = new();
        public Mock<ISystemConfigProvider> Config { get; } = new();

        public GroupLedger Ledger(bool groupsOn = true)
        {
            Config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SystemConfigSnapshot(0, new SystemConfigDocument { Features = new FeatureSettings { Groups = groupsOn } }, null));
            return new GroupLedger(Finance.Object, Mock.Of<IGroupRepository>(), Budgets.Object, Mock.Of<IGroupAccessService>(), Alerts.Object, Config.Object);
        }
    }

    [Fact]
    public async Task ReversingASharedPurchase_RecomputesTheGroupPeriodOfTheOriginal()
    {
        var h = new GroupHarness();
        var groupId = Guid.NewGuid();
        var budget = new Budget { Id = Guid.NewGuid(), GroupId = groupId, CategoryId = _shopping.Id, LimitCents = 2_000_000 };
        var period = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = budget.Id, LimitCents = 2_000_000, SpentCents = 1_400_000 };
        h.Finance.Setup(f => f.GetMatchingBudgetsAsync(groupId, _shopping.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        h.Finance.Setup(f => f.SumSharedNetSpendAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Transaction, bool>>>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(500_000);   // các khoản khác của nhóm trong tháng 10
        h.Budgets.Setup(b => b.GetPeriodAsync(budget.Id, new DateTimeOffset(2026, 9, 30, 17, 0, 0, TimeSpan.Zero), It.IsAny<CancellationToken>())).ReturnsAsync(period);

        var original = new Transaction
        {
            Id = Guid.NewGuid(), UserId = _userId, GroupId = groupId, CategoryId = _shopping.Id, Category = _shopping, AmountCents = 900_000,
            TransactionType = TransactionType.Debit, Status = TransactionStatus.Confirmed, TransactedAt = Oct20, IsReversed = true,
        };

        var alerts = await h.Ledger().RecomputeAsync([original], [GroupScopeTouch.Of(original, _shopping)!]);

        period.SpentCents.Should().Be(500_000);
        alerts.Should().BeEmpty();
        h.Alerts.Verify(a => a.EvaluateAndMarkAsync(It.IsAny<Budget>(), It.IsAny<BudgetPeriod>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyingASharedRefund_NeverEvaluatesGroupAlerts()
    {
        var h = new GroupHarness();
        var groupId = Guid.NewGuid();
        var budget = new Budget { Id = Guid.NewGuid(), GroupId = groupId, LimitCents = 1_000_000 };
        var period = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = budget.Id, LimitCents = 1_000_000, SpentCents = 950_000 };
        h.Finance.Setup(f => f.GetMatchingBudgetsAsync(groupId, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        h.Budgets.Setup(b => b.GetPeriodAsync(budget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(period);
        var refund = Refund(300_000, Nov13);
        refund.GroupId = groupId;

        var alerts = await h.Ledger().ApplyAsync(refund, _shopping, +1);

        period.SpentCents.Should().Be(650_000);
        alerts.Should().BeEmpty();
        h.Alerts.Verify(a => a.EvaluateAndMarkAsync(It.IsAny<Budget>(), It.IsAny<BudgetPeriod>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
