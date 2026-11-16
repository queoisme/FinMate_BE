using FinMate.Application.Common.Interfaces;
using FinMate.Application.Notifications;
using FinMate.Application.Notifications.Commands;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Notifications;

/// <summary>
/// Phase 82 ở luồng thông báo: cờ trùng của AI không còn làm mất thông báo (BR-DUP-02); cùng sự kiện ngân hàng → log <c>Duplicate</c>,
/// không nháp, không push (BR-DUP-05); có ứng viên → nháp <c>Suspected</c> và push <c>review_duplicate</c> (BR-DUP-06, D15).
/// </summary>
public class AnalyzeNotificationDuplicateTests
{
    private readonly Mock<IFinancialAccountRepository> _accounts = new();
    private readonly Mock<INotificationLogRepository> _logs = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IAIServiceClient> _ai = new();
    private readonly Mock<IPushNotificationService> _push = new();
    private readonly Mock<IDuplicateDetector> _duplicates = new();
    private readonly FinancialAccount _account = new() { Id = Guid.NewGuid(), IsMonitored = true };
    private readonly List<Transaction> _saved = [];
    private readonly List<NotificationLogStatus> _logStatuses = [];
    private IReadOnlyDictionary<string, string>? _pushData;

    public AnalyzeNotificationDuplicateTests()
    {
        _accounts.Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_account);
        _logs.Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);
        _logs.Setup(r => r.UpdateAsync(It.IsAny<NotificationLog>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationLog, CancellationToken>((l, _) => _logStatuses.Add(l.Status))
            .Returns(Task.CompletedTask);
        _transactions.Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .Callback<Transaction, CancellationToken>((t, _) => _saved.Add(t))
            .Returns(Task.CompletedTask);
        _push.Setup(p => p.NotifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, IReadOnlyDictionary<string, string>?, CancellationToken>((_, _, _, d, _) => _pushData = d)
            .Returns(Task.CompletedTask);
    }

    private AnalyzeNotificationCommandHandler Handler() => new(
        _accounts.Object, _logs.Object, _categories.Object, _transactions.Object, _ai.Object, _push.Object,
        new AnalyzeNotificationCommandValidator(), duplicates: _duplicates.Object);

    private static AnalyzeNotificationCommand Command()
        => new(Guid.NewGuid(), "com.VCB", "VCB", "TK 123: -50,000VND. SD: 1,000,000VND", DateTimeOffset.UtcNow);

    private void AiSays(bool aiDuplicate, Guid? aiDuplicateOf = null)
        => _ai.Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(50_000, "debit", "Highlands", "Thanh toan", DateTimeOffset.UtcNow, 1_000_000, 0.95),
                new CategorizationResult("food", 0.95),
                new DuplicateResult(aiDuplicate, aiDuplicateOf),
                new ModelVersions("1", "1", "1"),
                20));

    private void DetectorReturns(DuplicateMatch match, bool marks)
        => _duplicates.Setup(d => d.MarkSuspectedAsync(It.IsAny<Transaction>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Transaction, string?, Guid?, CancellationToken>((t, _, _, _) =>
            {
                if (marks && match.SuspectedOf is { } top)
                {
                    t.DuplicateOfTransactionId = top.Id;
                    t.DuplicateStatus = DuplicateStatus.Suspected;
                }
            })
            .ReturnsAsync(match);

    [Fact]
    public async Task AiFlagAlone_NoLongerDropsTheNotification()
    {
        AiSays(aiDuplicate: true, aiDuplicateOf: Guid.NewGuid());
        DetectorReturns(DuplicateMatch.None, marks: false);

        var result = await Handler().HandleAsync(Command());

        result.DraftTransactionId.Should().NotBeNull();
        _saved.Should().ContainSingle().Which.DuplicateStatus.Should().BeNull();
        _pushData!["action"].Should().NotBe(TransactionReviewPushBuilder.ActionReviewDuplicate);
        result.Status.Should().Be("Processed");
    }

    [Fact]
    public async Task AiDuplicateRequestId_IsPassedToTheDetectorAsAHint()
    {
        var aiLog = Guid.NewGuid();
        AiSays(aiDuplicate: true, aiDuplicateOf: aiLog);
        DetectorReturns(DuplicateMatch.None, marks: false);

        await Handler().HandleAsync(Command());

        _duplicates.Verify(d => d.MarkSuspectedAsync(It.IsAny<Transaction>(), "com.VCB", aiLog, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SameBankEvent_IsLoggedDuplicate_WithoutDraftOrPush()
    {
        var existing = Guid.NewGuid();
        AiSays(aiDuplicate: false);
        DetectorReturns(new DuplicateMatch(existing, []), marks: false);

        var result = await Handler().HandleAsync(Command());

        result.Status.Should().Be("Duplicate");
        result.DraftTransactionId.Should().BeNull();
        result.DuplicateOfTransactionId.Should().Be(existing);
        _saved.Should().BeEmpty();
        _logStatuses.Last().Should().Be(NotificationLogStatus.Duplicate);
        _push.Verify(p => p.NotifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Candidate_MakesASuspectedDraftAndAsksToReview()
    {
        var chat = Guid.NewGuid();
        AiSays(aiDuplicate: false);
        DetectorReturns(new DuplicateMatch(null, [new RankedDuplicate(chat, DuplicatePair.ManualNotification), new RankedDuplicate(Guid.NewGuid(), DuplicatePair.ManualNotification)]), marks: true);

        var result = await Handler().HandleAsync(Command());

        var draft = _saved.Should().ContainSingle().Subject;
        draft.Status.Should().Be(TransactionStatus.Draft);
        draft.DuplicateOfTransactionId.Should().Be(chat);
        draft.DuplicateStatus.Should().Be(DuplicateStatus.Suspected);
        result.DraftTransactionId.Should().Be(draft.Id);
        result.DuplicateOfTransactionId.Should().Be(chat);
        _pushData!["action"].Should().Be(TransactionReviewPushBuilder.ActionReviewDuplicate);
        _pushData["suggestionCount"].Should().Be("2");
        _logStatuses.Last().Should().Be(NotificationLogStatus.Processed);
    }

    // ---- Phase 83 ----

    [Fact]
    public async Task ATransferSuggestion_IsPushedAsReviewTransfer()
    {
        AiSays(aiDuplicate: false);
        DetectorReturns(DuplicateMatch.None, marks: false);
        var transfers = new Mock<FinMate.Application.Transactions.Transfers.ITransferSuggestions>();
        transfers.Setup(t => t.ForOneAsync(It.IsAny<Guid>(), It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinMate.Application.Transactions.Transfers.TransferSuggestion(
                TransferKind.AtmWithdrawal, TransferKind.AtmWithdrawal, null, _account.Id, null, 50_000, null, [Guid.NewGuid()]));
        var handler = new AnalyzeNotificationCommandHandler(
            _accounts.Object, _logs.Object, _categories.Object, _transactions.Object, _ai.Object, _push.Object,
            new AnalyzeNotificationCommandValidator(), duplicates: _duplicates.Object, transfers: transfers.Object);

        await handler.HandleAsync(Command());

        _pushData!["action"].Should().Be(TransactionReviewPushBuilder.ActionReviewTransfer);
        _pushData["transferKind"].Should().Be("AtmWithdrawal");
    }

    [Fact]
    public async Task TheAiTransferHint_IsStoredForBothDirections()
    {
        AiResult? stored = null;
        _logs.Setup(r => r.AddAiResultAsync(It.IsAny<AiResult>(), It.IsAny<CancellationToken>()))
            .Callback<AiResult, CancellationToken>((r, _) => stored = r)
            .Returns(Task.CompletedTask);
        _ai.Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(2_000_000, "debit", null, "RUT TIEN ATM", DateTimeOffset.UtcNow, 500_000, 0.95, "transfer", "atm_withdrawal"),
                new CategorizationResult("other", 0.9),
                new DuplicateResult(false, null),
                new ModelVersions("1", "1", "1"),
                20));
        DetectorReturns(DuplicateMatch.None, marks: false);

        var result = await Handler().HandleAsync(Command());

        stored!.RelationHint.Should().Be(NotificationRelationHint.Transfer);
        stored.TransferKind.Should().Be(TransferKind.AtmWithdrawal);
        result.RelationHint.Should().BeNull("relationHint của analyze vẫn chỉ nói hoàn/đảo như Phase 81");
    }
}

