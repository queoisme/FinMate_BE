using System.Text.Json;
using FinMate.Application.Agent;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>
/// Công cụ <c>spending_summary</c>: chi tiêu theo nhóm trong N tháng gần nhất (gồm tháng này, đến hôm nay). Nguồn dữ liệu là bản giả; kiểm cách công cụ gom top 5 + "khác", tính phần trăm số nguyên
/// và giữ tổng bằng đúng tổng các nhóm.
/// </summary>
public class SpendingSummaryToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(7));

    private static JsonElement Args(object args) => JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement.Clone();

    private static CategorySpendVnd C(string slug, long amount) => new(slug, slug.ToUpperInvariant(), amount);

    private static async Task<(ToolResult Result, DateOnly? From, DateOnly? To)> RunAsync(IReadOnlyList<CategorySpendVnd> rows, int months = 3)
    {
        DateOnly? from = null, to = null;
        var source = new Mock<IFinancialDataSource>();
        source.Setup(s => s.GetCategorySpendAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateOnly, DateOnly, CancellationToken>((_, f, t, _) => { from = f; to = t; })
            .ReturnsAsync(rows);
        var result = await new SpendingSummaryTool(source.Object).RunAsync(new ToolContext(Guid.NewGuid(), Now), Args(new { months }));
        return (result, from, to);
    }

    [Fact]
    public async Task ReturnsTotalAndTopFiveCategories()
    {
        var (result, from, to) = await RunAsync([C("an-uong", 4_000_000), C("di-chuyen", 2_000_000), C("nha-o", 1_000_000), C("mua-sam", 500_000), C("giai-tri", 500_000)]);

        result.Status.Should().Be(ToolStatus.Ok);
        var total = result.Evidence.Single(e => e.Id == "spend_total");
        total.Key.Should().Be("spend_total");
        total.ValueVnd.Should().Be(8_000_000);
        total.Source.Should().Be(EvidenceSource.Confirmed);
        total.Text.Should().Contain("đến hôm nay");
        result.Evidence.Where(e => e.Id.StartsWith("cat_")).Select(e => e.Id).Should().Equal("cat_1", "cat_2", "cat_3", "cat_4", "cat_5");
        var top = result.Evidence.Single(e => e.Id == "cat_1");
        top.Key.Should().Be("category_spend:an-uong");
        top.ValueVnd.Should().Be(4_000_000);
        top.Text.Should().Be("AN-UONG: 50% tổng chi");
        from.Should().Be(new DateOnly(2026, 9, 1)); // 3 tháng gần nhất tính cả tháng 11
        to.Should().Be(new DateOnly(2026, 11, 8));
    }

    [Fact]
    public async Task GroupsRemainingCategoriesIntoOther()
    {
        var (result, _, _) = await RunAsync([C("a", 800), C("b", 700), C("c", 500), C("d", 400), C("e", 300), C("f", 200), C("g", 100)]);

        result.Evidence.Where(e => e.Id.StartsWith("cat_") && e.Id != "cat_other").Should().HaveCount(5);
        var other = result.Evidence.Single(e => e.Id == "cat_other");
        other.ValueVnd.Should().Be(300);
        other.Text.Should().Contain("10%");
        result.Evidence.Should().NotContain(e => e.Key == "category_spend:f");
    }

    [Fact]
    public async Task CategoryAmountsPlusOtherEqualTotal()
    {
        var (result, _, _) = await RunAsync([C("a", 1_700_000), C("b", 1_300_000), C("c", 900_000), C("d", 800_000), C("e", 700_000), C("f", 450_000), C("g", 150_000)]);

        var total = result.Evidence.Single(e => e.Id == "spend_total").ValueVnd!.Value;
        total.Should().Be(6_000_000);
        result.Evidence.Where(e => e.Id.StartsWith("cat_")).Sum(e => e.ValueVnd!.Value).Should().Be(6_000_000);
        result.Evidence.Single(e => e.Id == "cat_other").ValueVnd.Should().Be(600_000);
    }

    [Fact]
    public async Task EqualAmountsAreOrderedBySlugSoTheTopFiveIsDeterministic()
    {
        var (result, _, _) = await RunAsync([C("g", 100), C("f", 100), C("e", 100), C("d", 100), C("c", 100), C("b", 100), C("a", 100)]);

        result.Evidence.Where(e => e.Id.StartsWith("cat_") && e.Id != "cat_other").Select(e => e.Key)
            .Should().Equal("category_spend:a", "category_spend:b", "category_spend:c", "category_spend:d", "category_spend:e");
    }

    [Fact]
    public async Task NoTransactionsReturnsInsufficientNoSpendingHistory()
    {
        var (result, _, _) = await RunAsync([]);

        result.Status.Should().Be(ToolStatus.InsufficientData);
        result.Code.Should().Be("no_spending_history");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task RejectsMonthsOutsideRange(int months)
    {
        var executor = new ToolExecutor(new ToolRegistry([new SpendingSummaryTool(Mock.Of<IFinancialDataSource>())]));

        var result = await executor.ExecuteAsync(new ToolContext(Guid.NewGuid(), Now), "spending_summary", Args(new { months }));

        result.Status.Should().Be(ToolStatus.Rejected);
        result.Code.Should().Be("invalid_argument:months");
    }
}
