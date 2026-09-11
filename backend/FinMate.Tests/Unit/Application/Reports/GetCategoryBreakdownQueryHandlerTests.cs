using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Reports.Queries;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Reports;

public class GetCategoryBreakdownQueryHandlerTests
{
    private readonly Mock<IReportRepository> _reportRepository = new();
    private readonly GetCategoryBreakdownQueryHandler _handler;

    public GetCategoryBreakdownQueryHandlerTests()
    {
        _handler = new GetCategoryBreakdownQueryHandler(_reportRepository.Object);
    }

    private void SetupSpend(params CategorySpend[] spend)
        => _reportRepository.Setup(r => r.GetCategorySpendAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(spend.ToList());

    private Task<CategoryBreakdownDto> HandleAsync()
        => _handler.HandleAsync(new GetCategoryBreakdownQuery(Guid.NewGuid(), 2026, 9));

    [Fact]
    public async Task Handle_NoSpending_ReturnsEmptyWithZeroTotal()
    {
        SetupSpend();

        var result = await HandleAsync();

        result.TotalSpentCents.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EvenSplit_GivesExactPercentages()
    {
        SetupSpend(
            new CategorySpend(Guid.NewGuid(), "Ăn uống", "food", 600_000),
            new CategorySpend(Guid.NewGuid(), "Di chuyển", "transport", 400_000));

        var result = await HandleAsync();

        result.TotalSpentCents.Should().Be(1_000_000);
        result.Items.Select(i => i.Percent).Should().Equal(60, 40);
    }

    [Fact]
    public async Task Handle_PercentagesAlwaysSumToOneHundred_EvenWhenTheyDoNotDivideEvenly()
    {
        // Ba phần bằng nhau: làm tròn xuống cho 33+33+33 = 99, thiếu 1.
        SetupSpend(
            new CategorySpend(Guid.NewGuid(), "A", "a", 100),
            new CategorySpend(Guid.NewGuid(), "B", "b", 100),
            new CategorySpend(Guid.NewGuid(), "C", "c", 100));

        var result = await HandleAsync();

        result.Items.Sum(i => i.Percent).Should().Be(100);
    }

    [Fact]
    public async Task Handle_RoundingRemainder_GoesToTheLargestCategory()
    {
        var biggest = Guid.NewGuid();
        SetupSpend(
            new CategorySpend(biggest, "A", "a", 500),
            new CategorySpend(Guid.NewGuid(), "B", "b", 251),
            new CategorySpend(Guid.NewGuid(), "C", "c", 249));

        var result = await HandleAsync();

        // 50 + 25 + 24 = 99; phần thiếu dồn vào mục lớn nhất nên nó thành 51.
        result.Items.Sum(i => i.Percent).Should().Be(100);
        result.Items.Single(i => i.CategoryId == biggest).Percent.Should().Be(51);
    }

    [Fact]
    public async Task Handle_UncategorizedSpend_IsLabelledNotDropped()
    {
        SetupSpend(
            new CategorySpend(Guid.NewGuid(), "Ăn uống", "food", 700_000),
            new CategorySpend(null, null, null, 300_000));

        var result = await HandleAsync();

        var uncategorized = result.Items.Single(i => i.CategoryId is null);
        uncategorized.CategoryName.Should().Be("Chưa phân loại");
        uncategorized.CategorySlug.Should().BeNull();
        uncategorized.Percent.Should().Be(30);
    }

    [Fact]
    public async Task Handle_ReturnsVietnamLocalPeriodBounds()
    {
        SetupSpend();

        var result = await HandleAsync();

        result.PeriodStart.Offset.Should().Be(TimeSpan.FromHours(7));
        result.PeriodStart.Day.Should().Be(1);
        result.PeriodStart.Month.Should().Be(9);
    }
}
