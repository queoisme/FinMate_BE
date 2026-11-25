using FinMate.Application.Common.Exceptions;
using FinMate.Application.Loans;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Loans;

/// <summary>Luật thuần của sổ vay (Phase 90, chép từ bài test chính sách nợ của Phase 86 cũ): chiều giao dịch (BR-DEBT-05), điều kiện gắn (BR-DEBT-08), số còn lại và trạng thái (BR-DEBT-07, D5, D6, D16).</summary>
public class LoanPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(7));

    private static Loan NewLoan(LoanDirection direction = LoanDirection.Lend, long principal = 2_000_000, long repaid = 0)
        => new() { Id = Guid.NewGuid(), Direction = direction, PrincipalCents = principal, RepaidCents = repaid, Status = LoanStatus.Open };

    private static Transaction Eligible(TransactionType type = TransactionType.Credit) => new()
    {
        Id = Guid.NewGuid(), TransactionType = type, Status = TransactionStatus.Confirmed, AmountCents = 500_000,
    };

    [Theory]
    [InlineData(LoanDirection.Lend, LoanEntryKind.Principal, TransactionType.Debit)]
    [InlineData(LoanDirection.Lend, LoanEntryKind.Repayment, TransactionType.Credit)]
    [InlineData(LoanDirection.Borrow, LoanEntryKind.Principal, TransactionType.Credit)]
    [InlineData(LoanDirection.Borrow, LoanEntryKind.Repayment, TransactionType.Debit)]
    public void Direction_FollowsTheTable(LoanDirection direction, LoanEntryKind kind, TransactionType expected)
    {
        LoanPolicy.TransactionTypeFor(direction, kind).Should().Be(expected);
        FluentActions.Invoking(() => LoanPolicy.EnsureDirection(direction, kind, expected)).Should().NotThrow();

        var wrong = expected == TransactionType.Debit ? TransactionType.Credit : TransactionType.Debit;
        FluentActions.Invoking(() => LoanPolicy.EnsureDirection(direction, kind, wrong))
            .Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(LoanErrorCodes.DirectionMismatch);
    }

    public static TheoryData<string, Action<Transaction>> NotEligible => new()
    {
        { "transfer", t => t.TransactionType = TransactionType.Transfer },
        { "relation", t => t.Relation = TransactionRelation.Refund },
        { "merged", t => t.MergedIntoTransactionId = Guid.NewGuid() },
        { "suspected", t => t.DuplicateStatus = DuplicateStatus.Suspected },
        { "shared", t => t.GroupId = Guid.NewGuid() },
        { "group fund", t => t.GroupFundEntryId = Guid.NewGuid() },
        { "draft (D4)", t => t.Status = TransactionStatus.Draft },
        { "deleted", t => t.DeletedAt = Now },
    };

    [Theory]
    [MemberData(nameof(NotEligible))]
    public void IneligibleTransactions_AreRejected(string why, Action<Transaction> make)
    {
        var transaction = Eligible();
        make(transaction);

        var act = () => LoanPolicy.EnsureEligible(transaction, alreadyLinked: false);

        act.Should().Throw<BusinessRuleException>(why).Which.ErrorCode.Should().Be(LoanErrorCodes.TransactionNotEligible);
    }

    [Fact]
    public void ATransactionLinkedElsewhere_IsRejected_AndAPlainOneIsAccepted()
    {
        var transaction = Eligible();
        transaction.DuplicateStatus = DuplicateStatus.Dismissed;   // nghi trùng đã bác thì không cản

        FluentActions.Invoking(() => LoanPolicy.EnsureEligible(transaction, alreadyLinked: false)).Should().NotThrow();
        FluentActions.Invoking(() => LoanPolicy.EnsureEligible(transaction, alreadyLinked: true))
            .Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(LoanErrorCodes.TransactionNotEligible);
    }

    [Fact]
    public void Repaying_MoreThanOutstanding_IsOverpaid_AndExactlyOutstanding_Settles()
    {
        var debt = NewLoan(principal: 2_000_000, repaid: 1_500_000);

        FluentActions.Invoking(() => LoanPolicy.Add(debt, LoanEntryKind.Repayment, 600_000, Now))
            .Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(LoanErrorCodes.Overpay);
        debt.RepaidCents.Should().Be(1_500_000, "không đổi gì khi bị từ chối");

        LoanPolicy.Add(debt, LoanEntryKind.Repayment, 500_000, Now);

        (debt.PrincipalCents - debt.RepaidCents).Should().Be(0);
        debt.Status.Should().Be(LoanStatus.Settled);
        debt.SettledAt.Should().Be(Now);
    }

    [Fact]
    public void RemovingARepayment_FromASettledLoan_ReopensIt()
    {
        var debt = NewLoan(principal: 2_000_000, repaid: 2_000_000);
        debt.Status = LoanStatus.Settled;
        debt.SettledAt = Now.AddDays(-1);

        var emptied = LoanPolicy.Remove(debt, LoanEntryKind.Repayment, 500_000, Now);

        emptied.Should().BeFalse();
        debt.Status.Should().Be(LoanStatus.Open);
        debt.SettledAt.Should().BeNull();
        (debt.PrincipalCents - debt.RepaidCents).Should().Be(500_000);
    }

    [Fact]
    public void RemovingPrincipal_BelowWhatWasRepaid_HasPayments()
    {
        var debt = NewLoan(principal: 2_000_000, repaid: 500_000);

        FluentActions.Invoking(() => LoanPolicy.Remove(debt, LoanEntryKind.Principal, 2_000_000, Now))
            .Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(LoanErrorCodes.PrincipalBelowPaid);
        debt.PrincipalCents.Should().Be(2_000_000);
    }

    [Fact]
    public void RemovingTheLastPrincipal_WithNoPayments_EmptiesTheLoan()
    {
        var debt = NewLoan(principal: 2_000_000);

        LoanPolicy.Remove(debt, LoanEntryKind.Principal, 2_000_000, Now).Should().BeTrue("D5: khoản nợ 0đ bị xoá mềm theo");
        debt.Status.Should().Be(LoanStatus.Open, "một khoản rỗng không bao giờ được coi là đã trả xong");
    }

    [Fact]
    public void LendingMore_OnASettledLoan_ReopensIt()
    {
        var debt = NewLoan(principal: 1_000_000, repaid: 1_000_000);
        debt.Status = LoanStatus.Settled;
        debt.SettledAt = Now.AddDays(-3);

        LoanPolicy.Add(debt, LoanEntryKind.Principal, 300_000, Now);

        debt.Status.Should().Be(LoanStatus.Open);
        debt.SettledAt.Should().BeNull();
        (debt.PrincipalCents - debt.RepaidCents).Should().Be(300_000);
    }

    [Theory]
    [InlineData(400_000, LoanStatus.Open)]
    [InlineData(1_000_000, LoanStatus.Settled)]
    public void ARepayment_OnAWrittenOffLoan_ReopensOrSettlesIt(long amount, LoanStatus expected)
    {
        var debt = NewLoan(principal: 1_000_000);
        LoanPolicy.WriteOff(debt, Now.AddDays(-1));

        LoanPolicy.Add(debt, LoanEntryKind.Repayment, amount, Now);

        debt.Status.Should().Be(expected);
        debt.WrittenOffAt.Should().BeNull();
    }

    [Fact]
    public void WriteOff_KeepsTheAmounts_IsIdempotent_AndRefusesASettledLoan()
    {
        var debt = NewLoan(principal: 1_000_000, repaid: 200_000);

        LoanPolicy.WriteOff(debt, Now);
        LoanPolicy.WriteOff(debt, Now.AddHours(1));

        debt.Status.Should().Be(LoanStatus.WrittenOff);
        debt.WrittenOffAt.Should().Be(Now, "lần hai không đổi gì");
        (debt.PrincipalCents - debt.RepaidCents).Should().Be(800_000, "D16: giữ nguyên số liệu");

        var settled = NewLoan(principal: 1_000_000, repaid: 1_000_000);
        settled.Status = LoanStatus.Settled;
        settled.SettledAt = Now;
        FluentActions.Invoking(() => LoanPolicy.WriteOff(settled, Now))
            .Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(LoanErrorCodes.AlreadySettled);
    }

    [Fact]
    public void ChangingALinkedAmount_IsCheckedLikeRemoveThenAdd()
    {
        var debt = NewLoan(principal: 2_000_000, repaid: 1_500_000);

        FluentActions.Invoking(() => LoanPolicy.ChangeAmount(debt, LoanEntryKind.Repayment, 500_000, 1_100_000, Now))
            .Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(LoanErrorCodes.Overpay);
        debt.RepaidCents.Should().Be(1_500_000);

        LoanPolicy.ChangeAmount(debt, LoanEntryKind.Repayment, 500_000, 1_000_000, Now);
        debt.Status.Should().Be(LoanStatus.Settled);

        FluentActions.Invoking(() => LoanPolicy.ChangeAmount(debt, LoanEntryKind.Principal, 2_000_000, 1_000_000, Now))
            .Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(LoanErrorCodes.PrincipalBelowPaid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveAmounts_AreInvalid(long amount)
    {
        FluentActions.Invoking(() => LoanPolicy.Add(NewLoan(), LoanEntryKind.Principal, amount, Now)).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- S1: trả kèm lãi ----

    private static Loan WithInterest(long principal = 2_000_000, long repaid = 0, decimal rate = 12)
        => new() { Id = Guid.NewGuid(), Direction = LoanDirection.Borrow, PrincipalCents = principal, RepaidCents = repaid, InterestRatePercentYear = rate };

    [Fact]
    public void Split_ScenarioI_2MillionAt12Percent_After90Days_Pays2060000()
    {
        var loan = WithInterest();
        var accrued = (long)Math.Floor(2_000_000m * 0.12m / 365m * 90);   // 59.178

        LoanPolicy.SplitRepayment(loan, 2_060_000, accrued, toleranceCents: 10_000).Should().Be((2_000_000L, 60_000L));
    }

    [Theory]
    [InlineData(1_500_000)]
    [InlineData(2_000_000)]
    public void Split_WithinTheRemaining_IsAllPrincipal(long amount)
        => LoanPolicy.SplitRepayment(WithInterest(), amount, 50_000, 10_000).Should().Be((amount, 0L));

    [Fact]
    public void Split_ExcessAboveAccruedPlusTolerance_IsOverpaid()
    {
        var act = () => LoanPolicy.SplitRepayment(WithInterest(), 2_060_001, 50_000, 10_000);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(LoanErrorCodes.Overpay);
    }

    [Fact]
    public void Split_WithoutAnInterestRate_AnyExcessIsOverpaid()
    {
        var act = () => LoanPolicy.SplitRepayment(WithInterest(rate: 0), 2_000_001, 0, 10_000);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(LoanErrorCodes.Overpay);
    }

    [Fact]
    public void Split_OnASettledLoan_IsOverpaid_InterestAloneIsNotARepayment()
    {
        var act = () => LoanPolicy.SplitRepayment(WithInterest(repaid: 2_000_000), 5_000, 50_000, 10_000);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(LoanErrorCodes.Overpay);
    }
}
