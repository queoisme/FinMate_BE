using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>
/// Công cụ <c>plan_budget</c>: chia phần thu nhập còn lại sau tiết kiệm thành các nhóm chi. Tỉ lệ lấy từ chi tiêu của chính người dùng 3 tháng trọn gần nhất,
/// không có thì từ mẫu admin <c>agent.budgetTemplate</c>. Kiểm làm tròn xuống nghìn đồng, phần dư dồn vào nhóm lớn nhất và tổng luôn khớp thu nhập.
/// </summary>
public class PlanBudgetToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(7));

    private static JsonElement Args(object args) => JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement.Clone();

    private static CategorySpendVnd C(string slug, long amount) => new(slug, slug.ToUpperInvariant(), amount);

    private sealed class Run
    {
        public ToolResult Result { get; init; } = null!;
        public DateOnly? From { get; init; }
        public DateOnly? To { get; init; }
    }

    private static async Task<Run> RunAsync(object args, IReadOnlyList<CategorySpendVnd>? spend = null, string template = "", IReadOnlyList<long>? incomeSeries = null)
    {
        DateOnly? from = null, to = null;
        var source = new Mock<IFinancialDataSource>();
        source.Setup(s => s.GetCategorySpendAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateOnly, DateOnly, CancellationToken>((_, f, t, _) => { from = f; to = t; })
            .ReturnsAsync(spend ?? []);
        source.Setup(s => s.GetMonthlySeriesAsync(It.IsAny<Guid>(), 12, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonthlySeries(incomeSeries ?? [], []));
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemConfigSnapshot(1, new SystemConfigDocument { Agent = new AgentSettings { BudgetTemplate = template } }, null));

        var result = await new PlanBudgetTool(source.Object, config.Object).RunAsync(new ToolContext(Guid.NewGuid(), Now), Args(args));
        return new Run { Result = result, From = from, To = to };
    }

    private static long Alloc(ToolResult r, string slug) => r.Evidence.Single(e => e.Key == $"budget_alloc:{slug}").ValueVnd!.Value;

    private static long AllocSum(ToolResult r) => r.Evidence.Where(e => e.Id.StartsWith("alloc_")).Sum(e => e.ValueVnd!.Value);

    [Fact]
    public async Task AllocationsPlusSavingEqualIncome()
    {
        var run = await RunAsync(new { income_vnd = 2_000_000, save_vnd = 1_000_000 }, template: """{"an-uong":50,"nha-o":30,"giai-tri":20}""");

        run.Result.Status.Should().Be(ToolStatus.Ok);
        AllocSum(run.Result).Should().Be(1_000_000);
        Alloc(run.Result, "an-uong").Should().Be(500_000);
        Alloc(run.Result, "nha-o").Should().Be(300_000);
        Alloc(run.Result, "giai-tri").Should().Be(200_000);
        var alloc = run.Result.Evidence.Single(e => e.Key == "budget_alloc:nha-o");
        alloc.Id.Should().Be("alloc_2"); // id theo vị trí (số tiền giảm dần), không theo slug
        alloc.Source.Should().Be(EvidenceSource.Estimated);
        var income = run.Result.Evidence.Single(e => e.Id == "income");
        income.ValueVnd.Should().Be(2_000_000);
        income.Source.Should().Be(EvidenceSource.UserEntered);
        var save = run.Result.Evidence.Single(e => e.Id == "save");
        save.Key.Should().Be("save");
        save.ValueVnd.Should().Be(1_000_000);
        save.Source.Should().Be(EvidenceSource.UserEntered);
        var rate = run.Result.Evidence.Single(e => e.Id == "savings_rate");
        rate.ValueVnd.Should().BeNull();
        rate.Unit.Should().Be("pct");
        rate.Text.Should().Be("Tiết kiệm chiếm 50% thu nhập");
        run.Result.Evidence.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        run.Result.Evidence.Select(e => e.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task RemainingIsIncomeMinusSaveAndEqualsSumOfAllocations()
    {
        var given = await RunAsync(new { income_vnd = 7_300_000, save_vnd = 1_250_000 }, spend: [C("a", 5), C("b", 3), C("c", 2)]);
        var median = await RunAsync(new { save_vnd = 1_000_000 }, template: """{"a":100}""", incomeSeries: [9_000_000, 3_000_000, 5_000_000]);

        var remaining = given.Result.Evidence.Single(e => e.Id == "remaining");
        remaining.Key.Should().Be("budget_remaining");
        remaining.ValueVnd.Should().Be(6_050_000);
        remaining.Source.Should().Be(EvidenceSource.UserEntered);
        remaining.Text.Should().Be("Phần còn lại để chi tiêu mỗi tháng (thu nhập − tiết kiệm)");
        AllocSum(given.Result).Should().Be(6_050_000);
        var medianRemaining = median.Result.Evidence.Single(e => e.Id == "remaining");
        medianRemaining.ValueVnd.Should().Be(4_000_000);
        medianRemaining.Source.Should().Be(EvidenceSource.Estimated);
    }

    [Fact]
    public async Task ManyCategoriesFoldIntoSevenPlusOtherWithinTwelveEvidence()
    {
        var longSlug = new string('x', 80);
        var spend = Enumerable.Range(1, 11).Select(i => C($"cat-{i:00}", 1_000L * i)).Append(C(longSlug, 20_000)).ToList(); // 12 nhóm, slug dài nhất lớn nhất

        var run = await RunAsync(new { income_vnd = 10_000_000, save_vnd = 1_000_000 }, spend: spend);

        var allocs = run.Result.Evidence.Where(e => e.Id.StartsWith("alloc_")).ToList();
        allocs.Should().HaveCount(8);
        allocs.Select(e => e.Id).Should().Equal("alloc_1", "alloc_2", "alloc_3", "alloc_4", "alloc_5", "alloc_6", "alloc_7", "alloc_other");
        var other = allocs.Single(e => e.Id == "alloc_other");
        other.Key.Should().Be("budget_alloc_other");
        other.Text.Should().StartWith("Các nhóm khác:");
        AllocSum(run.Result).Should().Be(9_000_000);
        run.Result.Evidence.Single(e => e.Id == "remaining").ValueVnd.Should().Be(9_000_000);
        run.Result.Evidence.Should().HaveCountLessThanOrEqualTo(12);
        run.Result.Evidence.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        run.Result.Evidence.Select(e => e.Key).Should().OnlyHaveUniqueItems();
        run.Result.Evidence.Should().OnlyContain(e => e.Id.Length <= 64 && e.Key.Length <= 64);
        allocs[0].Key.Should().StartWith("budget_alloc:xxx"); // nhóm slug dài vẫn có mặt, key bị cắt
    }

    [Fact]
    public async Task LongSlugsSharingAPrefixKeepUniqueKeys()
    {
        var a = new string('y', 70) + "a";
        var b = new string('y', 70) + "b";

        var run = await RunAsync(new { income_vnd = 3_000_000, save_vnd = 1_000_000 }, spend: [C(a, 2), C(b, 1)]);

        var keys = run.Result.Evidence.Where(e => e.Id.StartsWith("alloc_")).Select(e => e.Key).ToList();
        keys.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        keys.Should().OnlyContain(k => k.Length <= 64);
    }

    [Fact]
    public async Task RoundsToThousandAndAbsorbsRemainderInLargestBucket()
    {
        // Ba phần bằng nhau của 1.000.000: làm tròn xuống được 333.000 mỗi nhóm, dư 1.000 dồn vào nhóm "a" (bằng nhau thì slug nhỏ nhất).
        var run = await RunAsync(new { income_vnd = 2_000_000, save_vnd = 1_000_000 }, spend: [C("c", 100), C("b", 100), C("a", 100)]);

        Alloc(run.Result, "a").Should().Be(334_000);
        Alloc(run.Result, "b").Should().Be(333_000);
        Alloc(run.Result, "c").Should().Be(333_000);
        AllocSum(run.Result).Should().Be(1_000_000);
    }

    [Fact]
    public async Task RemainderBelowThousandStillLandsOnLargestBucketAndZeroBucketsAreOmitted()
    {
        // còn 1.500.500: "lon" 99,9% → 1.498.000 + dư 2.500; "nho"/"vua" 0,05% → 750 làm tròn xuống 0 nên bị bỏ, phần đó cũng dồn vào "lon".
        var run = await RunAsync(new { income_vnd = 2_500_500, save_vnd = 1_000_000 }, spend: [C("lon", 1998), C("nho", 1), C("vua", 1)]);

        run.Result.Evidence.Should().NotContain(e => e.Key == "budget_alloc:nho" || e.Key == "budget_alloc:vua");
        Alloc(run.Result, "lon").Should().Be(1_500_500);
        (AllocSum(run.Result) + 1_000_000).Should().Be(2_500_500);
    }

    [Fact]
    public async Task HugeAmountsDoNotOverflow()
    {
        var run = await RunAsync(new { income_vnd = 100_000_000_000, save_vnd = 1_000 },
            spend: [C("a", 99_000_000_000), C("b", 99_000_000_000), C("c", 99_000_000_001)]);

        run.Result.Status.Should().Be(ToolStatus.Ok);
        (AllocSum(run.Result) + 1_000).Should().Be(100_000_000_000);
        Alloc(run.Result, "a").Should().BeInRange(33_333_000_000, 33_334_000_000);
    }

    [Theory]
    [InlineData(1_000_000, 1_000_000)]
    [InlineData(1_000_000, 2_000_000)]
    public async Task SavingEqualOrAboveIncomeIsInsufficientGoalExceedsIncome(long income, long save)
    {
        var run = await RunAsync(new { income_vnd = income, save_vnd = save }, template: """{"a":100}""");

        run.Result.Status.Should().Be(ToolStatus.InsufficientData);
        run.Result.Code.Should().Be("goal_exceeds_income");
    }

    [Fact]
    public async Task UsesUserOwnShareBeforeTemplate()
    {
        var run = await RunAsync(new { income_vnd = 3_000_000, save_vnd = 1_000_000 }, spend: [C("an-uong", 3_000_000), C("di-chuyen", 1_000_000)], template: """{"nha-o":100}""");

        Alloc(run.Result, "an-uong").Should().Be(1_500_000);
        Alloc(run.Result, "di-chuyen").Should().Be(500_000);
        run.Result.Evidence.Should().NotContain(e => e.Key == "budget_alloc:nha-o");
        run.Result.Evidence.Single(e => e.Key == "budget_alloc:an-uong").Text.Should().StartWith("AN-UONG");
        run.From.Should().Be(new DateOnly(2026, 8, 1)); // 3 tháng TRỌN gần nhất: 8, 9, 10
        run.To.Should().Be(new DateOnly(2026, 10, 31));
    }

    [Fact]
    public async Task FallsBackToAdminTemplate()
    {
        var run = await RunAsync(new { income_vnd = 3_000_000, save_vnd = 1_000_000 }, template: """{"thiet-yeu":75,"linh-hoat":25}""");

        Alloc(run.Result, "thiet-yeu").Should().Be(1_500_000);
        Alloc(run.Result, "linh-hoat").Should().Be(500_000);
    }

    [Theory]
    [InlineData("""{"a":50,"b":40}""")]       // tổng ≠ 100
    [InlineData("""{"a":50,"b":60}""")]
    [InlineData("""{"a":50.5,"b":49.5}""")]   // không phải số nguyên
    [InlineData("""{"a":110,"b":-10}""")]     // âm
    [InlineData("""{"":100}""")]              // slug rỗng
    [InlineData("""{"a":50,"a":50}""")]       // trùng slug
    [InlineData("""[100]""")]
    [InlineData("không phải json")]
    public async Task InvalidTemplateSumIsTreatedAsMissing(string template)
    {
        var run = await RunAsync(new { income_vnd = 2_000_000, save_vnd = 1_000_000 }, template: template);

        run.Result.Status.Should().Be(ToolStatus.InsufficientData);
        run.Result.Code.Should().Be("no_budget_basis");
    }

    [Fact]
    public async Task NoBasisIsInsufficientNoBudgetBasis()
    {
        var run = await RunAsync(new { income_vnd = 2_000_000, save_vnd = 1_000_000 });

        run.Result.Status.Should().Be(ToolStatus.InsufficientData);
        run.Result.Code.Should().Be("no_budget_basis");
    }

    [Fact]
    public async Task MissingIncomeFallsBackToMedianIncome()
    {
        var run = await RunAsync(new { save_vnd = 1_000_000 }, template: """{"a":100}""", incomeSeries: [9_000_000, 3_000_000, 5_000_000]);

        var income = run.Result.Evidence.Single(e => e.Id == "income");
        income.ValueVnd.Should().Be(5_000_000);
        income.Source.Should().Be(EvidenceSource.Estimated);
        Alloc(run.Result, "a").Should().Be(4_000_000);
        run.Result.Evidence.Single(e => e.Id == "savings_rate").Text.Should().Be("Tiết kiệm chiếm 20% thu nhập");
    }

    [Fact]
    public async Task MissingIncomeWithoutHistoryIsInsufficient()
    {
        var run = await RunAsync(new { save_vnd = 1_000_000 }, template: """{"a":100}""");

        run.Result.Code.Should().Be("history_too_short");
    }

    [Theory]
    [InlineData(999)]
    [InlineData(100_000_000_001)]
    public async Task RejectsSaveOutsideRange(long save)
    {
        var executor = new ToolExecutor(new ToolRegistry([new PlanBudgetTool(Mock.Of<IFinancialDataSource>(), Mock.Of<ISystemConfigProvider>())]));

        var result = await executor.ExecuteAsync(new ToolContext(Guid.NewGuid(), Now), "plan_budget", Args(new { save_vnd = save }));

        result.Status.Should().Be(ToolStatus.Rejected);
        result.Code.Should().Be("invalid_argument:save_vnd");
    }
}
