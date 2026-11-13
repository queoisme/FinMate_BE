using FinMate.Application.Common.Exceptions;
using FinMate.Application.Transactions;
using FinMate.Application.Transactions.Refunds;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>BR-RF-01/02/08 và quyết định D4, D12 của Phase 81: khi nào một khoản chi nhận được hoàn/đảo, và hoàn bao nhiêu.</summary>
public class RefundPolicyTests
{
    private static readonly Category Shopping = new() { Id = Guid.NewGuid(), Slug = "shopping", IsSystem = true };
    private static readonly Category Loan = new() { Id = Guid.NewGuid(), Slug = Category.LoanSlug, IsSystem = true };

    private static Transaction Original(
        long amount = 900_000,
        TransactionType type = TransactionType.Debit,
        TransactionStatus status = TransactionStatus.Confirmed,
        bool reversed = false,
        Category? category = null)
    {
        var cat = category ?? Shopping;
        return new Transaction
        {
            Id = Guid.NewGuid(),
            AmountCents = amount,
            TransactionType = type,
            Status = status,
            IsReversed = reversed,
            CategoryId = cat.Id,
            Category = cat,
        };
    }

    private static string CodeOf(Action act)
        => act.Should().Throw<BusinessRuleException>().Which.ErrorCode;

    [Fact]
    public void Refund_WithinRemaining_IsAllowed()
    {
        var act = () => RefundPolicy.EnsureLinkable(Original(), TransactionRelation.Refund, 300_000, alreadyRefundedCents: 600_000);
        act.Should().NotThrow();
    }

    [Fact]
    public void Refund_BeyondRemaining_IsRejected()
    {
        CodeOf(() => RefundPolicy.EnsureLinkable(Original(), TransactionRelation.Refund, 300_001, alreadyRefundedCents: 600_000))
            .Should().Be(TransactionErrorCodes.RefundExceedsOriginal);
    }

    [Theory]
    [InlineData(TransactionType.Credit, TransactionStatus.Confirmed, false)]
    [InlineData(TransactionType.Transfer, TransactionStatus.Confirmed, false)]
    [InlineData(TransactionType.Debit, TransactionStatus.Draft, false)]
    [InlineData(TransactionType.Debit, TransactionStatus.Confirmed, true)]
    public void IneligibleOriginal_IsRejected(TransactionType type, TransactionStatus status, bool reversed)
    {
        CodeOf(() => RefundPolicy.EnsureLinkable(Original(type: type, status: status, reversed: reversed), TransactionRelation.Refund, 1, 0))
            .Should().Be(TransactionErrorCodes.RefundOriginalNotEligible);
    }

    [Fact]
    public void LoanOriginal_IsRejected()
    {
        CodeOf(() => RefundPolicy.EnsureLinkable(Original(category: Loan), TransactionRelation.Refund, 1, 0))
            .Should().Be(TransactionErrorCodes.RefundOriginalNotEligible);
    }

    [Fact]
    public void Reversal_MustMatchFullAmount()
    {
        CodeOf(() => RefundPolicy.EnsureLinkable(Original(), TransactionRelation.Reversal, 899_999, 0))
            .Should().Be(TransactionErrorCodes.ReversalAmountMismatch);
        var ok = () => RefundPolicy.EnsureLinkable(Original(), TransactionRelation.Reversal, 900_000, 0);
        ok.Should().NotThrow();
    }

    [Fact]
    public void Reversal_OfPartlyRefundedOriginal_IsRejected()
    {
        CodeOf(() => RefundPolicy.EnsureLinkable(Original(), TransactionRelation.Reversal, 900_000, alreadyRefundedCents: 1))
            .Should().Be(TransactionErrorCodes.ReversalAmountMismatch);
    }

    [Fact]
    public void Remaining_IsZeroForReversedOriginal()
    {
        RefundPolicy.RemainingRefundable(Original(reversed: true), 0).Should().Be(0);
        RefundPolicy.RemainingRefundable(Original(), 300_000).Should().Be(600_000);
        RefundPolicy.RemainingRefundable(Original(), 1_000_000).Should().Be(0);
    }

    [Theory]
    [InlineData(TransactionType.Debit)]
    [InlineData(TransactionType.Transfer)]
    public void OnlyCreditCanBecomeRefund(TransactionType type)
    {
        var tx = new Transaction { Id = Guid.NewGuid(), TransactionType = type };
        CodeOf(() => RefundPolicy.EnsureCanCarryRelation(tx, Guid.NewGuid()))
            .Should().Be(TransactionErrorCodes.RefundNotLinkable);
    }

    [Fact]
    public void TransactionCannotRelateToItself()
    {
        var tx = new Transaction { Id = Guid.NewGuid(), TransactionType = TransactionType.Credit };
        CodeOf(() => RefundPolicy.EnsureCanCarryRelation(tx, tx.Id))
            .Should().Be(TransactionErrorCodes.RefundNotLinkable);
    }
}

/// <summary>Chốt chặn ngân sách duy nhất (<see cref="TransactionBudgetDelta.Spend(TransactionType, long, Category?, TransactionRelation?, bool)"/>) với hoàn và đảo.</summary>
public class TransactionBudgetDeltaRefundTests
{
    private static readonly Category Shopping = new() { Slug = "shopping" };
    private static readonly Category Loan = new() { Slug = Category.LoanSlug };

    [Fact]
    public void PlainDebit_Spends()
        => TransactionBudgetDelta.Spend(TransactionType.Debit, 900_000, Shopping).Should().Be(900_000);

    [Fact]
    public void ReversedDebit_SpendsNothing()
        => TransactionBudgetDelta.Spend(TransactionType.Debit, 900_000, Shopping, isReversed: true).Should().Be(0);

    [Fact]
    public void PlainCredit_SpendsNothing()
        => TransactionBudgetDelta.Spend(TransactionType.Credit, 900_000, Shopping).Should().Be(0);

    [Fact]
    public void Refund_GivesBack()
        => TransactionBudgetDelta.Spend(TransactionType.Credit, 300_000, Shopping, TransactionRelation.Refund).Should().Be(-300_000);

    [Fact]
    public void Reversal_HasNoOwnEffect()
        => TransactionBudgetDelta.Spend(TransactionType.Credit, 900_000, Shopping, TransactionRelation.Reversal).Should().Be(0);

    [Fact]
    public void LoanRefund_HasNoEffect()
        => TransactionBudgetDelta.Spend(TransactionType.Credit, 300_000, Loan, TransactionRelation.Refund).Should().Be(0);

    [Fact]
    public void Transfer_SpendsNothing()
        => TransactionBudgetDelta.Spend(TransactionType.Transfer, 900_000, null).Should().Be(0);

    [Fact]
    public void EntityOverload_ReadsRelationAndReversal()
    {
        var refund = new Transaction { TransactionType = TransactionType.Credit, AmountCents = 300_000, Relation = TransactionRelation.Refund };
        TransactionBudgetDelta.Spend(refund, Shopping).Should().Be(-300_000);

        var reversed = new Transaction { TransactionType = TransactionType.Debit, AmountCents = 900_000, IsReversed = true };
        TransactionBudgetDelta.Spend(reversed, Shopping).Should().Be(0);
    }
}
