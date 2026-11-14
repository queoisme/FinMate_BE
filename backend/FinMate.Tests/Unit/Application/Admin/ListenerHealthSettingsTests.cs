using FinMate.Application.Admin.SystemConfig;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>Nhóm cấu hình <c>listenerHealth</c> và hai ngưỡng độ phủ của <c>forecast</c> (Phase 84): mặc định đã chốt, khoảng hợp lệ, audit gọi đúng tên nhóm.</summary>
public class ListenerHealthSettingsTests
{
    private readonly SystemConfigDocumentValidator _validator = new();

    [Fact]
    public void Defaults_AreTheAgreedValues()
    {
        var doc = new SystemConfigDocument();
        var lh = doc.ListenerHealth;

        lh.HistoryRetentionDays.Should().Be(90);
        lh.HistoryDays.Should().Be(30);
        lh.MinNotificationsForBaseline.Should().Be(8);
        lh.MinSilenceHours.Should().Be(48);
        lh.SilenceMultiplier.Should().Be(3);
        lh.StaleReportHours.Should().Be(72);
        lh.CoverageInheritHours.Should().Be(36);
        lh.CooldownDays.Should().Be(3);
        lh.MaxRemindersPerEpisode.Should().Be(3);
        lh.MaxDevicesPerUser.Should().Be(10);
        lh.FormatDriftRatio.Should().Be(3);
        lh.FormatDriftMinSamples.Should().Be(20);
        lh.FormatDriftMinRatePercent.Should().Be(10);
        lh.FormatDriftBaselineFloorPercent.Should().Be(1);
        lh.FormatDriftRecoveryRuns.Should().Be(24);
        lh.FormatFinancialShareDropRatio.Should().Be(0.33);
        doc.Forecast.GoodCoveragePercent.Should().Be(85);
        doc.Forecast.MinCoveragePercent.Should().Be(60);
        _validator.Validate(doc).IsValid.Should().BeTrue();
    }

    public static TheoryData<ListenerHealthSettings, bool> Ranges() => new()
    {
        { new ListenerHealthSettings { HistoryRetentionDays = 6 }, false },
        { new ListenerHealthSettings { HistoryRetentionDays = 365 }, true },
        { new ListenerHealthSettings { HistoryRetentionDays = 366 }, false },
        { new ListenerHealthSettings { HistoryDays = 6 }, false },
        { new ListenerHealthSettings { HistoryDays = 91 }, false },
        { new ListenerHealthSettings { MinNotificationsForBaseline = 1 }, false },
        { new ListenerHealthSettings { MinNotificationsForBaseline = 2 }, true },
        { new ListenerHealthSettings { MinSilenceHours = 0 }, false },
        { new ListenerHealthSettings { MinSilenceHours = 721 }, false },
        { new ListenerHealthSettings { SilenceMultiplier = 0.9 }, false },
        { new ListenerHealthSettings { SilenceMultiplier = 20 }, true },
        { new ListenerHealthSettings { SilenceMultiplier = 20.1 }, false },
        { new ListenerHealthSettings { StaleReportHours = 11 }, false },
        { new ListenerHealthSettings { StaleReportHours = 720 }, true },
        { new ListenerHealthSettings { CoverageInheritHours = 11 }, false },
        { new ListenerHealthSettings { CoverageInheritHours = 12 }, true },
        { new ListenerHealthSettings { CoverageInheritHours = 72 }, true },
        { new ListenerHealthSettings { CoverageInheritHours = 73 }, false },
        { new ListenerHealthSettings { CooldownDays = 0 }, false },
        { new ListenerHealthSettings { CooldownDays = 31 }, false },
        { new ListenerHealthSettings { MaxRemindersPerEpisode = 0 }, false },
        { new ListenerHealthSettings { MaxRemindersPerEpisode = 11 }, false },
        { new ListenerHealthSettings { MaxDevicesPerUser = 0 }, false },
        { new ListenerHealthSettings { MaxDevicesPerUser = 51 }, false },
        { new ListenerHealthSettings { FormatDriftRatio = 1.0 }, false },
        { new ListenerHealthSettings { FormatDriftRatio = 1.1 }, true },
        { new ListenerHealthSettings { FormatDriftRatio = 100.1 }, false },
        { new ListenerHealthSettings { FormatDriftMinSamples = 0 }, false },
        { new ListenerHealthSettings { FormatDriftMinSamples = 10_001 }, false },
        { new ListenerHealthSettings { FormatDriftMinRatePercent = -1 }, false },
        { new ListenerHealthSettings { FormatDriftMinRatePercent = 101 }, false },
        { new ListenerHealthSettings { FormatDriftBaselineFloorPercent = -1 }, false },
        { new ListenerHealthSettings { FormatDriftBaselineFloorPercent = 101 }, false },
        { new ListenerHealthSettings { FormatDriftRecoveryRuns = 0 }, false },
        { new ListenerHealthSettings { FormatDriftRecoveryRuns = 169 }, false },
        { new ListenerHealthSettings { FormatFinancialShareDropRatio = 0.009 }, false },
        { new ListenerHealthSettings { FormatFinancialShareDropRatio = 0.01 }, true },
        { new ListenerHealthSettings { FormatFinancialShareDropRatio = 1 }, true },
        { new ListenerHealthSettings { FormatFinancialShareDropRatio = 1.01 }, false },
    };

    [Theory]
    [MemberData(nameof(Ranges))]
    public void ListenerHealthValues_MustStayInRange(ListenerHealthSettings settings, bool valid)
    {
        _validator.Validate(new SystemConfigDocument { ListenerHealth = settings }).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData(85, 60, true)]
    [InlineData(60, 60, true)]
    [InlineData(100, 0, true)]
    [InlineData(59, 60, false)]
    [InlineData(101, 60, false)]
    [InlineData(85, -1, false)]
    public void CoveragePercents_MinMustNotExceedGood(int good, int min, bool valid)
    {
        var doc = new SystemConfigDocument { Forecast = new ForecastSettings { GoodCoveragePercent = good, MinCoveragePercent = min } };

        _validator.Validate(doc).IsValid.Should().Be(valid);
    }

    [Fact]
    public void ChangedSections_NamesListenerHealth()
    {
        var before = new SystemConfigDocument();
        var after = before with { ListenerHealth = new ListenerHealthSettings { CooldownDays = 5 } };

        SystemConfigService.ChangedSections(before, after).Should().Equal("listenerHealth");
    }
}
