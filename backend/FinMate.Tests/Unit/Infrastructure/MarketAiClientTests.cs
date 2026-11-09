using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.MarketData;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class MarketAiClientTests
{
    [Fact]
    public void ReadsTheAiServiceContract()
    {
        var result = MarketAiClient.Parse("""
            {"status":"ok","signals":[{"topic":"fuel","direction":"up","fact_status":"actual","change_percent":3.5,"effective_date":"2026-11-05","evidence":"Giá xăng tăng 3,5%."},
            {"topic":"water","direction":"down","fact_status":"forecast","change_percent":null,"effective_date":null,"evidence":"x"}]}
            """);

        result.Status.Should().Be(MarketAiStatus.Ok);
        result.Signals.Should().HaveCount(2);
        result.Signals[0].ChangePercent.Should().Be(3.5m);
        result.Signals[1].ChangePercent.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"status":"not_configured","signals":[]}""", MarketAiStatus.NotConfigured)]
    [InlineData("""{"status":"failed","signals":[]}""", MarketAiStatus.Failed)]
    [InlineData("""{"status":"khac"}""", MarketAiStatus.Failed)]
    public void OtherStatusesMapToNoSignals(string body, MarketAiStatus expected)
    {
        var result = MarketAiClient.Parse(body);

        result.Status.Should().Be(expected);
        result.Signals.Should().BeEmpty();
    }
}
