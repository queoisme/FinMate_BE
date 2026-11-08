using System.Text.Json;
using FinMate.Application.Agent;
using FinMate.Application.Forecasting;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>
/// Công cụ mô phỏng khoản mua bọc <see cref="IPurchaseCheckService"/> (engine dự báo của Phase 54–55). Ví dụ chuẩn của Flow 6 §6.4: hôm nay ngày 10, mua 1.500.000 thì điểm thấp nhất đổi từ 1.000.000 thành −500.000.
/// Ở đây engine là bản giả: kiểm cách công cụ đổi kết quả thành bằng chứng có nguồn, độ tin cậy và kết luận, không kiểm lại phép tính của engine (đã có test ở <c>Forecasting</c>).
/// </summary>
public class SimulatePurchaseToolTests
{
    private static readonly DateTimeOffset Day10 = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly DateOnly D10 = new(2026, 11, 10);

    private static JsonElement Args(object args) => JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement.Clone();

    private static PurchaseCheckDto Check(string status = "assessable", string verdict = "shortfall_risk", long before = 1_000_000, long after = -500_000, string source = "confirmed", string confidence = "high")
        => new(
            status, verdict, "Có nguy cơ thiếu tiền trước ngày nhận lương.", 1_500_000, D10, "quần",
            new PurchasePeriodDto(D10, new DateOnly(2026, 11, 14), "next_income"),
            new PurchasePointDto(new DateOnly(2026, 11, 12), before), new PurchasePointDto(new DateOnly(2026, 11, 12), after), -900_000,
            1_000_000, after < 0 ? -after : 0, new PurchaseBudgetImpactDto("unknown", null, null, null, null), new PurchasePlanImpactDto("unknown", null, null),
            800_000, new DateOnly(2026, 11, 15),
            [new PurchaseLineDto(new DateOnly(2026, 11, 12), "Tiền nhà", -3_000_000, source), new PurchaseLineDto(new DateOnly(2026, 11, 15), "Lương", 10_000_000, source)],
            [new PurchaseWarningDto("already_short", "Số dư đã thấp hơn vùng đệm.")],
            confidence, Day10.AddHours(-2), Day10);

    private static async Task<(ToolResult Result, PurchaseCheckCommand? Sent)> RunAsync(PurchaseCheckDto dto, object? args = null)
    {
        PurchaseCheckCommand? sent = null;
        var purchases = new Mock<IPurchaseCheckService>();
        purchases.Setup(p => p.CheckAsync(It.IsAny<PurchaseCheckCommand>(), It.IsAny<CancellationToken>()))
            .Callback<PurchaseCheckCommand, CancellationToken>((c, _) => sent = c)
            .ReturnsAsync(dto);
        var result = await new SimulatePurchaseTool(purchases.Object).RunAsync(new ToolContext(Guid.NewGuid(), Day10), Args(args ?? new { price_vnd = 1_500_000 }));
        return (result, sent);
    }

    private static long Value(IReadOnlyList<Evidence> evidence, string id) => evidence.Single(e => e.Id == id).ValueVnd!.Value;

    [Fact]
    public async Task buying_the_trousers_turns_the_lowest_point_from_1_000_000_into_minus_500_000_and_the_verdict_is_infeasible()
    {
        var (result, _) = await RunAsync(Check());

        result.Status.Should().Be(ToolStatus.Ok);
        Value(result.Evidence, "min_before").Should().Be(1_000_000);
        Value(result.Evidence, "min_after").Should().Be(-500_000);
        Value(result.Evidence, "shortfall").Should().Be(500_000);
        Value(result.Evidence, "price").Should().Be(1_500_000);
        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: infeasible");
    }

    [Fact]
    public async Task the_command_carries_the_asking_user_the_price_and_the_purchase_date_and_never_a_user_id_argument()
    {
        var userId = Guid.NewGuid();
        PurchaseCheckCommand? sent = null;
        var purchases = new Mock<IPurchaseCheckService>();
        purchases.Setup(p => p.CheckAsync(It.IsAny<PurchaseCheckCommand>(), It.IsAny<CancellationToken>())).Callback<PurchaseCheckCommand, CancellationToken>((c, _) => sent = c).ReturnsAsync(Check());

        await new SimulatePurchaseTool(purchases.Object).RunAsync(new ToolContext(userId, Day10), Args(new { price_vnd = 1_500_000, purchase_in_days = 6, window_days = 14 }));

        sent.Should().NotBeNull();
        sent!.UserId.Should().Be(userId); // userId của lượt hỏi (từ JWT), không phải do LLM đưa vào
        sent.AmountCents.Should().Be(1_500_000);
        sent.PurchaseDate.Should().Be(new DateOnly(2026, 11, 16));
        sent.HorizonDays.Should().Be(14);
    }

