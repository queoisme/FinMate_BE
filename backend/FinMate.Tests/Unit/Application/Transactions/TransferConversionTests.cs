using FinMate.Application.Transactions.Transfers;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// <see cref="TransferConversion"/> (Phase 83): bất biến số dư BR-TRF-07 cho MỌI tổ hợp nháp/đã xác nhận, có và không có phí; bản giữ lại theo
/// BR-TRF-08; một chiều theo BR-TRF-09. Hàm thuần.
/// </summary>
public class TransferConversionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(7));
    private static readonly Guid Vcb = Guid.NewGuid();
    private static readonly Guid Momo = Guid.NewGuid();

    private static ConversionSide Side(TransactionType type, TransactionStatus status, long amount, Guid account, int createdMinutes = 0)
        => new(Guid.NewGuid(), type, status, amount, account, T0.AddMinutes(createdMinutes));

    /// <summary>Phần mà các chiều ĐÃ xác nhận đang tác động lên số dư (nháp chưa tác động gì).</summary>
    private static Dictionary<Guid, long> Applied(params ConversionSide[] sides)
    {
        var applied = new Dictionary<Guid, long>();
        foreach (var s in sides.Where(s => s.Status == TransactionStatus.Confirmed))
        {
            applied[s.AccountId] = applied.GetValueOrDefault(s.AccountId) + (s.Type == TransactionType.Credit ? s.AmountCents : -s.AmountCents);
        }

        return applied;
    }

    private static long After(Dictionary<Guid, long> applied, TransferPlan plan, Guid account)
        => applied.GetValueOrDefault(account) + plan.BalanceDeltas.GetValueOrDefault(account);

    [Theory]
    [InlineData(TransactionStatus.Draft, TransactionStatus.Draft, 500_000, 500_000)]
    [InlineData(TransactionStatus.Draft, TransactionStatus.Confirmed, 500_000, 500_000)]
    [InlineData(TransactionStatus.Confirmed, TransactionStatus.Draft, 500_000, 500_000)]
    [InlineData(TransactionStatus.Confirmed, TransactionStatus.Confirmed, 500_000, 500_000)]
    [InlineData(TransactionStatus.Draft, TransactionStatus.Draft, 1_003_300, 1_000_000)]
    [InlineData(TransactionStatus.Draft, TransactionStatus.Confirmed, 1_003_300, 1_000_000)]
    [InlineData(TransactionStatus.Confirmed, TransactionStatus.Draft, 1_003_300, 1_000_000)]
    [InlineData(TransactionStatus.Confirmed, TransactionStatus.Confirmed, 1_003_300, 1_000_000)]
    public void BR_TRF_07_EachWalletEndsAsIfBothSidesWereConfirmedAsPlainDebitAndCredit(
        TransactionStatus debitStatus, TransactionStatus creditStatus, long debitAmount, long creditAmount)
    {
        var debit = Side(TransactionType.Debit, debitStatus, debitAmount, Vcb);
        var credit = Side(TransactionType.Credit, creditStatus, creditAmount, Momo, createdMinutes: 1);
        var applied = Applied(debit, credit);

        var plan = TransferConversion.Pair(debit, credit);

        After(applied, plan, Vcb).Should().Be(-debitAmount, "ví nguồn: −chuyển −phí = −tiền ra mà ngân hàng báo");
        After(applied, plan, Momo).Should().Be(creditAmount);
        plan.AmountCents.Should().Be(creditAmount);
        plan.FeeCents.Should().Be(debitAmount - creditAmount);
        plan.FromAccountId.Should().Be(Vcb);
        plan.ToAccountId.Should().Be(Momo);
    }

    [Fact]
    public void BR_TRF_08_ExactlyOneConfirmed_IsKept()
    {
        var debit = Side(TransactionType.Debit, TransactionStatus.Draft, 500_000, Vcb);
        var credit = Side(TransactionType.Credit, TransactionStatus.Confirmed, 500_000, Momo, createdMinutes: 1);

        var plan = TransferConversion.Pair(debit, credit);

        plan.SurvivorId.Should().Be(credit.Id);
        plan.MergedId.Should().Be(debit.Id);
    }

    [Fact]
    public void BR_TRF_08_BothConfirmed_KeepsTheEarlierCreated()
    {
        var debit = Side(TransactionType.Debit, TransactionStatus.Confirmed, 500_000, Vcb, createdMinutes: 5);
        var credit = Side(TransactionType.Credit, TransactionStatus.Confirmed, 500_000, Momo, createdMinutes: 1);

        TransferConversion.Pair(debit, credit).SurvivorId.Should().Be(credit.Id);
    }

    [Fact]
    public void BR_TRF_08_BothDrafts_KeepsTheDebit()
    {
        var debit = Side(TransactionType.Debit, TransactionStatus.Draft, 500_000, Vcb, createdMinutes: 5);
        var credit = Side(TransactionType.Credit, TransactionStatus.Draft, 500_000, Momo, createdMinutes: 1);

        TransferConversion.Pair(debit, credit).SurvivorId.Should().Be(debit.Id);
    }

    [Theory]
    [InlineData(TransactionStatus.Draft)]
    [InlineData(TransactionStatus.Confirmed)]
    public void BR_TRF_09_OneDebit_MovesTheMoneyIntoTheChosenWallet(TransactionStatus status)
    {
        var atm = Side(TransactionType.Debit, status, 2_000_000, Vcb);
        var cash = Guid.NewGuid();
        var applied = Applied(atm);

        var plan = TransferConversion.OneSided(atm, cash);

        After(applied, plan, Vcb).Should().Be(-2_000_000, "tiền vẫn ra khỏi ngân hàng đúng như thông báo");
        After(applied, plan, cash).Should().Be(2_000_000, "ví đích tăng đúng số tiền");
        plan.FeeCents.Should().Be(0);
        plan.SurvivorId.Should().Be(atm.Id);
        plan.MergedId.Should().BeNull();
        (plan.FromAccountId, plan.ToAccountId).Should().Be((Vcb, cash));
    }

    [Theory]
    [InlineData(TransactionStatus.Draft)]
    [InlineData(TransactionStatus.Confirmed)]
    public void BR_TRF_09_OneCredit_TakesTheMoneyFromTheChosenWallet(TransactionStatus status)
    {
        var deposit = Side(TransactionType.Credit, status, 3_000_000, Vcb);
        var cash = Guid.NewGuid();
        var applied = Applied(deposit);

        var plan = TransferConversion.OneSided(deposit, cash);

        After(applied, plan, Vcb).Should().Be(3_000_000);
        After(applied, plan, cash).Should().Be(-3_000_000);
        (plan.FromAccountId, plan.ToAccountId).Should().Be((cash, Vcb));
    }

    [Fact]
    public void Pair_RejectsTwoSidesThatAreNotADebitAndACredit()
    {
        var a = Side(TransactionType.Debit, TransactionStatus.Draft, 500_000, Vcb);
        var b = Side(TransactionType.Debit, TransactionStatus.Draft, 500_000, Momo);

        var act = () => TransferConversion.Pair(a, b);

        act.Should().Throw<ArgumentException>();
    }
}
