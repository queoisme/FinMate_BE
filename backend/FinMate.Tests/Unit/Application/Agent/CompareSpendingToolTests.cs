using System.Text.Json;
using FinMate.Application.Agent;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>
/// Công cụ <c>compare_spending</c>: so N tháng trọn gần nhất với N tháng trọn ngay trước đó (không gồm tháng đang dở). Kiểm số cụ thể, khoảng ngày, phần trăm số nguyên làm tròn nửa lên,
/// không chia cho 0 khi một nhóm mới xuất hiện, và mã thiếu dữ liệu.
/// </summary>
public class CompareSpendingToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(7));

    private static JsonElement Args(object args) => JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement.Clone();

    private static CategorySpendVnd C(string slug, long amount) => new(slug, slug.ToUpperInvariant(), amount);

    /// <summary>Nguồn giả: khoảng bắt đầu ngày 1 tháng 9 (kỳ trước khi months = 1 là tháng 9) trả <paramref name="previous"/>, khoảng khác trả <paramref name="current"/>.</summary>
    private static async Task<(ToolResult Result, List<(DateOnly From, DateOnly To)> Calls)> RunAsync(IReadOnlyList<CategorySpendVnd> current, IReadOnlyList<CategorySpendVnd> previous, int months = 1)
    {
        var calls = new List<(DateOnly, DateOnly)>();
        var source = new Mock<IFinancialDataSource>();
        source.Setup(s => s.GetCategorySpendAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, DateOnly, DateOnly, CancellationToken>((_, from, to, _) =>
            {
                calls.Add((from, to));
                return Task.FromResult(calls.Count == 1 ? current : previous);  // lần gọi đầu là kỳ hiện tại, lần hai là kỳ trước
            });
        var result = await new CompareSpendingTool(source.Object).RunAsync(new ToolContext(Guid.NewGuid(), Now), Args(new { months }));
        return (result, calls);
    }

    [Fact]
    public async Task ReportsIncreaseWithAbsoluteChangeAndPercent_AndComparesOnlyWholeMonths()
    {
        var (result, calls) = await RunAsync([C("an-uong", 6_000_000), C("di-chuyen", 2_000_000)], [C("an-uong", 4_000_000), C("di-chuyen", 2_000_000)]);

        result.Status.Should().Be(ToolStatus.Ok);
        calls[0].Should().Be((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));  // tháng 11 đang dở nên không tính
        calls[1].Should().Be((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        result.Evidence.Single(e => e.Id == "period_current").ValueVnd.Should().Be(8_000_000);
        result.Evidence.Single(e => e.Id == "period_previous").ValueVnd.Should().Be(6_000_000);
        var change = result.Evidence.Single(e => e.Id == "period_change");
        change.ValueVnd.Should().Be(2_000_000);
        change.Text.Should().Be("Tổng chi: tăng 33% so với kỳ trước");  // 2/6 = 33,3% làm tròn
        var top = result.Evidence.Single(e => e.Id == "chg_1");
        (top.Key, top.ValueVnd, top.Text).Should().Be(("category_change:an-uong", 2_000_000, "AN-UONG: tăng 50% so với kỳ trước"));
        result.Evidence.Where(e => e.Id.StartsWith("chg_")).Should().ContainSingle();  // di-chuyen không đổi nên không có dòng
    }

    [Fact]
    public async Task ReportsDecrease_AndEqualTotalsSayUnchangedWithoutAPercent()
    {
        var (down, _) = await RunAsync([C("a", 1_000_000)], [C("a", 4_000_000)]);
        down.Evidence.Single(e => e.Id == "period_change").Text.Should().Be("Tổng chi: giảm 75% so với kỳ trước");

        var (same, _) = await RunAsync([C("a", 1_000_000)], [C("a", 1_000_000)]);
        same.Evidence.Single(e => e.Id == "period_change").Text.Should().Be("Tổng chi: không đổi");
        same.Evidence.Single(e => e.Id == "period_change").ValueVnd.Should().Be(0);
        same.Evidence.Should().NotContain(e => e.Id.StartsWith("chg_"));
    }

    [Fact]
    public async Task NewAndVanishedCategoriesAreWordsNotPercents_AndNothingDividesByZero()
    {
        var (result, _) = await RunAsync([C("moi", 3_000_000), C("a", 1_000_000)], [C("cu", 2_000_000), C("a", 1_000_000)]);

        result.Evidence.Single(e => e.Id == "chg_1").Text.Should().Be("MOI: mới xuất hiện trong kỳ này");
        result.Evidence.Single(e => e.Id == "chg_2").Text.Should().Be("CU: không còn chi trong kỳ này");
        result.Evidence.Where(e => e.Id.StartsWith("chg_")).Select(e => e.Text).Should().OnlyContain(text => !text.Contains('%'));
    }

    [Fact]
    public async Task KeepsTheThreeBiggestChanges_InAStableOrder()
    {
        var current = new[] { C("d", 900), C("c", 800), C("b", 700), C("a", 600) };
        var previous = new[] { C("d", 100), C("c", 100), C("b", 100), C("a", 100) };

        var (result, _) = await RunAsync(current, previous);

        result.Evidence.Where(e => e.Id.StartsWith("chg_")).Select(e => e.Key).Should().Equal("category_change:d", "category_change:c", "category_change:b");
    }

    [Fact]
    public async Task UsesAWholeNumberOfMonthsOnBothSides()
    {
        var (_, calls) = await RunAsync([C("a", 1)], [C("a", 1)], months: 3);

        calls[0].Should().Be((new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 31)));
        calls[1].Should().Be((new DateOnly(2026, 5, 1), new DateOnly(2026, 7, 31)));
    }

    [Fact]
    public async Task WithoutAPreviousPeriodOrAnyHistory_IsInsufficientWithADistinctCode()
    {
        var (noPrevious, _) = await RunAsync([C("a", 1_000_000)], []);
        noPrevious.Status.Should().Be(ToolStatus.InsufficientData);
        noPrevious.Code.Should().Be("no_previous_period");

        var (none, _) = await RunAsync([], []);
        none.Code.Should().Be("no_spending_history");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task RejectsMonthsOutsideOneToSix(int months)
    {
        var executor = new ToolExecutor(new ToolRegistry([new CompareSpendingTool(Mock.Of<IFinancialDataSource>())]));

        var result = await executor.ExecuteAsync(new ToolContext(Guid.NewGuid(), Now), "compare_spending", Args(new { months }));

        result.Status.Should().NotBe(ToolStatus.Ok);
    }
}
