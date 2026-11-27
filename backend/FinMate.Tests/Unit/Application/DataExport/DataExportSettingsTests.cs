using FinMate.Application.Admin.SystemConfig;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.DataExport;

/// <summary>Phase 91: mặc định của xuất dữ liệu (BR-EXP-01…10, S3, S4) và giới hạn khi nhân sự sửa cấu hình.</summary>
public class DataExportSettingsTests
{
    private readonly SystemConfigDocumentValidator _validator = new();

    [Fact]
    public void Defaults_AreTheApprovedOnes_AndTheFeatureIsOn()
    {
        var doc = new SystemConfigDocument();

        doc.Features.DataExport.Should().BeTrue("quyền theo luật, chỉ tắt khi sự cố");
        doc.DataExport.Should().Be(new DataExportSettings
        {
            MaxRangeDays = 366,
            MaxRows = 50_000,
            CsvPerHour = 20,
            MaxRequestsPerDay = 1,
            FileRetentionHours = 72,
            MaxArchiveMb = 25,
            DownloadLinkMinutes = 10,
            RequestHistoryDays = 365,
        });
        _validator.Validate(doc).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AStoredConfigWrittenBeforePhase91_ReadsWithTheDefaults()
    {
        var doc = System.Text.Json.JsonSerializer.Deserialize<SystemConfigDocument>(
            """{"features":{"groups":true}}""", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        doc.Features.DataExport.Should().BeTrue();
        doc.DataExport.MaxArchiveMb.Should().Be(25);
    }

    public static TheoryData<DataExportSettings> OutOfBounds => new()
    {
        new DataExportSettings { MaxRangeDays = 30 },
        new DataExportSettings { MaxRows = 999 },
        new DataExportSettings { CsvPerHour = 0 },
        new DataExportSettings { MaxRequestsPerDay = 0 },
        new DataExportSettings { FileRetentionHours = 0 },
        new DataExportSettings { MaxArchiveMb = 201 },
        new DataExportSettings { DownloadLinkMinutes = 61 },
        new DataExportSettings { RequestHistoryDays = 29 },
    };

    [Theory]
    [MemberData(nameof(OutOfBounds))]
    public void EachLimit_IsBounded(DataExportSettings settings)
    {
        _validator.Validate(new SystemConfigDocument { DataExport = settings }).Errors.Should().ContainSingle();
    }
}
