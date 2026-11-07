using System.Text.Json;
using FinMate.Application.Agent;
using FinMate.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Công cụ mô phỏng khoản mua, theo ví dụ chuẩn của Flow 6 §6.4: hôm nay ngày 10, số dư 5.000.000, tiền nhà 3.000.000 ngày 12, lương 10.000.000 ngày 15, dự trữ 1.000.000.</summary>
public class SimulatePurchaseToolTests
{
    private static readonly DateTimeOffset Day10 = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(7));

    private static RecurringEntry Rent(EvidenceSource source = EvidenceSource.Confirmed, bool paid = false) => new("Tiền nhà", 3_000_000, RecurringKind.Obligation, 12, source, paid);

    private static RecurringEntry Salary(EvidenceSource source = EvidenceSource.Confirmed) => new("Lương", 10_000_000, RecurringKind.Inflow, 15, source, false);

    private static JsonElement Args(object args) => JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement.Clone();

    private static async Task<IReadOnlyList<Evidence>> RunAsync(object args, params RecurringEntry[] entries)
    {
        var source = new InMemoryFinancialDataSource([], [], 5_000_000, null, entries);
        var result = await new SimulatePurchaseTool(source).RunAsync(new ToolContext(Guid.NewGuid(), Day10), Args(args));
        result.Status.Should().Be(ToolStatus.Ok);
        return result.Evidence;
    }

    private static long Value(IReadOnlyList<Evidence> evidence, string id) => evidence.Single(e => e.Id == id).ValueVnd!.Value;

    [Fact]
    public async Task buying_the_trousers_today_turns_the_lowest_point_from_1_000_000_into_minus_500_000()
    {
        var evidence = await RunAsync(new { price_vnd = 1_500_000, reserve_vnd = 1_000_000 }, Rent(), Salary());

        Value(evidence, "min_before").Should().Be(1_000_000);
        Value(evidence, "min_after").Should().Be(-500_000);
        Value(evidence, "shortfall").Should().Be(500_000);
        Value(evidence, "balance").Should().Be(5_000_000);
        evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: infeasible");
    }

    [Fact]
    public async Task buying_after_payday_is_outside_the_window_and_changes_nothing_before_it()
    {
        var evidence = await RunAsync(new { price_vnd = 1_500_000, reserve_vnd = 1_000_000, purchase_in_days = 6 }, Rent(), Salary());

        Value(evidence, "min_after").Should().Be(Value(evidence, "min_before")).And.Be(1_000_000);
        evidence.Should().NotContain(e => e.Id == "shortfall");
        evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: feasible");
    }

    [Fact]
    public async Task paid_obligation_is_not_deducted_twice()
    {
        var evidence = await RunAsync(new { price_vnd = 1_500_000, reserve_vnd = 1_000_000 }, Rent(paid: true), Salary());

        Value(evidence, "min_after").Should().Be(2_500_000);
        evidence.Should().NotContain(e => e.Key == "scheduled_obligation"); // khoản đã thanh toán không xuất hiện trong bảng "vì sao"
    }

    [Fact]
    public async Task unconfirmed_recurring_is_estimated_source_and_caps_the_verdict_at_tight()
    {
        var evidence = await RunAsync(new { price_vnd = 500_000 }, Rent(EvidenceSource.Estimated), Salary());

        evidence.Where(e => e.Key.StartsWith("scheduled_")).Should().Contain(e => e.Key == "scheduled_obligation" && e.Source == EvidenceSource.Estimated);
        evidence.Single(e => e.Id == "min_after").Source.Should().Be(EvidenceSource.Estimated);
        evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: tight");
    }

    [Fact]
    public async Task with_no_confirmed_recurring_item_the_answer_is_never_feasible()
    {
        var evidence = await RunAsync(new { price_vnd = 500_000 });

        evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: tight").And.Contain("chưa có khoản định kỳ nào được xác nhận");
        Value(evidence, "min_after").Should().Be(4_500_000);
    }

    [Fact]
    public async Task without_a_confirmed_payday_the_window_is_window_days_long()
    {
        // lương CHƯA xác nhận nên không dùng làm mốc kết thúc; kỳ tính là 7 ngày: 10/11 → 17/11, gồm cả ngày 15
        var evidence = await RunAsync(new { price_vnd = 1_000_000, window_days = 7 }, Rent(), Salary(EvidenceSource.Estimated));

        evidence.Single(e => e.Id == "min_before").Text.Should().Contain("10/11 đến 17/11");
        Value(evidence, "min_after").Should().Be(1_000_000); // 5.000.000 − 3.000.000 (ngày 12) − 1.000.000 (mua) — đáy trước khi lương về
    }

    [Fact]
    public async Task every_money_figure_is_an_integer_with_a_source_and_a_confidence()
    {
        var evidence = await RunAsync(new { price_vnd = 1_500_000, reserve_vnd = 1_000_000 }, Rent(), Salary());

        evidence.Where(e => e.Unit == "VND").Should().OnlyContain(e => e.ValueVnd != null && e.Confidence > 0 && e.Confidence <= 1);
        evidence.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        evidence.Single(e => e.Id == "price").Source.Should().Be(EvidenceSource.UserEntered);
        evidence.Single(e => e.Id == "balance").Source.Should().Be(EvidenceSource.Confirmed);
    }

    [Fact]
    public void the_spec_bounds_every_argument_and_only_the_price_is_required()
    {
        var spec = new SimulatePurchaseTool(new InMemoryFinancialDataSource([], [], 0)).Spec;

        spec.Name.Should().Be("simulate_purchase");
        spec.Args.Where(a => a.Required).Select(a => a.Name).Should().Equal("price_vnd");
        spec.Args.Should().OnlyContain(a => a.Type == "int" && a.Max > a.Min);
    }

    [Fact]
    public async Task the_executor_rejects_an_unknown_argument_and_an_out_of_range_day()
    {
        var executor = new ToolExecutor(new ToolRegistry([new SimulatePurchaseTool(new InMemoryFinancialDataSource([], [], 0))]));
        var context = new ToolContext(Guid.NewGuid(), Day10);

        (await executor.ExecuteAsync(context, "simulate_purchase", Args(new { price_vnd = 1000, user_id = Guid.NewGuid() }))).Status.Should().Be(ToolStatus.Rejected);
        (await executor.ExecuteAsync(context, "simulate_purchase", Args(new { price_vnd = 1000, purchase_in_days = 91 }))).Status.Should().Be(ToolStatus.Rejected);
        (await executor.ExecuteAsync(context, "simulate_purchase", Args(new { price_vnd = 0 }))).Status.Should().Be(ToolStatus.Rejected);
    }
}
