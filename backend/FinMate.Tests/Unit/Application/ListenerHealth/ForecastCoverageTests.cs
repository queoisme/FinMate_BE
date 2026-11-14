using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Application.ListenerHealth;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.ListenerHealth;

/// <summary>BR-LH-08, D10, S6, S7: poor hạ độ tin cậy và thêm câu cảnh báo, không đổi con số; cửa sổ theo cấu hình dự báo; cache theo user + cửa sổ.</summary>
public class ForecastCoverageTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);

    private static CashflowForecastDto Forecast(string confidence = "normal")
        => new(Today, Today.AddDays(10), 1_000_000, 800_000, 700_000, 900_000, Today.AddDays(5), 600_000, confidence, "history", [], []);

    [Fact]
    public void Poor_LowersConfidence_AddsTheWarning_AndKeepsEveryNumber()
    {
        var before = Forecast();
        var coverage = ForecastCoverage.Summarize(new CoverageResult(50, CoverageLevel.Poor, 90, 30, 15));

        var after = ForecastCoverage.Apply(before, coverage);

        after.Confidence.Should().Be("low");
        after.DataCoverage.Should().Be(coverage);
        coverage.Warning.Should().Be("Chỉ 50% số ngày gần đây FinMate đọc được thông báo ngân hàng, nên dự báo có thể thiếu giao dịch.");
        after.Should().BeEquivalentTo(before, o => o.Excluding(f => f.Confidence).Excluding(f => f.DataCoverage));
    }

    [Theory]
    [InlineData(CoverageLevel.Good, 90)]
    [InlineData(CoverageLevel.Partial, 70)]
    [InlineData(CoverageLevel.Unknown, null)]
    public void NotPoor_KeepsTheConfidence_AndHasNoWarning(CoverageLevel level, int? percent)
    {
        var coverage = ForecastCoverage.Summarize(new CoverageResult(percent, level, 90, 30, 0));

        var after = ForecastCoverage.Apply(Forecast("normal"), coverage);

        after.Confidence.Should().Be("normal");
        after.DataCoverage!.Warning.Should().BeNull();
        after.DataCoverage.WindowDays.Should().Be(90);
    }

    [Fact]
    public void AnAlreadyLowForecast_StaysLow()
        => ForecastCoverage.Apply(Forecast("low"), ForecastCoverage.Summarize(new CoverageResult(95, CoverageLevel.Good, 90, 30, 28))).Confidence.Should().Be("low");

    [Theory]
    [InlineData(90, 90)]
    [InlineData(365, 90)]
    [InlineData(30, 30)]
    public async Task TheForecastWindow_IsTheHistoryWindowClampedToNinety(int historyWindowDays, int expected)
    {
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemConfigSnapshot(1, new SystemConfigDocument { Forecast = new ForecastSettings { HistoryWindowDays = historyWindowDays } }, null));
        var accounts = new Mock<IFinancialAccountRepository>();
        accounts.Setup(a => a.GetListByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var repository = new Mock<IListenerHealthRepository>();
        repository.Setup(r => r.GetStatusesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var cache = new Mock<ICacheService>();
        var service = new DataCoverageService(Mock.Of<IListenerHealthReader>(), repository.Object, config.Object, accounts.Object, cache.Object);
        var user = Guid.NewGuid();

        var summary = await service.GetForecastSummaryAsync(user);

        summary.WindowDays.Should().Be(expected);
        summary.Level.Should().Be("unknown");
        cache.Verify(c => c.SetAsync(DataCoverageCache.Key(user, expected), summary, DataCoverageCache.Ttl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ACachedSummary_IsReturnedWithoutReadingTheDatabase()
    {
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(1, new SystemConfigDocument(), null));
        var user = Guid.NewGuid();
        var cached = new DataCoverageSummaryDto(40, "poor", 90, "x");
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<DataCoverageSummaryDto>(DataCoverageCache.Key(user, 90), It.IsAny<CancellationToken>())).ReturnsAsync(cached);
        var repository = new Mock<IListenerHealthRepository>(MockBehavior.Strict);
        var service = new DataCoverageService(Mock.Of<IListenerHealthReader>(), repository.Object, config.Object, Mock.Of<IFinancialAccountRepository>(), cache.Object);

        (await service.GetForecastSummaryAsync(user)).Should().Be(cached);
    }
}
