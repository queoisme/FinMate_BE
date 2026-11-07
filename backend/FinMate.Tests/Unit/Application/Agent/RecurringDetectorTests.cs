using FinMate.Application.Agent;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

public class RecurringDetectorTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    private static Transaction Tx(string name, long amount, int month, int day, TransactionType type = TransactionType.Debit, TransactionStatus status = TransactionStatus.Confirmed, bool deleted = false)
        => new()
        {
            Id = Guid.NewGuid(),
            MerchantName = name,
            AmountCents = amount,
            TransactionType = type,
            Status = status,
            TransactedAt = new DateTimeOffset(2026, month, day, 9, 0, 0, Vn),
            DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
        };

    private static List<Transaction> Monthly(string name, long amount, int day, int[] months, TransactionType type = TransactionType.Debit)
        => months.Select(m => Tx(name, amount, m, day, type)).ToList();

    [Fact]
    public void a_steady_monthly_payment_is_proposed_with_the_median_amount_and_day()
    {
        var found = RecurringDetector.Detect(Monthly("Tiền nhà", 3_000_000, 12, [8, 9, 10, 11]));

        found.Should().ContainSingle().Which.Should().Be(new RecurringCandidate("Tiền nhà", 3_000_000, RecurringKind.Obligation, 12, 4));
    }

    [Fact]
    public void a_steady_monthly_credit_is_an_inflow()
    {
        var found = RecurringDetector.Detect(Monthly("Công ty ABC", 10_000_000, 15, [9, 10, 11], TransactionType.Credit));

        found.Should().ContainSingle().Which.Kind.Should().Be(RecurringKind.Inflow);
    }

    [Fact]
    public void detector_ignores_one_off_charges()
    {
        var oneOff = new List<Transaction> { Tx("Mua laptop", 20_000_000, 10, 3) };
        var twice = Monthly("Sửa xe", 800_000, 5, [9, 10]); // chỉ hai tháng: chưa đủ để gọi là định kỳ

        RecurringDetector.Detect([.. oneOff, .. twice]).Should().BeEmpty();
    }

    [Fact]
    public void several_payments_in_the_same_month_count_as_one_month()
    {
        var burst = new List<Transaction> { Tx("Cà phê", 45_000, 11, 1), Tx("Cà phê", 45_000, 11, 2), Tx("Cà phê", 45_000, 11, 3), Tx("Cà phê", 45_000, 11, 4) };

        RecurringDetector.Detect(burst).Should().BeEmpty();
    }

    [Fact]
    public void an_erratic_amount_or_day_is_not_recurring()
    {
        var wobblyAmount = new List<Transaction> { Tx("Điện", 400_000, 9, 10), Tx("Điện", 900_000, 10, 10), Tx("Điện", 450_000, 11, 10) };
        var wobblyDay = new List<Transaction> { Tx("Internet", 250_000, 9, 2), Tx("Internet", 250_000, 10, 15), Tx("Internet", 250_000, 11, 27) };

        RecurringDetector.Detect([.. wobblyAmount, .. wobblyDay]).Should().BeEmpty();
    }

    [Fact]
    public void amounts_within_the_tolerance_still_count_as_the_same_bill()
    {
        var bills = new List<Transaction> { Tx("Điện", 400_000, 9, 10), Tx("Điện", 430_000, 10, 11), Tx("Điện", 380_000, 11, 9) };

        RecurringDetector.Detect(bills).Should().ContainSingle().Which.AmountVnd.Should().Be(400_000);
    }

    [Fact]
    public void transfer_is_not_spending()
    {
        var transfers = Monthly("Chuyển sang ví tiết kiệm", 2_000_000, 1, [9, 10, 11], TransactionType.Transfer);

        RecurringDetector.Detect(transfers).Should().BeEmpty();
    }

    [Fact]
    public void drafts_and_deleted_transactions_are_ignored()
    {
        var all = new List<Transaction>
        {
            Tx("Gym", 500_000, 9, 5, status: TransactionStatus.Draft),
            Tx("Gym", 500_000, 10, 5),
            Tx("Gym", 500_000, 11, 5, deleted: true),
            Tx("Gym", 500_000, 8, 5),
        };

        RecurringDetector.Detect(all).Should().BeEmpty(); // chỉ còn hai tháng hợp lệ
    }

    [Fact]
    public void the_same_name_in_different_case_and_spacing_is_one_series_and_credit_and_debit_are_separate()
    {
        var mixed = new List<Transaction> { Tx("tiền  NHÀ", 3_000_000, 9, 12), Tx("Tiền nhà", 3_000_000, 10, 12), Tx("Tiền nhà ", 3_000_000, 11, 12) };
        var refund = Monthly("Tiền nhà", 3_000_000, 12, [9, 10], TransactionType.Credit);

        var found = RecurringDetector.Detect([.. mixed, .. refund]);

        found.Should().ContainSingle().Which.Kind.Should().Be(RecurringKind.Obligation);
    }
}
