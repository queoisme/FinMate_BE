using FinMate.Application.Budgets;
using FinMate.Application.Budgets.Commands;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Budgets;

/// <summary>
/// T1/T2 do người dùng chỉnh: cách tính ngưỡng hiệu lực từ tham số phương pháp, và việc evaluator, service cảnh báo tức thì và
/// bước mở lại cờ khi nâng hạn mức đều dùng ngưỡng của CHÍNH người đó thay vì 70/90 cố định.
/// </summary>
public class AlertThresholdsTests
{
    private const string Defs = """
        [{"key":"t1_pct","label":"T1","type":"percent","default":70,"min":1,"max":99,"userEditable":true},
         {"key":"t2_pct","label":"T2","type":"percent","default":90,"min":2,"max":99,"userEditable":true},
         {"key":"other","label":"X","type":"int","default":5,"min":1,"max":9,"userEditable":true}]
        """;

    private const string LockedDefs = """
        [{"key":"t1_pct","label":"T1","type":"percent","default":70,"min":1,"max":99,"userEditable":false},
         {"key":"t2_pct","label":"T2","type":"percent","default":90,"min":2,"max":99,"userEditable":false}]
        """;

    // ---- AlertThresholds.FromMethod ----

    [Fact]
    public void FromMethod_WithoutOverrides_UsesTheMethodDefaults()
        => AlertThresholds.FromMethod(Defs, "{}").Should().Be(new AlertThresholds(70, 90));

    [Fact]
    public void FromMethod_ValidOverrides_Win()
        => AlertThresholds.FromMethod(Defs, """{"t1_pct":55,"t2_pct":85}""").Should().Be(new AlertThresholds(55, 85));

    [Fact]
    public void FromMethod_OverrideOutsideMinMax_IsIgnoredForThatKeyOnly()
    {
        // t1 = 0 dưới min (1): bị bỏ, dùng mặc định 70. t2 = 85 hợp lệ nên giữ.
        AlertThresholds.FromMethod(Defs, """{"t1_pct":0,"t2_pct":85}""").Should().Be(new AlertThresholds(70, 85));

        // t2 = 100 vượt max (99): 100% là mức "vượt hạn mức" cố định, không được trùng với T2.
        AlertThresholds.FromMethod(Defs, """{"t2_pct":100}""").Should().Be(new AlertThresholds(70, 90));
    }

    [Fact]
    public void FromMethod_PairThatBreaksTheOrder_FallsBackToTheMethodDefaultsWholesale()
        => AlertThresholds.FromMethod(Defs, """{"t1_pct":95,"t2_pct":80}""").Should().Be(new AlertThresholds(70, 90));

    [Fact]
    public void FromMethod_ParameterThatIsNotUserEditable_IgnoresTheOverride()
        => AlertThresholds.FromMethod(LockedDefs, """{"t1_pct":10,"t2_pct":20}""").Should().Be(new AlertThresholds(70, 90));

    [Fact]
    public void FromMethod_MethodThatDefinesNoThresholds_ReturnsNull()
    {
        AlertThresholds.FromMethod("[]", "{}").Should().BeNull();
        AlertThresholds.FromMethod("""[{"key":"other","default":1,"min":1,"max":2,"userEditable":true}]""", "{}").Should().BeNull();
    }

    [Theory]
    [InlineData("not json", "{}")]
    [InlineData("{}", "{}")]                                          // param_defs phải là mảng
    [InlineData("""[{"key":"t1_pct"}]""", "{}")]                      // thiếu default/min/max
    [InlineData("""[{"key":"t1_pct","default":"x","min":1,"max":9,"userEditable":true}]""", "{}")]
    [InlineData(Defs, "not json")]
    [InlineData(Defs, "[]")]                                          // ghi đè phải là object: bỏ qua, dùng mặc định
    public void FromMethod_BrokenJson_NeverThrows(string defs, string overrides)
    {
        var act = () => AlertThresholds.FromMethod(defs, overrides);

        act.Should().NotThrow("đường cảnh báo chạy sau khi giao dịch đã lưu — dữ liệu lạ không được làm hỏng nó");
    }

