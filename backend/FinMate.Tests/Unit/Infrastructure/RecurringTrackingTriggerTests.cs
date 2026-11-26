using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Phase 92 (S3): xếp hàng tracker theo user chỉ khi có khoản cùng chiều, không xếp trùng, không bao giờ ném.</summary>
public class RecurringTrackingTriggerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IBackgroundJobClient> _jobs = new();
    private readonly Mock<IRecurringItemRepository> _items = new();
    private readonly Mock<ICacheService> _cache = new();

    private HangfireRecurringTrackingTrigger Trigger() => new(_jobs.Object, _items.Object, _cache.Object, NullLogger<HangfireRecurringTrackingTrigger>.Instance);

    private void HasActive(RecurringDirection direction)
        => _items.Setup(r => r.ListAsync(_userId, RecurringStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RecurringItem { Id = Guid.NewGuid(), UserId = _userId, Direction = direction, CounterpartyKey = "fe credit", Status = RecurringStatus.Active }]);

    private void VerifyEnqueued(Times times)
        => _jobs.Verify(j => j.Create(It.Is<Job>(job => job.Type == typeof(RecurringTrackingJob)), It.IsAny<IState>()), times);

    [Fact]
    public async Task ADebit_WithAnActiveOutflow_EnqueuesOnce_AndMarksTheUser()
    {
        HasActive(RecurringDirection.Outflow);

        await Trigger().TransactionConfirmedAsync(_userId, TransactionType.Debit);

        VerifyEnqueued(Times.Once());
        _cache.Verify(c => c.SetAsync<bool?>(CacheKeys.RecurringTrackingQueued(_userId), true, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ACredit_WithOnlyOutflows_DoesNothing()
    {
        HasActive(RecurringDirection.Outflow);

        await Trigger().TransactionConfirmedAsync(_userId, TransactionType.Credit);

        VerifyEnqueued(Times.Never());
    }

    [Fact]
    public async Task ATransfer_DoesNothing()
    {
        HasActive(RecurringDirection.Outflow);

        await Trigger().TransactionConfirmedAsync(_userId, TransactionType.Transfer);

        VerifyEnqueued(Times.Never());
    }

    [Fact]
    public async Task AlreadyQueued_IsNotQueuedAgain()
    {
        HasActive(RecurringDirection.Inflow);
        _cache.Setup(c => c.GetAsync<bool?>(CacheKeys.RecurringTrackingQueued(_userId), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Trigger().TransactionConfirmedAsync(_userId, TransactionType.Credit);

        VerifyEnqueued(Times.Never());
    }

    [Fact]
    public async Task Failures_NeverReachTheCaller()
    {
        _items.Setup(r => r.ListAsync(_userId, RecurringStatus.Active, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));

        await Trigger().Invoking(t => t.TransactionConfirmedAsync(_userId, TransactionType.Debit)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task TheJob_ClearsTheMarkerFirst_ThenTracksThatUserOnly()
    {
        var detection = new Mock<IRecurringDetectionService>();
        var order = new List<string>();
        _cache.Setup(c => c.RemoveAsync(CacheKeys.RecurringTrackingQueued(_userId), It.IsAny<CancellationToken>())).Callback(() => order.Add("clear")).Returns(Task.CompletedTask);
        detection.Setup(d => d.TrackForUserAsync(_userId, It.IsAny<CancellationToken>())).Callback(() => order.Add("track")).Returns(Task.CompletedTask);

        await new RecurringTrackingJob(detection.Object, _cache.Object).RunAsync(_userId);

        order.Should().Equal("clear", "track");
        detection.Verify(d => d.RunForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never(), "không đề xuất khoản mới ngoài job đêm");
    }
}
