using FinMate.Application.Admin.SystemConfig;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>Nhóm cấu hình <c>duplicates</c> (Phase 82): mặc định đúng như đã chốt, khoảng hợp lệ, và nhật ký audit gọi đúng tên nhóm khi đổi.</summary>
public class DuplicateSettingsTests
{
    private readonly SystemConfigDocumentValidator _validator = new();

    [Fact]
    public void Defaults_AreTheAgreedWindows()
    {
        var settings = new SystemConfigDocument().Duplicates;

        settings.NotificationWindowMinutes.Should().Be(10);
        settings.ManualWindowHours.Should().Be(48);
        settings.ReceiptWindowHours.Should().Be(12);
    }

    [Theory]
    [InlineData(0, 48, 12, false)]
    [InlineData(1, 48, 12, true)]
    [InlineData(60, 48, 12, true)]
    [InlineData(61, 48, 12, false)]
    [InlineData(10, 0, 12, false)]
    [InlineData(10, 168, 12, true)]
    [InlineData(10, 169, 12, false)]
    [InlineData(10, 48, 0, false)]
    [InlineData(10, 48, 72, true)]
    [InlineData(10, 48, 73, false)]
    public void Windows_MustStayInRange(int notificationMinutes, int manualHours, int receiptHours, bool valid)
    {
        var doc = new SystemConfigDocument
        {
            Duplicates = new DuplicateSettings
            {
                NotificationWindowMinutes = notificationMinutes,
                ManualWindowHours = manualHours,
                ReceiptWindowHours = receiptHours,
            },
        };

        _validator.Validate(doc).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("duplicates")]
    [InlineData("forecast")]
    [InlineData("refunds")]
    public void ChangedSections_NamesEverySectionThatChanged(string section)
    {
        var before = new SystemConfigDocument();
        var after = section switch
        {
            "duplicates" => before with { Duplicates = new DuplicateSettings { ManualWindowHours = 24 } },
            "forecast" => before with { Forecast = new ForecastSettings { AnomalyScanDays = 5 } },
            _ => before with { Refunds = new RefundSettings { MatchWindowDays = 30 } },
        };

        SystemConfigService.ChangedSections(before, after).Should().Equal(section);
    }
}
