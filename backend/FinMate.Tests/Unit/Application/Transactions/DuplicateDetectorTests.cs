using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// <see cref="DuplicateDetector"/>: nạp ứng viên trong cửa sổ rộng nhất, dựng tập cặp đã bị bác từ liên kết của cả hai phía, và gắn
/// <c>suspected</c> lên giao dịch mới khi có ứng viên (BR-DUP-06). Không bao giờ gắn khi ứng viên chỉ để cảnh báo (D4).
/// </summary>
public class DuplicateDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 5, 0, 0, TimeSpan.Zero);
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _account = Guid.NewGuid();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<ISystemConfigProvider> _config = new();
    private DuplicateCandidateQuery? _query;

    public DuplicateDetectorTests()
    {
        _config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemConfigSnapshot(0, new SystemConfigDocument(), null));
    }

    private DuplicateDetector Build(params DuplicateCandidateRow[] rows)
    {
        _transactions
            .Setup(r => r.ListDuplicateCandidatesAsync(It.IsAny<DuplicateCandidateQuery>(), It.IsAny<CancellationToken>()))
            .Callback<DuplicateCandidateQuery, CancellationToken>((q, _) => _query = q)
            .ReturnsAsync(rows);
        return new DuplicateDetector(_transactions.Object, _config.Object);
    }

    private Transaction NewTransaction(TransactionSource source = TransactionSource.Notification) => new()
    {
        Id = Guid.NewGuid(),
        UserId = _user,
        FinancialAccountId = _account,
        AmountCents = 50_000,
        TransactionType = TransactionType.Debit,
        Source = source,
        Status = TransactionStatus.Draft,
        TransactedAt = T0,
        CreatedAt = T0,
    };

    private DuplicateCandidateRow Row(
        TransactionSource source = TransactionSource.Manual, Guid? duplicateOf = null, DuplicateStatus? status = null, Guid? id = null)
        => new(
            new DuplicateProbe(id ?? Guid.NewGuid(), source, TransactionType.Debit, 50_000, T0.AddMinutes(-1), T0.AddMinutes(-1), _account,
                null, null, null, null, false, false, null, null),
            duplicateOf,
            status);

    [Fact]
    public async Task NewTransactionWithACandidate_IsMarkedSuspectedOfRankOne()
    {
        var chat = Row();
        var tx = NewTransaction();

        var match = await Build(chat).MarkSuspectedAsync(tx, packageName: "com.VCB");

        match.SuspectedOf!.Id.Should().Be(chat.Probe.Id);
        tx.DuplicateOfTransactionId.Should().Be(chat.Probe.Id);
        tx.DuplicateStatus.Should().Be(DuplicateStatus.Suspected);
    }

    [Fact]
    public async Task LoadsCandidatesForTheExactAmountInsideTheWidestWindow()
    {
        var tx = NewTransaction();

        await Build().MarkSuspectedAsync(tx, null);

        _query!.UserId.Should().Be(_user);
        _query.AmountsCents.Should().Equal(50_000);
        _query.From.Should().Be(T0.AddHours(-48));
        _query.To.Should().Be(T0.AddHours(48));
    }

    [Fact]
    public async Task NoCandidate_LeavesTheTransactionUntouched()
    {
        var tx = NewTransaction();

        var match = await Build().MarkSuspectedAsync(tx, null);

        match.Candidates.Should().BeEmpty();
        tx.DuplicateOfTransactionId.Should().BeNull();
        tx.DuplicateStatus.Should().BeNull();
    }

    [Fact]
    public async Task WarnOnlyCandidate_DoesNotMarkSuspected()
    {
        var manual = Row(TransactionSource.Manual);
        var receipt = NewTransaction(TransactionSource.Receipt);

        var match = await Build(manual).MarkSuspectedAsync(receipt, null);

        match.Candidates.Should().ContainSingle();
        receipt.DuplicateStatus.Should().BeNull();
    }

    [Fact]
    public async Task PairDismissedFromTheOtherSide_IsNotSuggested()
    {
        var tx = NewTransaction();
        var other = Row(duplicateOf: tx.Id, status: DuplicateStatus.Dismissed);

        var match = await Build(other).MarkSuspectedAsync(tx, null);

        match.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task PairDismissedFromThisSide_IsNotSuggested()
    {
        var tx = NewTransaction();
        var other = Row();
        var self = Row(TransactionSource.Notification, duplicateOf: other.Probe.Id, status: DuplicateStatus.Dismissed, id: tx.Id);

        var match = await Build(other, self).MarkSuspectedAsync(tx, null);

        match.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task AiHint_IsPassedThroughAsATieBreak()
    {
        var aiLog = Guid.NewGuid();
        var plain = Row(TransactionSource.Notification);
        var hinted = new DuplicateCandidateRow(plain.Probe with { Id = Guid.NewGuid(), NotificationLogId = aiLog }, null, null);
        var tx = NewTransaction();

        var match = await Build(plain, hinted).MarkSuspectedAsync(tx, null, aiHintLogId: aiLog);

        match.Candidates[0].Id.Should().Be(hinted.Probe.Id);
    }
}
