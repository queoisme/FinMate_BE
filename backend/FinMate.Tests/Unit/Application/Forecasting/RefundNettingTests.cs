using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>Phase 81, BR-RF-07/08, D14: dữ liệu thói quen mà mọi bộ tính dự báo nhận.</summary>
public class RefundNettingTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid Shopping = Guid.NewGuid();

    private static ForecastTransaction Tx(long amount, TransactionType type, TransactionRelation? relation = null, Guid? related = null, bool reversed = false, Guid? id = null)
        => new(id ?? Guid.NewGuid(), Guid.NewGuid(), Shopping, amount, type, Day, DateOnly.FromDateTime(Day.Date), "Shopee", "shopee", false, relation, related, reversed);

    [Fact]
    public void FullyRefundedPurchase_AndItsRefunds_Disappear()
    {
        var shoes = Tx(900_000, TransactionType.Debit);
        var rows = new[] { shoes, Tx(600_000, TransactionType.Credit, TransactionRelation.Refund, shoes.Id), Tx(300_000, TransactionType.Credit, TransactionRelation.Refund, shoes.Id) };

        RefundNetting.ForHabits(rows).Should().BeEmpty();
    }

    [Fact]
    public void PartiallyRefundedPurchase_KeepsTheNet()
    {
        var shoes = Tx(900_000, TransactionType.Debit);
        var result = RefundNetting.ForHabits([shoes, Tx(300_000, TransactionType.Credit, TransactionRelation.Refund, shoes.Id)]);

        result.Should().ContainSingle().Which.AmountCents.Should().Be(600_000);
        result[0].Id.Should().Be(shoes.Id);
    }

    [Fact]
    public void ReversedPair_Disappears()
    {
        var wrong = Tx(500_000, TransactionType.Debit, reversed: true);
        RefundNetting.ForHabits([wrong, Tx(500_000, TransactionType.Credit, TransactionRelation.Reversal, wrong.Id)]).Should().BeEmpty();
    }

    [Fact]
    public void RefundsAreNeverIncome_OrphansIncluded_ButSalaryStays()
    {
        var salary = Tx(10_000_000, TransactionType.Credit);
        var result = RefundNetting.ForHabits([salary, Tx(200_000, TransactionType.Credit, TransactionRelation.Refund)]);

        result.Should().ContainSingle().Which.Should().Be(salary);
    }

    [Fact]
    public void RefundWhoseOriginalIsOutsideTheWindow_IsDropped()
    {
        RefundNetting.ForHabits([Tx(200_000, TransactionType.Credit, TransactionRelation.Refund, Guid.NewGuid())]).Should().BeEmpty();
    }

    [Fact]
    public void FeedingTheBaseline_ARefundedPurchaseDoesNotRaiseIt()
    {
        // Thói quen chi chỉ thấy khoản ròng: 900k hoàn đủ không đẩy trung vị lên.
        var shoes = Tx(900_000, TransactionType.Debit);
        var habits = RefundNetting.ForHabits([shoes, Tx(900_000, TransactionType.Credit, TransactionRelation.Refund, shoes.Id)]);
        habits.Where(t => t.Type == TransactionType.Debit).Sum(t => t.AmountCents).Should().Be(0);
    }
}
