using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.ListenerHealth;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Job 09:00 (BR-LH-05, S2, D20): đi hết mọi trang user, một user lỗi không chặn người khác, gộp upload gap theo phiên bản, dọn tới khi hết.</summary>
public class ListenerHealthDailyJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 2, 0, 0, TimeSpan.Zero);
    private readonly Mock<IListenerHealthRepository> _repository = new();
    private readonly Mock<IListenerHealthReminderService> _reminders = new();
    private readonly Mock<ISystemConfigProvider> _config = new();
    private IReadOnlyDictionary<string, int>? _savedGaps;

    public ListenerHealthDailyJobTests()
    {
        _config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(1, new SystemConfigDocument(), null));
        _repository.Setup(r => r.ReplaceUploadGapCountsAsync(It.IsAny<DateOnly>(), It.IsAny<IReadOnlyDictionary<string, int>>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly, IReadOnlyDictionary<string, int>, CancellationToken>((_, gaps, _) => _savedGaps = gaps)
            .Returns(Task.CompletedTask);
    }

    private ListenerHealthDailyJob Job() => new(_repository.Object, _reminders.Object, _config.Object, NullLogger<ListenerHealthDailyJob>.Instance);

    [Fact]
    public async Task WalksEveryPage_AndOneFailingUserDoesNotStopTheOthers()
    {
        var first = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToList();
        var second = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var lastOfFirst = first[^1];
        _repository.Setup(r => r.ListWatchedUserIdsAsync(null, 200, It.IsAny<CancellationToken>())).ReturnsAsync(first);
        _repository.Setup(r => r.ListWatchedUserIdsAsync(lastOfFirst, 200, It.IsAny<CancellationToken>())).ReturnsAsync(second);
        _reminders.Setup(r => r.RunForUserAsync(It.IsAny<Guid>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(new ListenerHealthRunResult(0, []));
        var failing = first[3];
        _reminders.Setup(r => r.RunForUserAsync(failing, Now, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        await Job().RunAtAsync(Now);

        _reminders.Verify(r => r.RunForUserAsync(It.IsAny<Guid>(), Now, It.IsAny<CancellationToken>()), Times.Exactly(202));
    }

    [Fact]
    public async Task UploadGaps_AreCountedByAppVersion_WithUnknownForMissingVersions()
    {
        var users = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _repository.Setup(r => r.ListWatchedUserIdsAsync(null, 200, It.IsAny<CancellationToken>())).ReturnsAsync(users);
        _reminders.Setup(r => r.RunForUserAsync(users[0], Now, It.IsAny<CancellationToken>())).ReturnsAsync(new ListenerHealthRunResult(1, ["2.4.0", null]));
        _reminders.Setup(r => r.RunForUserAsync(users[1], Now, It.IsAny<CancellationToken>())).ReturnsAsync(new ListenerHealthRunResult(0, ["2.4.0"]));

        await Job().RunAtAsync(Now);

        _savedGaps.Should().BeEquivalentTo(new Dictionary<string, int> { ["2.4.0"] = 2, ["unknown"] = 1 });
    }

    [Fact]
    public async Task Purge_RepeatsUntilNothingIsLeft_WithTheRetentionCutoff()
    {
        _repository.Setup(r => r.ListWatchedUserIdsAsync(null, 200, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _repository.SetupSequence(r => r.PurgeDailyRowsBeforeAsync(new DateOnly(2026, 8, 16), 5_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5_000).ReturnsAsync(12).ReturnsAsync(0);

        await Job().RunAtAsync(Now);

        _repository.Verify(r => r.PurgeDailyRowsBeforeAsync(It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task AFailingPurge_DoesNotThrow()
    {
        _repository.Setup(r => r.ListWatchedUserIdsAsync(null, 200, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _repository.Setup(r => r.PurgeDailyRowsBeforeAsync(It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

        await FluentActions.Awaiting(() => Job().RunAtAsync(Now)).Should().NotThrowAsync();
    }
}
