using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Job thử lại thông báo theo đúng luật trùng của Phase 82: cờ AI không bỏ thông báo; cùng sự kiện → <c>Duplicate</c>; ứng viên → nháp <c>Suspected</c>.</summary>
public class RetryFailedNotificationDuplicateTests
{
    private readonly Mock<INotificationLogRepository> _logs = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IAIServiceClient> _ai = new();
    private readonly Mock<IDuplicateDetector> _duplicates = new();
    private readonly NotificationLog _log = new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        FinancialAccountId = Guid.NewGuid(),
        PackageName = "com.VCB",
        Status = NotificationLogStatus.Failed,
        ReceivedAt = DateTimeOffset.UtcNow,
    };
    private readonly List<Transaction> _saved = [];

    public RetryFailedNotificationDuplicateTests()
    {
        _logs.Setup(r => r.GetFailedForRetryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([_log]);
        _transactions.Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .Callback<Transaction, CancellationToken>((t, _) => _saved.Add(t))
            .Returns(Task.CompletedTask);
        _ai.Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(35_000, "debit", null, null, DateTimeOffset.UtcNow, null, 0.9),
                new CategorizationResult("food", 0.9),
                new DuplicateResult(true, Guid.NewGuid()),
                new ModelVersions("1", "1", "1"),
                10));
    }

    private RetryFailedNotificationJob Job() => new(_logs.Object, _categories.Object, _transactions.Object, _ai.Object, _duplicates.Object);

    private void DetectorReturns(DuplicateMatch match)
        => _duplicates.Setup(d => d.MarkSuspectedAsync(It.IsAny<Transaction>(), "com.VCB", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Transaction, string?, Guid?, CancellationToken>((t, _, _, _) =>
            {
                if (match.SuspectedOf is { } top)
                {
                    t.DuplicateOfTransactionId = top.Id;
                    t.DuplicateStatus = DuplicateStatus.Suspected;
                }
            })
            .ReturnsAsync(match);

    [Fact]
    public async Task AiFlagAlone_StillCreatesTheDraft()
    {
        DetectorReturns(DuplicateMatch.None);

        await Job().RunAsync();

        _saved.Should().ContainSingle().Which.DuplicateStatus.Should().BeNull();
        _log.Status.Should().Be(NotificationLogStatus.Processed);
    }

    [Fact]
    public async Task SameBankEvent_IsLoggedDuplicateWithoutADraft()
    {
        DetectorReturns(new DuplicateMatch(Guid.NewGuid(), []));

        await Job().RunAsync();

        _saved.Should().BeEmpty();
        _log.Status.Should().Be(NotificationLogStatus.Duplicate);
    }

    [Fact]
    public async Task Candidate_MakesASuspectedDraft()
    {
        var other = Guid.NewGuid();
        DetectorReturns(new DuplicateMatch(null, [new RankedDuplicate(other, DuplicatePair.NotificationNotification)]));

        await Job().RunAsync();

        var draft = _saved.Should().ContainSingle().Subject;
        draft.DuplicateOfTransactionId.Should().Be(other);
        draft.DuplicateStatus.Should().Be(DuplicateStatus.Suspected);
    }
}
