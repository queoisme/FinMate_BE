using FinMate.Application.Agent;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Sổ dòng tiền theo ngày: ví dụ chuẩn của Flow 6 §6.4 (ngày 10, số dư 5.000.000, tiền nhà 3.000.000 ngày 12, dự trữ 1.000.000, lương 10.000.000 ngày 15, mua quần 1.500.000).</summary>
public class CashFlowLedgerTests
{
    private static DateOnly D(int day) => new(2026, 11, day);

    private static readonly ScheduledItem Rent = new("rent", "Tiền nhà", 3_000_000, D(12), EvidenceSource.Confirmed);
    private static readonly ScheduledItem Salary = new("salary", "Lương", 10_000_000, D(15), EvidenceSource.Confirmed);

    private static CashFlowProjection Project(HypotheticalPurchase? purchase, IEnumerable<ScheduledItem>? obligations = null, DateOnly? to = null)
        => CashFlowLedger.Project(5_000_000, [Salary], obligations ?? [Rent], 1_000_000, purchase, D(10), to ?? D(14));

    [Fact]
    public void before_the_purchase_the_lowest_point_before_payday_is_1_000_000()
    {
        var projection = Project(null);

        projection.MinimumBalanceVnd.Should().Be(1_000_000);
        projection.MinimumDate.Should().Be(D(12));
    }

    [Fact]
    public void buying_the_trousers_on_day_10_drops_the_lowest_point_to_minus_500_000_and_the_negative_is_kept()
    {
        var projection = Project(new HypotheticalPurchase(1_500_000, D(10)));

        projection.MinimumBalanceVnd.Should().Be(-500_000);
        projection.MinimumDate.Should().Be(D(12));
        projection.Days.Select(d => d.BalanceVnd).Should().Equal(2_500_000, 2_500_000, -500_000, -500_000, -500_000);
    }

    [Fact]
    public void buying_on_day_16_gives_a_different_answer_even_though_the_months_total_is_the_same()
    {
        var through = D(20);

        var before = Project(null, to: through);
        var after = Project(new HypotheticalPurchase(1_500_000, D(16)), to: through);

        before.MinimumBalanceVnd.Should().Be(1_000_000);
        after.MinimumBalanceVnd.Should().Be(1_000_000); // đáy ở ngày 12-14 trước khi mua, lương ngày 15 nâng số dư lên
        after.Days.Single(d => d.Date == D(16)).BalanceVnd.Should().Be(9_500_000);
        after.Days.Single(d => d.Date == D(15)).BalanceVnd.Should().Be(11_000_000);
    }

    [Fact]
    public void paid_obligation_is_not_deducted_twice()
    {
        var paid = Rent with { Settled = true }; // đã có giao dịch trả tiền nhà: số dư mở đầu đã phản ánh

        Project(new HypotheticalPurchase(1_500_000, D(10)), [paid]).MinimumBalanceVnd.Should().Be(2_500_000);
    }

    [Fact]
    public void items_outside_the_window_do_not_count_but_a_purchase_after_the_window_does_nothing()
    {
        var yesterday = Rent with { Id = "old", Date = D(9) }; // đã trôi qua trước kỳ tính: nằm trong số dư mở đầu
        var nextMonth = Rent with { Id = "later", Date = new DateOnly(2026, 12, 12) };

        Project(new HypotheticalPurchase(9_000_000, D(15)), [yesterday, nextMonth]).MinimumBalanceVnd.Should().Be(4_000_000);
    }

    [Fact]
    public void an_end_before_the_start_is_rejected()
    {
        var act = () => CashFlowLedger.Project(0, [], [], 0, null, D(10), D(9));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void a_single_day_window_works_and_money_stays_in_exact_integers()
    {
        var projection = CashFlowLedger.Project(long.MaxValue / 4, [], [], 0, new HypotheticalPurchase(1, D(10)), D(10), D(10));

        projection.Days.Should().ContainSingle().Which.BalanceVnd.Should().Be(long.MaxValue / 4 - 1);
    }
}