    // ---- BudgetAlertEvaluator ----

    private static BudgetPeriod Period(long limit, long spent) => new() { Id = Guid.NewGuid(), LimitCents = limit, SpentCents = spent };

    [Fact]
    public void Evaluate_UsesThePersonalT1_NotASeventyPercentConstant()
    {
        var period = Period(limit: 1_000_000, spent: 550_000);

        BudgetAlertEvaluator.Evaluate(period, Guid.NewGuid(), "Ăn uống", AlertThresholds.Default).Should().BeNull("55% chưa tới T1 mặc định 70");

        var alert = BudgetAlertEvaluator.Evaluate(period, Guid.NewGuid(), "Ăn uống", new AlertThresholds(50, 80));
        alert.Should().NotBeNull();
        alert!.Level.Should().Be(BudgetAlertLevel.T1);
        alert.Percent.Should().Be(50);
        alert.Title.Should().Contain("50%");
        alert.Body.Should().Contain("50%").And.Contain("Ăn uống");
    }

    [Fact]
    public void Evaluate_JumpingStraightPastEveryLevel_ReportsOnlyOver()
    {
        var alert = BudgetAlertEvaluator.Evaluate(Period(1_000_000, 1_200_000), Guid.NewGuid(), "x", new AlertThresholds(40, 60));

        alert!.Level.Should().Be(BudgetAlertLevel.Over);
        alert.Percent.Should().Be(100);
    }

    [Fact]
    public void MarkSent_ClosesTheReportedLevelAndEveryLowerOne_ByLevelNotByPercent()
    {
        var period = Period(1_000_000, 700_000);
        var thresholds = new AlertThresholds(40, 70);   // T2 = 70 trùng số với T1 mặc định: đóng cờ phải theo mức, không theo phần trăm
        var alert = BudgetAlertEvaluator.Evaluate(period, Guid.NewGuid(), "x", thresholds)!;
        alert.Level.Should().Be(BudgetAlertLevel.T2);

        BudgetAlertEvaluator.MarkSent(period, alert, DateTimeOffset.UtcNow);

        period.AlertT1SentAt.Should().NotBeNull();
        period.AlertT2SentAt.Should().NotBeNull();
        period.AlertOverSentAt.Should().BeNull();
        BudgetAlertEvaluator.Evaluate(period, Guid.NewGuid(), "x", thresholds).Should().BeNull("mức đã gửi không được gửi lại");
    }

    [Fact]
    public void Evaluate_WithInvalidThresholds_FallsBackToTheDefaults()
    {
        var alert = BudgetAlertEvaluator.Evaluate(Period(1_000_000, 750_000), Guid.NewGuid(), "x", new AlertThresholds(90, 20));

        alert!.Percent.Should().Be(70);
    }

    [Fact]
    public void ReopenBelow_ClearsOnlyTheLevelsSpendingNoLongerReaches()
    {
        var now = DateTimeOffset.UtcNow;
        var period = new BudgetPeriod
        {
            LimitCents = 1_000_000,
            SpentCents = 600_000,
            AlertT1SentAt = now,
            AlertT2SentAt = now,
            AlertOverSentAt = now,
        };

        // 60% với (50, 80): còn trên T1 nên giữ cờ T1; đã dưới T2 và dưới 100% nên mở lại hai cờ kia.
        BudgetAlertEvaluator.ReopenBelow(period, new AlertThresholds(50, 80));

        period.AlertT1SentAt.Should().NotBeNull();
        period.AlertT2SentAt.Should().BeNull();
        period.AlertOverSentAt.Should().BeNull();
    }

    // ---- đường tức thì: BudgetPeriodService ----

