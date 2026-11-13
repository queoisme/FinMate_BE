using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// <see cref="DuplicateMatcher"/> (Phase 82): ai là ứng viên trùng (BR-DUP-03), xếp hạng thế nào (BR-DUP-04, D14), và khi nào hai thông báo
/// là cùng một sự kiện ngân hàng (BR-DUP-05, D5). Hàm thuần, không DB.
/// </summary>
public class DuplicateMatcherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(7));
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Vcb = Guid.NewGuid();
    private static readonly DuplicateSettings Settings = new();

    private static DuplicateProbe Probe(
        TransactionSource source = TransactionSource.Notification,
        TransactionType type = TransactionType.Debit,
        long amount = 50_000,
        TimeSpan? offset = null,
        Guid? account = null,
        string? merchant = null,
        long? balance = null,
        string? package = null,
        TransactionRelation? relation = null,
        bool deleted = false,
        bool merged = false,
        string[]? mergedSources = null,
        Guid? notificationLogId = null,
        TimeSpan? createdOffset = null)
        => new(
            Id: Guid.NewGuid(),
            Source: source,
            Type: type,
            AmountCents: amount,
            TransactedAt: T0 + (offset ?? TimeSpan.Zero),
            CreatedAt: T0 + (createdOffset ?? offset ?? TimeSpan.Zero),
            AccountId: account ?? Vcb,
            MerchantName: merchant,
            BalanceAfterCents: balance,
            PackageName: package,
            Relation: relation,
            IsDeleted: deleted,
            IsMerged: merged,
            MergedSources: mergedSources,
            NotificationLogId: notificationLogId);

    private static DuplicateMatch Match(
        DuplicateProbe subject, IEnumerable<DuplicateProbe> candidates, IReadOnlySet<Guid>? dismissed = null, Guid? aiHintLogId = null)
        => DuplicateMatcher.Match(subject, candidates.ToList(), dismissed ?? new HashSet<Guid>(), Settings, aiHintLogId);

    // ---- BR-DUP-03: ai là ứng viên ----

    [Fact]
    public void ChatThenBankNotification_IsACandidate()
    {
        var chat = Probe(TransactionSource.Manual, account: Cash, merchant: "trà sữa", offset: TimeSpan.FromMinutes(-1));
        var bank = Probe(TransactionSource.Notification, account: Vcb);

        var match = Match(bank, [chat]);

        match.SameEventId.Should().BeNull();
        match.Candidates.Should().ContainSingle().Which.Id.Should().Be(chat.Id);
        match.Candidates[0].Pair.Should().Be(DuplicatePair.ManualNotification);
        match.Candidates[0].MarksSuspected.Should().BeTrue();
    }

    [Theory]
    [InlineData(TransactionType.Credit)]
    [InlineData(TransactionType.Transfer)]
    public void DifferentDirection_IsNotACandidate(TransactionType type)
    {
        var other = Probe(TransactionSource.Manual, type: type);
        Match(Probe(), [other]).Candidates.Should().BeEmpty();
    }

    [Fact]
    public void TransferSubject_HasNoCandidates()
    {
        var other = Probe(TransactionSource.Manual, type: TransactionType.Transfer);
        Match(Probe(type: TransactionType.Transfer), [other]).Candidates.Should().BeEmpty();
    }

    [Fact]
    public void DifferentAmount_IsNotACandidate()
    {
        Match(Probe(), [Probe(TransactionSource.Manual, amount: 50_001)]).Candidates.Should().BeEmpty();
    }

    [Fact]
    public void DeletedOrMerged_IsNotACandidate()
    {
        var deleted = Probe(TransactionSource.Manual, deleted: true);
        var merged = Probe(TransactionSource.Manual, merged: true, deleted: true);

        Match(Probe(), [deleted, merged]).Candidates.Should().BeEmpty();
    }

    [Fact]
    public void RefundOrReversal_IsNeverACandidate_EitherSide()
    {
        var refund = Probe(TransactionSource.Manual, type: TransactionType.Credit, relation: TransactionRelation.Refund);
        Match(Probe(type: TransactionType.Credit), [refund]).Candidates.Should().BeEmpty();

        var plainCredit = Probe(TransactionSource.Manual, type: TransactionType.Credit);
        Match(Probe(type: TransactionType.Credit, relation: TransactionRelation.Reversal), [plainCredit]).Candidates.Should().BeEmpty();
    }

    [Fact]
    public void DismissedPair_IsNotSuggestedAgain()
    {
        var chat = Probe(TransactionSource.Manual);
        Match(Probe(), [chat], dismissed: new HashSet<Guid> { chat.Id }).Candidates.Should().BeEmpty();
    }

    [Fact]
    public void SubjectItself_IsNeverItsOwnCandidate()
    {
        var subject = Probe();
        Match(subject, [subject]).Candidates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(TransactionSource.Manual, TransactionSource.Manual)]
    [InlineData(TransactionSource.Manual, TransactionSource.Voice)]
    [InlineData(TransactionSource.Support, TransactionSource.Manual)]
    [InlineData(TransactionSource.Receipt, TransactionSource.Receipt)]
    public void TwoUserEnteredRows_AreNeverCompared(TransactionSource subjectSource, TransactionSource otherSource)
    {
        Match(Probe(subjectSource), [Probe(otherSource)]).Candidates.Should().BeEmpty();
    }

    // ---- BR-DUP-03: cửa sổ theo cặp nguồn, ở biên ----

    [Theory]
    [InlineData(TransactionSource.Notification, 10 * 60, true)]
    [InlineData(TransactionSource.Notification, 10 * 60 + 1, false)]
    [InlineData(TransactionSource.Manual, 48 * 3600, true)]
    [InlineData(TransactionSource.Manual, 48 * 3600 + 1, false)]
    [InlineData(TransactionSource.Voice, 48 * 3600, true)]
    [InlineData(TransactionSource.Support, 48 * 3600 + 1, false)]
    [InlineData(TransactionSource.Receipt, 12 * 3600, true)]
    [InlineData(TransactionSource.Receipt, 12 * 3600 + 1, false)]
    public void Window_DependsOnTheSourcePair(TransactionSource otherSource, int secondsApart, bool isCandidate)
    {
        var earlier = Probe(otherSource, offset: TimeSpan.FromSeconds(-secondsApart));
        var later = Probe(otherSource, offset: TimeSpan.FromSeconds(secondsApart));

        Match(Probe(), [earlier]).Candidates.Any().Should().Be(isCandidate);
        Match(Probe(), [later]).Candidates.Any().Should().Be(isCandidate);   // cửa sổ đối xứng: khoản nhập tay có thể đến sau thông báo
    }

    [Fact]
    public void ReceiptAndManual_IsWarnOnly()
    {
        var manual = Probe(TransactionSource.Manual, offset: TimeSpan.FromHours(-2));

        var match = Match(Probe(TransactionSource.Receipt), [manual]);

        match.Candidates.Should().ContainSingle();
        match.Candidates[0].Pair.Should().Be(DuplicatePair.ReceiptManual);
        match.Candidates[0].MarksSuspected.Should().BeFalse();
    }

    [Fact]
    public void ReceiptAndManual_UsesTheReceiptWindow()
    {
        var manual = Probe(TransactionSource.Manual, offset: TimeSpan.FromHours(-13));
        Match(Probe(TransactionSource.Receipt), [manual]).Candidates.Should().BeEmpty();
    }

    // ---- BR-DUP-04 + D14: xếp hạng ----

    [Fact]
    public void Ranking_SameWalletThenMerchantThenAiHintThenNearestTime_AtMostThree()
    {
        var aiLog = Guid.NewGuid();
        var nearest = Probe(TransactionSource.Manual, account: Cash, offset: TimeSpan.FromMinutes(-1));
        var aiHinted = Probe(TransactionSource.Notification, account: Cash, offset: TimeSpan.FromMinutes(-5), notificationLogId: aiLog);
        var sameMerchant = Probe(TransactionSource.Manual, account: Cash, merchant: "Highlands Coffee", offset: TimeSpan.FromHours(-30));
        var sameWallet = Probe(TransactionSource.Manual, account: Vcb, offset: TimeSpan.FromHours(-40));
        var subject = Probe(account: Vcb, merchant: "HIGHLANDS  COFFEE");

        var match = Match(subject, [nearest, aiHinted, sameMerchant, sameWallet], aiHintLogId: aiLog);

        match.Candidates.Select(c => c.Id).Should().Equal(sameWallet.Id, sameMerchant.Id, aiHinted.Id);
    }

    [Fact]
    public void OtherWallet_IsStillACandidate()
    {
        var cash = Probe(TransactionSource.Manual, account: Cash);
        Match(Probe(account: Vcb), [cash]).Candidates.Should().ContainSingle();
    }

    [Fact]
    public void EqualRank_PrefersTheEarlierCreated()
    {
        var first = Probe(TransactionSource.Notification, offset: TimeSpan.FromMinutes(-2), createdOffset: TimeSpan.FromMinutes(-2));
        var second = Probe(TransactionSource.Notification, offset: TimeSpan.FromMinutes(2), createdOffset: TimeSpan.FromMinutes(-1));

        Match(Probe(), [second, first]).Candidates.Select(c => c.Id).Should().Equal(first.Id, second.Id);
    }

    // ---- BR-DUP-05 + D5: số dư sau giao dịch ----

    [Fact]
    public void SameProviderSameBalance_IsTheSameEvent()
    {
        var first = Probe(balance: 1_000_000, package: "com.VCB", offset: TimeSpan.FromMinutes(-3));
        var again = Probe(balance: 1_000_000, package: "com.VCB");

        var match = Match(again, [first]);

        match.SameEventId.Should().Be(first.Id);
    }

    [Fact]
    public void SameProviderDifferentBalance_AreTwoRealTransactions()
    {
        var first = Probe(balance: 1_035_000, package: "com.VCB", offset: TimeSpan.FromMinutes(-3));
        var second = Probe(balance: 1_000_000, package: "com.VCB");

        var match = Match(second, [first]);

        match.SameEventId.Should().BeNull();
        match.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void MissingBalance_FallsBackToTheWindowRule()
    {
        var first = Probe(balance: null, package: "com.VCB", offset: TimeSpan.FromMinutes(-3));
        var second = Probe(balance: 1_000_000, package: "com.VCB");

        var match = Match(second, [first]);

        match.SameEventId.Should().BeNull();
        match.Candidates.Should().ContainSingle().Which.Id.Should().Be(first.Id);
    }

    [Fact]
    public void SameBalance_DifferentProvider_IsNotTheSameEvent()
    {
        var bank = Probe(balance: 1_000_000, package: "com.VCB", offset: TimeSpan.FromMinutes(-3));
        var wallet = Probe(balance: 1_000_000, package: "com.mservice.momo");

        var match = Match(wallet, [bank]);

        match.SameEventId.Should().BeNull();
        match.Candidates.Should().ContainSingle();
    }

    [Fact]
    public void SameBalance_OutsideTheNotificationWindow_IsNotTheSameEvent()
    {
        // Chi 50k, nhận 50k, chi 50k: số dư sau lặp lại thật, nên chỉ tin trong cửa sổ ngắn.
        var earlier = Probe(TransactionSource.Manual, balance: 1_000_000, package: "com.VCB",
            mergedSources: ["manual", "notification"], offset: TimeSpan.FromHours(-5));
        var later = Probe(balance: 1_000_000, package: "com.VCB");

        var match = Match(later, [earlier]);

        match.SameEventId.Should().BeNull();
        match.Candidates.Should().ContainSingle();
    }

    [Fact]
    public void BalanceOfAHandEnteredRow_IsIgnored()
    {
        // Số dư trên dòng nhập tay là số app tự cộng, khác nghĩa với số dư ngân hàng báo.
        var manual = Probe(TransactionSource.Manual, balance: 777, package: null, offset: TimeSpan.FromMinutes(-1));
        var bank = Probe(balance: 1_000_000, package: "com.VCB");

        Match(bank, [manual]).Candidates.Should().ContainSingle();
    }

    [Fact]
    public void MergedSurvivorVerifiedByTheBank_ComparesBalances()
    {
        var survivor = Probe(TransactionSource.Manual, balance: 1_035_000, package: "com.VCB",
            mergedSources: ["manual", "notification"], offset: TimeSpan.FromMinutes(-3));
        var next = Probe(balance: 1_000_000, package: "com.VCB");

        Match(next, [survivor]).Candidates.Should().BeEmpty();
    }

    // ---- Phase 83, BR-TRF-06: thông báo về sau một Transfer đã ghi tay ----

    private static readonly Guid Momo = Guid.NewGuid();
    private static readonly TransferSettings Transfers = new();

    private static DuplicateProbe ManualTransfer(long amount = 50_000, TimeSpan? offset = null)
        => Probe(TransactionSource.Manual, TransactionType.Transfer, amount, offset ?? TimeSpan.FromMinutes(-5), account: Vcb) with { CounterAccountId = Momo };

    private static DuplicateMatch MatchWithTransfers(DuplicateProbe subject, params DuplicateProbe[] candidates)
        => DuplicateMatcher.Match(subject, candidates, new HashSet<Guid>(), Settings, null, Transfers);

    [Fact]
    public void BankDebitFromTheSourceWallet_IsSuspectedOfTheTransfer()
    {
        var transfer = ManualTransfer();

        var match = MatchWithTransfers(Probe(account: Vcb), transfer);

        match.Candidates.Should().ContainSingle().Which.Id.Should().Be(transfer.Id);
        match.Candidates[0].Pair.Should().Be(DuplicatePair.TransferNotification);
        match.Candidates[0].MarksSuspected.Should().BeTrue();
    }

    [Fact]
    public void BankCreditIntoTheDestinationWallet_IsSuspectedOfTheTransfer()
    {
        var transfer = ManualTransfer();

        MatchWithTransfers(Probe(type: TransactionType.Credit, account: Momo), transfer).Candidates.Should().ContainSingle();
    }

    [Fact]
    public void BankDebitWithAFeeWithinTheLimits_StillMatches_ButACreditMustBeExact()
    {
        var transfer = ManualTransfer(1_000_000);

        MatchWithTransfers(Probe(amount: 1_003_300, account: Vcb), transfer).Candidates.Should().ContainSingle();
        MatchWithTransfers(Probe(amount: 1_020_001, account: Vcb), transfer).Candidates.Should().BeEmpty();
        MatchWithTransfers(Probe(type: TransactionType.Credit, amount: 999_000, account: Momo), transfer).Candidates.Should().BeEmpty();
    }

    [Fact]
    public void WrongWalletSide_DoesNotMatchTheTransfer()
    {
        var transfer = ManualTransfer();

        MatchWithTransfers(Probe(account: Momo), transfer).Candidates.Should().BeEmpty("tiền ra phải ở ví nguồn");
        MatchWithTransfers(Probe(type: TransactionType.Credit, account: Vcb), transfer).Candidates.Should().BeEmpty("tiền vào phải ở ví đích");
    }

    [Theory]
    [InlineData(48 * 60, true)]
    [InlineData(48 * 60 + 1, false)]
    public void TheWindowIsTheLargerOfThePairWindowAndTheManualWindow(int minutesApart, bool matches)
    {
        var transfer = ManualTransfer(offset: TimeSpan.FromMinutes(-minutesApart));

        MatchWithTransfers(Probe(account: Vcb), transfer).Candidates.Any().Should().Be(matches);
    }

    [Fact]
    public void WithoutTransferSettings_TransfersAreStillIgnored()
    {
        Match(Probe(account: Vcb), [ManualTransfer()]).Candidates.Should().BeEmpty();
    }

    [Fact]
    public void AHandEnteredDebit_IsNeverComparedWithATransfer()
    {
        MatchWithTransfers(Probe(TransactionSource.Manual, account: Vcb), ManualTransfer()).Candidates.Should().BeEmpty();
    }
}

