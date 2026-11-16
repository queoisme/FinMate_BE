using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.ListenerHealth;
using FinMate.Application.Monitoring;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.ListenerHealth;

/// <summary>BR-LH-10, S1, D13–D15: hai tín hiệu đổi mẫu tin, sàn nền và tỉ lệ tối thiểu, thiếu mẫu thì giữ nguyên, 24 lượt bình thường thì đóng.</summary>
public class ProviderFormatMonitorTests
{
    private static readonly ListenerHealthSettings Settings = new();
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 3, 5, 0, TimeSpan.Zero);

    private static ProviderFormatCounts Counts(int recentTotal, int recentFailed, int recentFinancial, int baseTotal, int baseFailed, int baseFinancial)
        => new("vietcombank", "Vietcombank", recentTotal, recentFailed, recentFinancial, baseTotal, baseFailed, baseFinancial);

    [Fact]
    public void ErrorRateJump_FromTwoToThirtyPercent_IsUnhealthy()
    {
        var verdict = ProviderFormatEvaluator.Evaluate(Counts(40, 12, 20, 150, 3, 90), Settings);

        verdict.State.Should().Be(MonitoredServiceState.Unhealthy);
        verdict.Signals.Should().Equal(FormatSignal.ErrorRate);
    }

    [Fact]
    public void FinancialShareCollapse_WithNoErrors_IsUnhealthy()
    {
        var verdict = ProviderFormatEvaluator.Evaluate(Counts(40, 0, 2, 150, 0, 90), Settings);   // 60% → 5%

        verdict.State.Should().Be(MonitoredServiceState.Unhealthy);
        verdict.Signals.Should().Equal(FormatSignal.FinancialShare);
    }

    [Fact]
    public void BothSignals_AreReported()
        => ProviderFormatEvaluator.Evaluate(Counts(40, 12, 2, 150, 3, 90), Settings).Signals.Should().Equal(FormatSignal.ErrorRate, FormatSignal.FinancialShare);

    [Fact]
    public void TooFewRecentSamples_IsUnknown()
        => ProviderFormatEvaluator.Evaluate(Counts(19, 19, 0, 150, 3, 90), Settings).State.Should().Be(MonitoredServiceState.Unknown);

    [Fact]
    public void OneStrayFailure_OnAZeroBaseline_IsHealthy()
        => ProviderFormatEvaluator.Evaluate(Counts(20, 1, 12, 150, 0, 90), Settings).State.Should().Be(MonitoredServiceState.Healthy);

    [Fact]
    public void AZeroBaseline_UsesTheFloor_SoARealJumpStillCounts()
        => ProviderFormatEvaluator.Evaluate(Counts(20, 3, 12, 150, 0, 90), Settings).Signals.Should().Contain(FormatSignal.ErrorRate);

    [Fact]
    public void ANewProviderWithoutABaseline_IsJudgedAgainstTheFloor_AndSkipsTheShareSignal()
    {
        var verdict = ProviderFormatEvaluator.Evaluate(Counts(30, 0, 0, 5, 0, 5), Settings);

        verdict.State.Should().Be(MonitoredServiceState.Healthy);
    }

    [Fact]
    public void ARiseBelowTheRatio_IsHealthy()
        => ProviderFormatEvaluator.Evaluate(Counts(40, 6, 20, 150, 9, 90), Settings).State.Should().Be(MonitoredServiceState.Healthy);   // 6% → 15%: < 3 lần

    [Fact]
    public void TheDetail_HasOnlyCounts()
    {
        var verdict = ProviderFormatEvaluator.Evaluate(Counts(40, 12, 20, 150, 3, 90), Settings);

        verdict.Detail.Should().Be("24h: lỗi 12/40 (30%), giao dịch 20/40 (50%); 7 ngày trước: lỗi 3/150 (2%), giao dịch 90/150 (60%)");
    }

    private sealed class Harness
    {
        public Mock<IProviderFormatRepository> Counts { get; } = new();
        public Mock<IMonitoringRepository> Monitoring { get; } = new();
        public Mock<IIncidentService> Incidents { get; } = new();
        public OpsServiceStatus? Status { get; set; }

        public Harness()
        {
            Monitoring.Setup(m => m.GetStatusAsync("format:vietcombank", It.IsAny<CancellationToken>())).ReturnsAsync(() => Status);
            Monitoring.Setup(m => m.AddStatus(It.IsAny<OpsServiceStatus>())).Callback<OpsServiceStatus>(s => Status = s);
        }

        public ProviderFormatMonitor Monitor()
        {
            var config = new Mock<ISystemConfigProvider>();
            config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(1, new SystemConfigDocument(), null));
            return new ProviderFormatMonitor(Counts.Object, Monitoring.Object, Incidents.Object, config.Object);
        }

        public void Given(ProviderFormatCounts counts)
            => Counts.Setup(c => c.CountAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([counts]);
    }

    [Fact]
    public async Task AnUnhealthyProvider_RaisesANormalIncident_KeyedByProvider()
    {
        var h = new Harness();
        h.Given(Counts(40, 12, 20, 150, 3, 90));
        IncidentSignal? raised = null;
        h.Incidents.Setup(i => i.RaiseAsync(It.IsAny<IncidentSignal>(), 1, Now, It.IsAny<CancellationToken>()))
            .Callback<IncidentSignal, int, DateTimeOffset, CancellationToken>((s, _, _, _) => raised = s);

        await h.Monitor().RunAsync(Now);

        raised!.Fingerprint.Should().Be("notification_format:vietcombank");
        raised.Source.Should().Be(IncidentSource.NotificationFormat);
        raised.Severity.Should().Be(IncidentSeverity.Normal);
        raised.Title.Should().Be("Có thể Vietcombank đã đổi mẫu thông báo");
        raised.TechnicalDetail.Should().StartWith("Tín hiệu: error_rate.");
        h.Status!.State.Should().Be(MonitoredServiceState.Unhealthy);
        h.Status.ConsecutiveFailures.Should().Be(1);
    }

    [Theory]
    [InlineData(22, false)]
    [InlineData(23, true)]
    public async Task TwentyFourHealthyRunsInARow_ResolveTheIncident(int previousHealthyRuns, bool resolves)
    {
        var h = new Harness { Status = new OpsServiceStatus { Service = "format:vietcombank", State = MonitoredServiceState.Healthy, ConsecutiveSuccesses = previousHealthyRuns } };
        h.Given(Counts(40, 1, 24, 150, 3, 90));

        await h.Monitor().RunAsync(Now);

        h.Incidents.Verify(i => i.ResolveAsync("notification_format:vietcombank", Now, It.IsAny<CancellationToken>()), resolves ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task TooFewSamples_KeepsTheCounters_AndNeitherRaisesNorResolves()
    {
        var h = new Harness { Status = new OpsServiceStatus { Service = "format:vietcombank", State = MonitoredServiceState.Healthy, ConsecutiveSuccesses = 23 } };
        h.Given(Counts(5, 0, 3, 150, 3, 90));

        await h.Monitor().RunAsync(Now);

        h.Status!.State.Should().Be(MonitoredServiceState.Unknown);
        h.Status.ConsecutiveSuccesses.Should().Be(23);
        h.Incidents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnUnhealthyRun_ResetsTheRecoveryCount()
    {
        var h = new Harness { Status = new OpsServiceStatus { Service = "format:vietcombank", ConsecutiveSuccesses = 20 } };
        h.Given(Counts(40, 0, 2, 150, 0, 90));

        await h.Monitor().RunAsync(Now);

        h.Status!.ConsecutiveSuccesses.Should().Be(0);
        h.Status.Detail.Should().Be("financial_share");
    }
}