    [Fact]
    public async Task BudgetPeriodService_AlertsAtThePersonalThreshold()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var budget = new Budget { Id = Guid.NewGuid(), UserId = userId, CategoryId = categoryId, LimitCents = 1_000_000 };
        var period = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = budget.Id, LimitCents = 1_000_000 };

        var budgets = new Mock<IBudgetRepository>();
        budgets.Setup(r => r.GetMatchingBudgetsAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        budgets.Setup(r => r.GetPeriodAsync(budget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(period);
        var notifier = new Mock<IBudgetAlertNotifier>();
        notifier.Setup(n => n.IsEnabledAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var thresholds = new Mock<IAlertThresholdProvider>();
        thresholds.Setup(t => t.GetAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new AlertThresholds(50, 80));

        var service = new BudgetPeriodService(budgets.Object, notifier.Object, thresholds.Object);

        var alerts = await service.ApplyDeltaAsync(userId, categoryId, 550_000, DateTimeOffset.UtcNow);

        alerts.Should().ContainSingle().Which.Level.Should().Be(BudgetAlertLevel.T1);
        thresholds.Verify(t => t.GetAsync(userId, It.IsAny<CancellationToken>()), Times.Once, "một lần cho cả lượt, không phải mỗi ngân sách một lần");
    }

    [Fact]
    public async Task BudgetPeriodService_DoesNotLookUpThresholds_WhenSpendingDoesNotIncrease()
    {
        var userId = Guid.NewGuid();
        var budget = new Budget { Id = Guid.NewGuid(), UserId = userId, LimitCents = 1_000_000 };
        var period = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = budget.Id, LimitCents = 1_000_000, SpentCents = 500_000 };

        var budgets = new Mock<IBudgetRepository>();
        budgets.Setup(r => r.GetMatchingBudgetsAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync([budget]);
        budgets.Setup(r => r.GetPeriodAsync(budget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(period);
        var thresholds = new Mock<IAlertThresholdProvider>();

        var service = new BudgetPeriodService(budgets.Object, new Mock<IBudgetAlertNotifier>().Object, thresholds.Object);
        await service.ApplyDeltaAsync(userId, null, -100_000, DateTimeOffset.UtcNow);

        thresholds.Verify(t => t.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never, "hoàn tiền không cần đánh giá cảnh báo");
    }

    // ---- nâng hạn mức: UpdateBudgetLimitCommandHandler ----

    [Fact]
    public async Task UpdateBudgetLimit_ReopensFlagsAgainstThePersonalThresholds()
    {
        var userId = Guid.NewGuid();
        var budget = new Budget { Id = Guid.NewGuid(), UserId = userId, LimitCents = 1_000_000 };
        var now = DateTimeOffset.UtcNow;
        var period = new BudgetPeriod
        {
            Id = Guid.NewGuid(),
            BudgetId = budget.Id,
            LimitCents = 1_000_000,
            SpentCents = 600_000,
            AlertT1SentAt = now,
            AlertT2SentAt = now,
            AlertOverSentAt = now,
        };
        var budgets = new Mock<IBudgetRepository>();
        budgets.Setup(r => r.GetByIdAsync(budget.Id, userId, It.IsAny<CancellationToken>())).ReturnsAsync(budget);
        budgets.Setup(r => r.GetPeriodAsync(budget.Id, VietnamTime.CurrentMonthStart(), It.IsAny<CancellationToken>())).ReturnsAsync(period);
        var thresholds = new Mock<IAlertThresholdProvider>();
        thresholds.Setup(t => t.GetAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new AlertThresholds(50, 80));

        var handler = new UpdateBudgetLimitCommandHandler(
            budgets.Object, new Mock<ICacheService>().Object, new UpdateBudgetLimitCommandValidator(), thresholds.Object);

        // Giữ nguyên hạn mức 1 triệu: 600k = 60% — trên T1 (50) nhưng dưới T2 (80) và dưới 100%.
        await handler.HandleAsync(new UpdateBudgetLimitCommand(userId, budget.Id, 1_000_000));

        period.AlertT1SentAt.Should().NotBeNull();
        period.AlertT2SentAt.Should().BeNull();
        period.AlertOverSentAt.Should().BeNull();
    }
}
