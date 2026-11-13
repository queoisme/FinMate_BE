using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Transactions.Transfers;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// <see cref="TransferMatcher"/> (Phase 83): khi nào hai chiều chi/thu là một lần chuyển giữa các ví của mình (BR-TRF-02), gợi ý một chiều
/// (BR-TRF-03), và những giao dịch không bao giờ được gợi ý (BR-TRF-04, S2). Hàm thuần, không DB.
/// </summary>
public class TransferMatcherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(7));
    private static readonly TransferSettings Settings = new();
    private static readonly TransferAccount Vcb = new(Guid.NewGuid(), "VCB của tôi", AccountType.Bank, "vietcombank", "Vietcombank");
    private static readonly TransferAccount Momo = new(Guid.NewGuid(), "Ví MoMo", AccountType.EWallet, "momo", "MoMo");
    private static readonly TransferAccount Cash = new(Guid.NewGuid(), "Tiền mặt", AccountType.Cash, null, null);
    private static readonly TransferAccount Cash2 = new(Guid.NewGuid(), "Két sắt", AccountType.Cash, null, null);
    private static readonly IReadOnlyList<TransferAccount> Accounts = [Vcb, Momo, Cash];

    private static TransferProbe Probe(
        TransactionType type,
        TransferAccount account,
        long amount = 500_000,
        TimeSpan? offset = null,
        string? merchant = null,
        string? description = null,
        TransferKind? kind = null,
        bool hinted = false,
        TransactionStatus status = TransactionStatus.Draft)
        => new(
            Guid.NewGuid(), type, status, amount, T0 + (offset ?? TimeSpan.Zero), T0 + (offset ?? TimeSpan.Zero), account.Id, merchant, description,
            hinted || kind is not null ? NotificationRelationHint.Transfer : null, kind);

    private static TransferSuggestion? Suggest(TransferProbe subject, IEnumerable<TransferProbe> others, IReadOnlyList<TransferAccount>? accounts = null)
        => TransferMatcher.Suggest(subject, others.ToList(), accounts ?? Accounts, Settings);

    // ---- BR-TRF-02: ghép hai chiều ----

    [Fact]
    public void BankDebitAndWalletCredit_WithAHint_ArePaired()
    {
        var debit = Probe(TransactionType.Debit, Vcb, description: "chuyen tien den vi MoMo", kind: TransferKind.WalletTopup);
        var credit = Probe(TransactionType.Credit, Momo, offset: TimeSpan.FromMinutes(1), description: "Nap tien tu Vietcombank", kind: TransferKind.WalletTopup);

        var fromDebit = Suggest(debit, [credit])!;
        var fromCredit = Suggest(credit, [debit])!;

        fromDebit.PairedId.Should().Be(credit.Id);
        fromDebit.Kind.Should().Be(TransferKind.WalletTopup);
        fromDebit.FromAccountId.Should().Be(Vcb.Id);
        fromDebit.ToAccountId.Should().Be(Momo.Id);
        fromDebit.FeeCents.Should().Be(0);
        fromCredit.PairedId.Should().Be(debit.Id);
    }

    [Fact]
    public void OnlyOneSideHinted_IsEnough()
    {
        var debit = Probe(TransactionType.Debit, Vcb, kind: TransferKind.WalletTopup);
        var credit = Probe(TransactionType.Credit, Momo, offset: TimeSpan.FromMinutes(5));

        Suggest(credit, [debit])!.PairedId.Should().Be(debit.Id);
    }

    [Fact]
    public void NoHint_ButTheTextNamesTheOtherWallet_IsPaired_AndKindComesFromTheWalletTypes()
    {
        var debit = Probe(TransactionType.Debit, Vcb, description: "TT MOMO 0901234567");
        var credit = Probe(TransactionType.Credit, Momo, offset: TimeSpan.FromMinutes(2));

        var suggestion = Suggest(debit, [credit])!;

        suggestion.PairedId.Should().Be(credit.Id);
        suggestion.Kind.Should().Be(TransferKind.WalletTopup);
        suggestion.HintedKind.Should().BeNull("ghép chỉ nhờ tên ví: thống kê xếp vào nhóm unhinted");
    }

    [Fact]
    public void NameMatching_IsWholeWordAndIgnoresAccents()
    {
        var debit = Probe(TransactionType.Debit, Momo, description: "Rút về Vietcombank");
        var credit = Probe(TransactionType.Credit, Vcb, offset: TimeSpan.FromMinutes(1));
        Suggest(debit, [credit])!.Kind.Should().Be(TransferKind.WalletWithdrawal);

        var noise = Probe(TransactionType.Debit, Vcb, description: "momotaro sushi");
        Suggest(noise, [Probe(TransactionType.Credit, Momo, offset: TimeSpan.FromMinutes(1))]).Should().BeNull();
    }

    [Fact]
    public void D_SendingToAFriendAndReceivingFromSomeoneElse_IsNeverSuggested()
    {
        var debit = Probe(TransactionType.Debit, Vcb, description: "chuyen tien cho ban Nam");
        var credit = Probe(TransactionType.Credit, Momo, offset: TimeSpan.FromMinutes(20), description: "Nhan tien tu Tran Thi B");

        Suggest(debit, [credit]).Should().BeNull();
        Suggest(credit, [debit]).Should().BeNull();
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    [InlineData(-2, true)]
    [InlineData(-3, false)]
    public void CreditMustArriveWithinTheWindow_OrAtMostTheClockSkewEarlier(int minutesAfterDebit, bool paired)
    {
        var debit = Probe(TransactionType.Debit, Vcb, kind: TransferKind.WalletTopup);
        var credit = Probe(TransactionType.Credit, Momo, offset: TimeSpan.FromMinutes(minutesAfterDebit));

        (Suggest(debit, [credit])?.PairedId == credit.Id).Should().Be(paired);
    }

    [Theory]
    [InlineData(1_003_300, 1_000_000, 3_300)]
    [InlineData(1_020_000, 1_000_000, 20_000)]
    public void AFeeWithinBothLimits_IsPaired(long debitAmount, long creditAmount, long fee)
    {
        var debit = Probe(TransactionType.Debit, Vcb, debitAmount, kind: TransferKind.WalletTopup);
        var credit = Probe(TransactionType.Credit, Momo, creditAmount, offset: TimeSpan.FromMinutes(1));

        var suggestion = Suggest(debit, [credit])!;

        suggestion.PairedId.Should().Be(credit.Id);
        suggestion.FeeCents.Should().Be(fee);
        suggestion.AmountCents.Should().Be(creditAmount);
    }

    [Theory]
    [InlineData(1_020_001, 1_000_000)]   // trên 20.000đ
    [InlineData(102_100, 100_000)]       // dưới 20.000đ nhưng trên 2%
    [InlineData(999_999, 1_000_000)]     // tiền vào nhiều hơn tiền ra
    public void AFeeOutsideTheLimits_IsNotAPair(long debitAmount, long creditAmount)
    {
        var debit = Probe(TransactionType.Debit, Vcb, debitAmount, kind: TransferKind.WalletTopup);
        var credit = Probe(TransactionType.Credit, Momo, creditAmount, offset: TimeSpan.FromMinutes(1));

        Suggest(debit, [credit])?.PairedId.Should().BeNull();
    }

    [Fact]
    public void SameWallet_IsNotAPair()
    {
        var debit = Probe(TransactionType.Debit, Vcb, kind: TransferKind.WalletTopup);
        var credit = Probe(TransactionType.Credit, Vcb, offset: TimeSpan.FromMinutes(1));

        Suggest(debit, [credit])?.PairedId.Should().BeNull();
    }

    [Fact]
    public void SeveralCandidates_PreferTheSmallestFeeThenTheNearest()
    {
        var debit = Probe(TransactionType.Debit, Vcb, 1_003_300, kind: TransferKind.WalletTopup);
        var withFee = Probe(TransactionType.Credit, Momo, 1_000_000, offset: TimeSpan.FromMinutes(1));
        var exactLater = Probe(TransactionType.Credit, Momo, 1_003_300, offset: TimeSpan.FromMinutes(20));
        var exactSooner = Probe(TransactionType.Credit, Momo, 1_003_300, offset: TimeSpan.FromMinutes(10));

        Suggest(debit, [withFee, exactLater, exactSooner])!.PairedId.Should().Be(exactSooner.Id);
    }

    // ---- BR-TRF-03: một chiều ----

    [Fact]
    public void AtmWithdrawal_Alone_SuggestsCashWallets()
    {
        var atm = Probe(TransactionType.Debit, Vcb, 2_000_000, description: "RUT TIEN ATM", kind: TransferKind.AtmWithdrawal);

        var suggestion = Suggest(atm, [], [Vcb, Momo, Cash, Cash2])!;

        suggestion.PairedId.Should().BeNull();
        suggestion.Kind.Should().Be(TransferKind.AtmWithdrawal);
        suggestion.CandidateAccountIds.Should().BeEquivalentTo([Cash.Id, Cash2.Id]);
        suggestion.FromAccountId.Should().Be(Vcb.Id);
        suggestion.ToAccountId.Should().BeNull("người dùng chọn ví đích");
    }

    [Fact]
    public void AtmWithdrawal_WithoutAnyCashWallet_StillSuggests_WithNoCandidates()
    {
        var atm = Probe(TransactionType.Debit, Vcb, 2_000_000, kind: TransferKind.AtmWithdrawal);

        var suggestion = Suggest(atm, [], [Vcb, Momo])!;

        suggestion.CandidateAccountIds.Should().BeEmpty("app gợi ý tạo ví Tiền mặt; backend không tự tạo");
    }

    [Fact]
    public void AtmDeposit_Alone_SuggestsCashAsTheSource()
    {
        var deposit = Probe(TransactionType.Credit, Vcb, 3_000_000, kind: TransferKind.AtmDeposit);

        var suggestion = Suggest(deposit, [])!;

        suggestion.CandidateAccountIds.Should().Equal(Cash.Id);
        suggestion.ToAccountId.Should().Be(Vcb.Id);
        suggestion.FromAccountId.Should().BeNull();
    }

    [Fact]
    public void OtherKinds_Alone_SuggestEveryOtherWallet()
    {
        var topup = Probe(TransactionType.Debit, Vcb, kind: TransferKind.WalletTopup);

        Suggest(topup, [])!.CandidateAccountIds.Should().BeEquivalentTo([Momo.Id, Cash.Id]);
    }

    [Fact]
    public void NoHint_AndNoPair_IsNoSuggestion()
    {
        Suggest(Probe(TransactionType.Debit, Vcb), []).Should().BeNull();
    }

    // ---- BR-TRF-04 + S2: không gợi ý ----

    [Theory]
    [InlineData("relation")]
    [InlineData("reversed")]
    [InlineData("merged")]
    [InlineData("suspected")]
    [InlineData("qr")]
    [InlineData("group")]
    [InlineData("refunds")]
    [InlineData("dismissed")]
    public void IneligibleSubject_GetsNoSuggestion(string why)
    {
        var debit = Ineligible(Probe(TransactionType.Debit, Vcb, kind: TransferKind.WalletTopup), why);
        var credit = Probe(TransactionType.Credit, Momo, offset: TimeSpan.FromMinutes(1));

        Suggest(debit, [credit]).Should().BeNull();
    }

    [Theory]
    [InlineData("relation")]
    [InlineData("suspected")]
    [InlineData("qr")]
    [InlineData("group")]
    [InlineData("dismissed")]
    public void IneligibleCounterpart_IsNotOffered(string why)
    {
        var debit = Probe(TransactionType.Debit, Vcb, kind: TransferKind.WalletTopup);
        var credit = Ineligible(Probe(TransactionType.Credit, Momo, offset: TimeSpan.FromMinutes(1)), why);

        Suggest(debit, [credit])?.PairedId.Should().BeNull();
    }

    [Fact]
    public void Transfers_AreNeverSubjectsOrCounterparts()
    {
        var transfer = Probe(TransactionType.Transfer, Vcb, kind: TransferKind.WalletTopup);
        Suggest(transfer, []).Should().BeNull();
    }

    [Theory]
    [InlineData("dismissed", null)]
    [InlineData("shared", TransferBlock.Shared)]
    [InlineData("qr", TransferBlock.QrMatched)]
    [InlineData("suspected", TransferBlock.SuspectedDuplicate)]
    [InlineData("refunds", TransferBlock.HasRefunds)]
    public void Conversion_IgnoresADismissal_ButNotTheOtherBlocks(string why, TransferBlock? expected)
    {
        // S2: "không phải chuyển ví" chỉ tắt gợi ý; người dùng vẫn tự chuyển đổi được.
        var probe = Ineligible(Probe(TransactionType.Debit, Vcb), why == "shared" ? "group" : why);

        TransferMatcher.BlockForConversion(probe).Should().Be(expected);
    }

    private static TransferProbe Ineligible(TransferProbe p, string why) => why switch
    {
        "relation" => p with { Relation = TransactionRelation.Refund },
        "reversed" => p with { IsReversed = true },
        "merged" => p with { IsDeletedOrMerged = true },
        "suspected" => p with { DuplicateStatus = DuplicateStatus.Suspected },
        "qr" => p with { QrMatched = true },
        "group" => p with { GroupId = Guid.NewGuid() },
        "refunds" => p with { HasConfirmedRefunds = true },
        "dismissed" => p with { TransferDismissedAt = T0 },
        _ => p,
    };
}
