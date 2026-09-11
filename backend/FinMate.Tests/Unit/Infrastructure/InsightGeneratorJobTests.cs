using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class InsightGeneratorJobTests
{
    private static readonly DateOnly Today = new(2026, 9, 20);
    private static readonly DateOnly MonthFirst = new(2026, 9, 1);

    private readonly Mock<IReportRepository> _reportRepository = new();
    private readonly InsightGeneratorJob _job;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly List<SpendingInsight> _written = new();

    public InsightGeneratorJobTests()
    {
        _job = new InsightGeneratorJob(_reportRepository.Object);

        _reportRepository.Setup(r => r.GetUserIdsWithRecentActivityAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { _userId });
        _reportRepository.Setup(r => r.GetDailySpendAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailySpendPoint>());
        _reportRepository.Setup(r => r.GetConfirmedDebitsAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Transaction>());
        _reportRepository.Setup(r => r.InsightExistsAsync(
                It.IsAny<Guid>(), It.IsAny<InsightType>(), It.IsAny<Guid?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _reportRepository.Setup(r => r.AddInsightAsync(It.IsAny<SpendingInsight>(), It.IsAny<CancellationToken>()))
            .Callback<SpendingInsight, CancellationToken>((i, _) => _written.Add(i))
            .Returns(Task.CompletedTask);
    }

    private void SetupMonthSpend(DateOnly from, DateOnly to, long spent)
        => _reportRepository.Setup(r => r.GetDailySpendAsync(_userId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailySpendPoint> { new(from, spent, 0, 1) });

    private void SetupDebits(params Transaction[] transactions)
        => _reportRepository.Setup(r => r.GetConfirmedDebitsAsync(
                _userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.ToList());

    private static Transaction Debit(DateOnly date, long amount, string? merchant = null, Guid? categoryId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            AmountCents = amount,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Confirmed,
            MerchantName = merchant,
            CategoryId = categoryId,
            TransactedAt = VietnamTime.DayRange(date).Start.AddHours(5),
        };

    [Fact]
    public async Task VsLastMonth_SpendingUpSharply_EmitsInsight()
    {
        SetupMonthSpend(MonthFirst, Today, 2_000_000);
        SetupMonthSpend(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), 1_000_000);

        await _job.GenerateAsync(Today);

        var insight = _written.Single(i => i.InsightType == InsightType.VsLastMonth);
        insight.Title.Should().Contain("nhiều hơn");
        insight.AmountCents.Should().Be(1_000_000);
    }

    [Fact]
    public async Task VsLastMonth_ChangeUnderThreshold_StaysQuiet()
    {
        // +10% là dao động bình thường, không đáng làm phiền.
        SetupMonthSpend(MonthFirst, Today, 1_100_000);
        SetupMonthSpend(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), 1_000_000);

        await _job.GenerateAsync(Today);

        _written.Should().NotContain(i => i.InsightType == InsightType.VsLastMonth);
    }

    [Fact]
    public async Task VsLastMonth_NoPreviousSpend_StaysQuietRatherThanClaimingInfinity()
    {
        SetupMonthSpend(MonthFirst, Today, 2_000_000);

        await _job.GenerateAsync(Today);

        _written.Should().NotContain(i => i.InsightType == InsightType.VsLastMonth);
    }

    [Fact]
    public async Task VsLastMonth_AlreadyEmittedThisPeriod_DoesNotEmitAgain()
    {
        SetupMonthSpend(MonthFirst, Today, 2_000_000);
        SetupMonthSpend(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), 1_000_000);
        _reportRepository.Setup(r => r.InsightExistsAsync(
                _userId, InsightType.VsLastMonth, null, MonthFirst, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _job.GenerateAsync(Today);

        _written.Should().NotContain(i => i.InsightType == InsightType.VsLastMonth);
    }

    [Fact]
    public async Task Recurring_MonthlyChargesOfTheSameAmount_AreFlagged()
    {
        SetupDebits(
            Debit(Today.AddDays(-60), 199_000, "Netflix"),
            Debit(Today.AddDays(-30), 199_000, "Netflix"),
            Debit(Today, 199_000, "Netflix"));

        await _job.GenerateAsync(Today);

        var insight = _written.Single(i => i.InsightType == InsightType.RecurringDetected);
        insight.Body.Should().Contain("Netflix");
        insight.AmountCents.Should().Be(199_000);
    }

    [Fact]
    public async Task Recurring_SameMerchantButErraticAmounts_IsNotASubscription()
    {
        SetupDebits(
            Debit(Today.AddDays(-60), 50_000, "Circle K"),
            Debit(Today.AddDays(-30), 320_000, "Circle K"),
            Debit(Today, 90_000, "Circle K"));

        await _job.GenerateAsync(Today);

        _written.Should().NotContain(i => i.InsightType == InsightType.RecurringDetected);
    }

    [Fact]
    public async Task Recurring_SameAmountButBunchedTogether_IsNotMonthlyCadence()
    {
        SetupDebits(
            Debit(Today.AddDays(-4), 199_000, "Highlands"),
            Debit(Today.AddDays(-2), 199_000, "Highlands"),
            Debit(Today, 199_000, "Highlands"));

        await _job.GenerateAsync(Today);

        _written.Should().NotContain(i => i.InsightType == InsightType.RecurringDetected);
    }

    [Fact]
    public async Task Recurring_OnlyTwoOccurrences_IsTooLittleToCallIt()
    {
        SetupDebits(
            Debit(Today.AddDays(-30), 199_000, "Spotify"),
            Debit(Today, 199_000, "Spotify"));

        await _job.GenerateAsync(Today);

        _written.Should().NotContain(i => i.InsightType == InsightType.RecurringDetected);
    }

    [Fact]
    public async Task UnusualSpending_OneChargeFarAboveTheCategoryNorm_IsFlagged()
    {
        var categoryId = Guid.NewGuid();
        var normal = Enumerable.Range(1, 8)
            .Select(i => Debit(Today.AddDays(-i), 50_000, $"Quán {i}", categoryId));
        SetupDebits(normal.Append(Debit(Today, 5_000_000, "Nhà hàng sang", categoryId)).ToArray());

        await _job.GenerateAsync(Today);

        var insight = _written.Single(i => i.InsightType == InsightType.UnusualSpending);
        insight.CategoryId.Should().Be(categoryId);
        insight.AmountCents.Should().Be(5_000_000);
    }

    [Fact]
    public async Task UnusualSpending_SteadySpending_HasNoOutlierToReport()
    {
        var categoryId = Guid.NewGuid();
        SetupDebits(Enumerable.Range(1, 8)
            .Select(i => Debit(Today.AddDays(-i), 50_000, $"Quán {i}", categoryId))
            .ToArray());

        await _job.GenerateAsync(Today);

        _written.Should().NotContain(i => i.InsightType == InsightType.UnusualSpending);
    }

    [Fact]
    public async Task UnusualSpending_TooFewSamples_DoesNotGuess()
    {
        var categoryId = Guid.NewGuid();
        SetupDebits(
            Debit(Today.AddDays(-2), 20_000, "A", categoryId),
            Debit(Today, 9_000_000, "B", categoryId));

        await _job.GenerateAsync(Today);

        _written.Should().NotContain(i => i.InsightType == InsightType.UnusualSpending);
    }
}