    [Fact]
    public async Task estimated_lines_make_the_derived_figures_estimated_and_cap_a_fitting_purchase_at_tight()
    {
        var (result, _) = await RunAsync(Check(verdict: "fits_plan", before: 4_000_000, after: 2_500_000, source: "estimated", confidence: "medium"));

        result.Evidence.Single(e => e.Id == "min_after").Source.Should().Be(EvidenceSource.Estimated);
        result.Evidence.Where(e => e.Key.StartsWith("scheduled_")).Should().OnlyContain(e => e.Source == EvidenceSource.Estimated);
        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: tight");
    }

    [Fact]
    public async Task confirmed_everything_and_a_fitting_purchase_is_feasible_and_has_no_shortfall_evidence()
    {
        var (result, _) = await RunAsync(Check(verdict: "fits_plan", before: 4_000_000, after: 2_500_000));

        result.Evidence.Should().NotContain(e => e.Id == "shortfall");
        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: feasible");
        result.Evidence.Single(e => e.Id == "min_after").Source.Should().Be(EvidenceSource.Confirmed);
    }

    [Theory]
    [InlineData("consider")]
    [InlineData("off_plan")]
    public async Task consider_and_off_plan_both_read_as_tight(string verdict)
    {
        var (result, _) = await RunAsync(Check(verdict: verdict, after: 200_000));

        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: tight");
    }

    [Fact]
    public async Task scheduled_lines_become_evidence_with_a_positive_amount_a_direction_a_date_and_their_own_source()
    {
        var (result, _) = await RunAsync(Check());

        var lines = result.Evidence.Where(e => e.Id.StartsWith("scheduled_")).ToList();
        lines.Should().HaveCount(2);
        lines[0].Should().Match<Evidence>(e => e.ValueVnd == 3_000_000 && e.Key == "scheduled_obligation" && e.Text!.Contains("Tiền nhà") && e.Text.Contains("12/11") && e.Source == EvidenceSource.Confirmed);
        lines[1].Should().Match<Evidence>(e => e.ValueVnd == 10_000_000 && e.Key == "scheduled_inflow");
    }

    [Fact]
    public async Task insufficient_data_from_the_engine_is_reported_as_insufficient_never_guessed()
    {
        var dto = Check(status: "insufficient_data", verdict: "insufficient_data") with { LowestBefore = null, LowestAfter = null };

        var (result, _) = await RunAsync(dto);

        result.Status.Should().Be(ToolStatus.InsufficientData);
        result.Code.Should().Be("purchase_data_missing");
    }

    [Fact]
    public async Task every_money_figure_is_an_integer_with_a_confidence_and_evidence_text_fits_the_wire_limit()
    {
        var (result, _) = await RunAsync(Check() with { Warnings = [new PurchaseWarningDto("low_margin", new string('x', 900))] });

        result.Evidence.Where(e => e.Unit == "VND").Should().OnlyContain(e => e.ValueVnd != null && e.Confidence > 0 && e.Confidence <= 1);
        result.Evidence.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        result.Evidence.Where(e => e.Text is not null).Should().OnlyContain(e => e.Text!.Length <= 500);
    }

    [Fact]
    public void the_spec_bounds_every_argument_and_only_the_price_is_required()
    {
        var spec = new SimulatePurchaseTool(Mock.Of<IPurchaseCheckService>()).Spec;

        spec.Name.Should().Be("simulate_purchase");
        spec.Args.Where(a => a.Required).Select(a => a.Name).Should().Equal("price_vnd");
        spec.Args.Should().OnlyContain(a => a.Type == "int" && a.Max > a.Min);
    }

    [Fact]
    public async Task the_executor_rejects_an_unknown_argument_and_an_out_of_range_day()
    {
        var executor = new ToolExecutor(new ToolRegistry([new SimulatePurchaseTool(Mock.Of<IPurchaseCheckService>())]));
        var context = new ToolContext(Guid.NewGuid(), Day10);

        (await executor.ExecuteAsync(context, "simulate_purchase", Args(new { price_vnd = 1000, user_id = Guid.NewGuid() }))).Status.Should().Be(ToolStatus.Rejected);
        (await executor.ExecuteAsync(context, "simulate_purchase", Args(new { price_vnd = 1000, purchase_in_days = 91 }))).Status.Should().Be(ToolStatus.Rejected);
        (await executor.ExecuteAsync(context, "simulate_purchase", Args(new { price_vnd = 0 }))).Status.Should().Be(ToolStatus.Rejected);
    }
}
