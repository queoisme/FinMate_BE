using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>
/// Thẻ đề xuất hành động do CODE sinh từ kết quả công cụ (không phải LLM viết): mỗi thẻ mang đúng số liệu của một bằng chứng của lượt, nên vẫn qua kiểm chứng số,
/// và chỉ đề xuất khi có lý do (khoản mua có vấn đề, hoặc có kết quả chia ngân sách). Tiền chỉ thay đổi khi người dùng bấm xác nhận ở app.
/// </summary>
public class SuggestedActionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly DateOnly Today = new(2026, 11, 10);

    private static JsonElement Args(object args) => JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement.Clone();

    private static PurchaseCheckDto Check(string verdict, long? maxAffordable, DateOnly? earliestSafe, long price = 1_500_000, string source = "confirmed", string confidence = "high")
        => new(
            "assessable", verdict, "Tóm tắt.", price, Today, null,
            new PurchasePeriodDto(Today, new DateOnly(2026, 11, 14), "next_income"),
            new PurchasePointDto(new DateOnly(2026, 11, 12), 1_000_000), new PurchasePointDto(new DateOnly(2026, 11, 12), -500_000), -900_000,
            1_000_000, 500_000, new PurchaseBudgetImpactDto("unknown", null, null, null, null), new PurchasePlanImpactDto("unknown", null, null),
            maxAffordable, earliestSafe,
            [new PurchaseLineDto(new DateOnly(2026, 11, 12), "Tiền nhà", -3_000_000, source)], [], confidence, Now.AddHours(-2), Now);

    private static Task<ToolResult> Purchase(PurchaseCheckDto dto)
    {
        var purchases = new Mock<IPurchaseCheckService>();
        purchases.Setup(p => p.CheckAsync(It.IsAny<PurchaseCheckCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        return new SimulatePurchaseTool(purchases.Object).RunAsync(new ToolContext(Guid.NewGuid(), Now), Args(new { price_vnd = dto.AmountCents }));
    }

    [Fact]
    public async Task AShortfallSuggestsWaitingForTheSafeDateAndLoweringTheAmountEachWithItsEvidence()
    {
        var result = await Purchase(Check("shortfall_risk", maxAffordable: 800_000, earliestSafe: new DateOnly(2026, 11, 15)));

        var actions = result.Actions!;
        actions.Select(a => a.Kind).Should().Equal(SuggestedActionKinds.WaitUntil, SuggestedActionKinds.LowerAmountTo);
        actions[0].Date.Should().Be("2026-11-15");
        actions[0].Title.Should().Contain("15/11/2026");
        actions[0].EvidenceIds.Should().Equal("earliest_safe_date");
        actions[1].AmountVnd.Should().Be(800_000);
        actions[1].Title.Should().Contain("800.000 đồng");
        actions[1].EvidenceIds.Should().Equal("max_affordable");
        actions.SelectMany(a => a.EvidenceIds).Should().OnlyContain(id => result.Evidence.Any(e => e.Id == id), "thẻ chỉ dựa vào bằng chứng của chính lượt này");
    }

    [Fact]
    public async Task APurchaseThatFitsThePlanWithConfirmedDataSuggestsNothing()
    {
        var result = await Purchase(Check("fits_plan", maxAffordable: 5_000_000, earliestSafe: null));

        result.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task NoCardIsOfferedWhenWaitingOrLoweringChangesNothing()
    {
        // ngày an toàn không sau ngày mua, và mức tối đa không thấp hơn giá: không có gì để hoãn hay giảm
        var result = await Purchase(Check("shortfall_risk", maxAffordable: 1_500_000, earliestSafe: Today));

        result.Actions.Should().BeEmpty();
    }

    private static async Task<ToolResult> PlanBudget()
    {
        var source = new Mock<IFinancialDataSource>();
        source.Setup(s => s.GetCategorySpendAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        source.Setup(s => s.GetMonthlySeriesAsync(It.IsAny<Guid>(), 12, Now, It.IsAny<CancellationToken>())).ReturnsAsync(new MonthlySeries([], []));
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemConfigSnapshot(1, new SystemConfigDocument { Agent = new AgentSettings { BudgetTemplate = """{"an-uong":50,"nha-o":30,"giai-tri":20}""" } }, null));
        return await new PlanBudgetTool(source.Object, config.Object).RunAsync(new ToolContext(Guid.NewGuid(), Now), Args(new { income_vnd = 2_000_000, save_vnd = 1_000_000 }));
    }

    [Fact]
    public async Task ABudgetPlanSuggestsOneLimitPerGroupWithTheAmountOfItsAllocation()
    {
        var result = await PlanBudget();

        var actions = result.Actions!;
        actions.Should().OnlyContain(a => a.Kind == SuggestedActionKinds.SetBudgetLimit);
        actions.Select(a => (a.CategorySlug, a.AmountVnd)).Should().BeEquivalentTo(new[] { ("an-uong", (long?)500_000), ("nha-o", 300_000), ("giai-tri", 200_000) });
        actions.Should().OnlyContain(a => a.Title.Contains("đồng") && a.EvidenceIds.Count == 1);
        foreach (var action in actions)
        {
            result.Evidence.Single(e => e.Id == action.EvidenceIds[0]).ValueVnd.Should().Be(action.AmountVnd);
        }
    }
}
