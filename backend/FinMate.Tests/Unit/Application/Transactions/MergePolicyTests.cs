using FinMate.Application.Common.Exceptions;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// <see cref="MergePolicy"/> (Phase 82): bản nào được giữ (BR-DUP-09), trường nào lấy từ đâu (BR-DUP-10), và khi nào từ chối gộp
/// (BR-DUP-12, D6, D12). Hàm thuần, không DB.
/// </summary>
public class MergePolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(7));
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Vcb = Guid.NewGuid();
    private static readonly Guid Food = Guid.NewGuid();
    private static readonly Guid Shopping = Guid.NewGuid();

    private static Transaction Tx(
        TransactionSource source,
        TransactionStatus status,
        TimeSpan? created = null,
        Guid? account = null,
        Guid? category = null,
        TimeSpan? at = null,
        long amount = 50_000,
        TransactionType type = TransactionType.Debit,
        string? merchant = null,
        string? description = null,
        long? balance = null,
        string[]? mergedSources = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Source = source,
            Status = status,
            CreatedAt = T0 + (created ?? TimeSpan.Zero),
            FinancialAccountId = account ?? Vcb,
            CategoryId = category,
            TransactedAt = T0 + (at ?? TimeSpan.Zero),
            AmountCents = amount,
            TransactionType = type,
            MerchantName = merchant,
            Description = description,
            BalanceAfterCents = balance,
            MergedSources = mergedSources,
        };

    private static MergeFacts Facts(bool loserRefunded = false, bool aQr = false, bool bQr = false) => new(loserRefunded, aQr, bQr);

    // ---- BR-DUP-09: survivor ----

    [Fact]
    public void ExactlyOneConfirmed_IsKept()
    {
        var chat = Tx(TransactionSource.Manual, TransactionStatus.Confirmed, created: TimeSpan.FromMinutes(5));
        var bank = Tx(TransactionSource.Notification, TransactionStatus.Draft);

        MergePolicy.ChooseSurvivor(bank, chat).Should().BeSameAs(chat);
        MergePolicy.ChooseSurvivor(chat, bank).Should().BeSameAs(chat);
    }

    [Fact]
    public void BothConfirmed_KeepsTheEarlierCreated()
    {
        var first = Tx(TransactionSource.Manual, TransactionStatus.Confirmed);
        var second = Tx(TransactionSource.Notification, TransactionStatus.Confirmed, created: TimeSpan.FromSeconds(1));

        MergePolicy.ChooseSurvivor(second, first).Should().BeSameAs(first);
    }

    [Fact]
    public void BothDrafts_KeepsTheNotification()
    {
        var support = Tx(TransactionSource.Support, TransactionStatus.Draft);
        var bank = Tx(TransactionSource.Notification, TransactionStatus.Draft, created: TimeSpan.FromMinutes(1));

        MergePolicy.ChooseSurvivor(support, bank).Should().BeSameAs(bank);
    }

    [Fact]
    public void TwoNotificationDrafts_KeepTheEarlierCreated()
    {
        var first = Tx(TransactionSource.Notification, TransactionStatus.Draft);
        var second = Tx(TransactionSource.Notification, TransactionStatus.Draft, created: TimeSpan.FromMinutes(3));

        MergePolicy.ChooseSurvivor(second, first).Should().BeSameAs(first);
    }

    // ---- BR-DUP-10: fields ----

    [Fact]
    public void BankGivesTimeWalletAndBalance_UserGivesCategoryNoteAndMerchant()
    {
        var chat = Tx(TransactionSource.Manual, TransactionStatus.Confirmed, account: Cash, category: Food, at: TimeSpan.FromMinutes(-1),
            merchant: "trà sữa Phúc Long", description: "với Lan", balance: 777);
        var bank = Tx(TransactionSource.Notification, TransactionStatus.Draft, account: Vcb, category: Shopping,
            merchant: "PHUC LONG 123", description: "TT QR", balance: 1_000_000);

        var fields = MergePolicy.Fields(chat, bank, overrideCategoryId: null);

        fields.AccountId.Should().Be(Vcb);
        fields.TransactedAt.Should().Be(bank.TransactedAt);
        fields.BalanceAfterCents.Should().Be(1_000_000);
        fields.CategoryId.Should().Be(Food);
        fields.Description.Should().Be("với Lan");
        fields.MerchantName.Should().Be("trà sữa Phúc Long");
    }

    [Fact]
    public void ReceiptIsTheBankSide_WhenThereIsNoNotification()
    {
        var manual = Tx(TransactionSource.Manual, TransactionStatus.Confirmed, account: Cash, category: Food, at: TimeSpan.FromHours(-3));
        var receipt = Tx(TransactionSource.Receipt, TransactionStatus.Confirmed, account: Vcb, category: Shopping, created: TimeSpan.FromHours(1));

        var fields = MergePolicy.Fields(manual, receipt, null);

        fields.TransactedAt.Should().Be(receipt.TransactedAt);
        fields.AccountId.Should().Be(Vcb);
        fields.BalanceAfterCents.Should().Be(manual.BalanceAfterCents, "số dư của hoá đơn không phải số ngân hàng báo");
        fields.CategoryId.Should().Be(Food);
    }

    [Fact]
    public void CategoryFromTheRequest_WinsEverything()
    {
        var chat = Tx(TransactionSource.Manual, TransactionStatus.Confirmed, category: Food);
        var bank = Tx(TransactionSource.Notification, TransactionStatus.Draft, category: Shopping);
        var chosen = Guid.NewGuid();

        MergePolicy.Fields(chat, bank, chosen).CategoryId.Should().Be(chosen);
    }

    [Fact]
    public void MissingUserValues_FallBackToTheOtherSide()
    {
        var chat = Tx(TransactionSource.Manual, TransactionStatus.Confirmed);
        var bank = Tx(TransactionSource.Notification, TransactionStatus.Draft, category: Shopping, merchant: "PHUC LONG", description: "TT QR");

        var fields = MergePolicy.Fields(chat, bank, null);

        fields.CategoryId.Should().Be(Shopping);
        fields.MerchantName.Should().Be("PHUC LONG");
        fields.Description.Should().Be("TT QR");
    }

    [Fact]
    public void MergedSources_UnionsBothSidesAndEarlierMerges()
    {
        var survivor = Tx(TransactionSource.Manual, TransactionStatus.Confirmed, mergedSources: ["manual", "receipt"]);
        var loser = Tx(TransactionSource.Notification, TransactionStatus.Draft);

        MergePolicy.MergedSources(survivor, loser).Should().Equal("manual", "notification", "receipt");
    }

    // ---- BR-DUP-12 + D6 + D12: refusals ----

    [Fact]
    public void SameShape_OutsideAnyWindow_CanStillBeMerged()
    {
        var a = Tx(TransactionSource.Manual, TransactionStatus.Confirmed, at: TimeSpan.FromDays(-30));
        var b = Tx(TransactionSource.Manual, TransactionStatus.Confirmed);

        var act = () => MergePolicy.EnsureMergeable(a, b, b, Facts());

        act.Should().NotThrow("gộp tay không đòi cửa sổ thời gian hay cặp nguồn (D12)");
    }

    [Theory]
    [InlineData("type")]
    [InlineData("amount")]
    [InlineData("transfer")]
    [InlineData("relation")]
    [InlineData("self")]
    public void DifferentShape_IsRefused(string what)
    {
        var a = Tx(TransactionSource.Manual, TransactionStatus.Confirmed);
        var b = what switch
        {
            "type" => Tx(TransactionSource.Notification, TransactionStatus.Draft, type: TransactionType.Credit),
            "amount" => Tx(TransactionSource.Notification, TransactionStatus.Draft, amount: 50_001),
            // Phase 83 (BR-TRF-06): một khoản chi gộp được vào một Transfer đã ghi tay; hai Transfer với nhau thì không.
            "transfer" => Tx(TransactionSource.Notification, TransactionStatus.Draft, type: TransactionType.Transfer),
            "relation" => Tx(TransactionSource.Notification, TransactionStatus.Draft, type: TransactionType.Credit),
            _ => a,
        };
        if (what == "transfer")
        {
            a.TransactionType = TransactionType.Transfer;
        }

        if (what == "relation")
        {
            a.TransactionType = TransactionType.Credit;
            b.Relation = TransactionRelation.Refund;
        }

        var act = () => MergePolicy.EnsureMergeable(a, b, MergePolicy.ChooseSurvivor(a, b), Facts());

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(TransactionErrorCodes.MergeNotAllowed);
    }

    [Fact]
    public void ConfirmedLoserWithRefunds_IsRefused()
    {
        var first = Tx(TransactionSource.Manual, TransactionStatus.Confirmed);
        var second = Tx(TransactionSource.Notification, TransactionStatus.Confirmed, created: TimeSpan.FromMinutes(1));

        var act = () => MergePolicy.EnsureMergeable(first, second, first, Facts(loserRefunded: true));

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(TransactionErrorCodes.MergeNotAllowed);
    }

    [Fact]
    public void ReversedLoser_IsRefused()
    {
        var first = Tx(TransactionSource.Manual, TransactionStatus.Confirmed);
        var second = Tx(TransactionSource.Notification, TransactionStatus.Confirmed, created: TimeSpan.FromMinutes(1));
        second.IsReversed = true;

        var act = () => MergePolicy.EnsureMergeable(first, second, first, Facts());

        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void LoserSharedIntoAGroup_IsRefused()
    {
        var first = Tx(TransactionSource.Manual, TransactionStatus.Confirmed);
        var second = Tx(TransactionSource.Notification, TransactionStatus.Confirmed, created: TimeSpan.FromMinutes(1));
        second.GroupId = Guid.NewGuid();

        var act = () => MergePolicy.EnsureMergeable(first, second, first, Facts());

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(TransactionErrorCodes.MergeNotAllowed);
    }

    [Fact]
    public void SurvivorSharedIntoAGroup_IsFine()
    {
        var chat = Tx(TransactionSource.Manual, TransactionStatus.Confirmed);
        chat.GroupId = Guid.NewGuid();
        var bank = Tx(TransactionSource.Notification, TransactionStatus.Draft);

        var act = () => MergePolicy.EnsureMergeable(chat, bank, chat, Facts());

        act.Should().NotThrow();
    }

    [Fact]
    public void OnlyTheLoserMatchedAQrIntent_IsFine_TheMatchMoves()
    {
        var chat = Tx(TransactionSource.Manual, TransactionStatus.Confirmed);
        var bank = Tx(TransactionSource.Notification, TransactionStatus.Draft);

        var act = () => MergePolicy.EnsureMergeable(chat, bank, chat, Facts(bQr: true));

        act.Should().NotThrow("ý định QR chuyển sang survivor (D6)");
    }

    [Fact]
    public void BothMatchedAQrIntent_IsRefused()
    {
        var chat = Tx(TransactionSource.Manual, TransactionStatus.Confirmed);
        var bank = Tx(TransactionSource.Notification, TransactionStatus.Draft);

        var act = () => MergePolicy.EnsureMergeable(chat, bank, chat, Facts(aQr: true, bQr: true));

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(TransactionErrorCodes.MergeNotAllowed);
    }

    // ---- Phase 83, BR-TRF-06 + D9: gộp thông báo vào một Transfer đã ghi tay ----

    private static Transaction ManualTransfer(long amount = 500_000)
    {
        var t = Tx(TransactionSource.Manual, TransactionStatus.Confirmed, created: TimeSpan.FromMinutes(5), account: Vcb, amount: amount, type: TransactionType.Transfer);
        t.CounterAccountId = Cash;
        return t;
    }

    [Fact]
    public void TheTransfer_IsAlwaysKept_EvenWhenTheNotificationIsOlderAndConfirmed()
    {
        var transfer = ManualTransfer();
        var bank = Tx(TransactionSource.Notification, TransactionStatus.Confirmed, account: Vcb, amount: 500_000);

        MergePolicy.ChooseSurvivor(bank, transfer).Should().BeSameAs(transfer);
    }

    [Theory]
    [InlineData(TransactionType.Debit, "vcb", 500_000, true)]
    [InlineData(TransactionType.Debit, "vcb", 503_300, true)]
    [InlineData(TransactionType.Debit, "vcb", 530_000, false)]
    [InlineData(TransactionType.Debit, "cash", 500_000, false)]
    [InlineData(TransactionType.Credit, "cash", 500_000, true)]
    [InlineData(TransactionType.Credit, "cash", 499_000, false)]
    [InlineData(TransactionType.Credit, "vcb", 500_000, false)]
    public void ANotification_FitsTheTransferOnlyOnTheRightSide(TransactionType type, string wallet, long amount, bool fits)
    {
        var transfer = ManualTransfer();
        var bank = Tx(TransactionSource.Notification, TransactionStatus.Draft, account: wallet == "vcb" ? Vcb : Cash, amount: amount, type: type);

        var act = () => MergePolicy.EnsureMergeable(bank, transfer, transfer, Facts());

        if (fits)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(TransactionErrorCodes.MergeNotAllowed);
        }
    }
}

