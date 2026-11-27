using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Kpi;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Kpi;

public class KpiMathTests
{
    [Theory]
    [InlineData(0, 5, "0")]
    [InlineData(1, 5, "<5")]
    [InlineData(4, 5, "<5")]
    [InlineData(5, 5, "5")]
    [InlineData(2, 3, "<3")]
    [InlineData(3, 3, "3")]
    public void SmallCells_BetweenOneAndTheThreshold_BecomeAString(long value, int minCellSize, string expected)
    {
        // Bộ ghi JSON mặc định viết '<' thành \u003C; client đọc ra vẫn là "<5", nên so trên giá trị đã đọc.
        var element = JsonDocument.Parse(JsonSerializer.Serialize(KpiCount.Mask(value, minCellSize))).RootElement;

        var masked = value > 0 && value < minCellSize;
        element.ValueKind.Should().Be(masked ? JsonValueKind.String : JsonValueKind.Number);
        (masked ? element.GetString() : element.GetInt64().ToString()).Should().Be(expected);
    }

    [Fact]
    public void KpiCount_RoundTripsThroughJson_SoTheCacheKeepsTheMask()
    {
        var masked = JsonSerializer.Deserialize<KpiCount>(JsonSerializer.Serialize(KpiCount.Mask(2, 5)));
        var exact = JsonSerializer.Deserialize<KpiCount>(JsonSerializer.Serialize(KpiCount.Mask(7, 5)));

        masked.Should().Be(new KpiCount(0, 5));
        exact.Should().Be(KpiCount.Exact(7));
    }

    [Fact]
    public void MaskCells_KeepsEveryRequestedKey_AndMasksEachOnItsOwn()
    {
        var cells = new Dictionary<string, long> { ["manual"] = 12, ["voice"] = 2, ["pro"] = 1 };

        var masked = KpiMath.MaskCells(cells, ["notification", "manual", "voice"], 5);

        masked["notification"].Should().Be(KpiCount.Exact(0));
        masked["manual"].Should().Be(KpiCount.Exact(12));
        masked["voice"].Masked.Should().BeTrue();
        masked["pro"].Masked.Should().BeTrue("khoá có số mà không nằm trong danh sách vẫn phải được che");
    }

    [Theory]
    [InlineData(20, 30, 4, 2, 0.8333)]
    [InlineData(0, 5, 3, 2, null)]
    [InlineData(0, 0, 0, 0, null)]
    [InlineData(3, 3, 0, 0, 1.0)]
    public void ConversionRate_IsRecomputedFromTotals_AndNullWithoutADenominator(long converted, long received, long ignored, long duplicate, double? expected)
        => KpiMath.ConversionRate(converted, received, ignored, duplicate).Should().Be(expected);

    [Fact]
    public void KpiSettings_DefaultIsFive_AndTheValidatorKeepsItBetween2And20()
    {
        var validator = new SystemConfigDocumentValidator();

        new SystemConfigDocument().Kpi.MinCellSize.Should().Be(5);
        validator.Validate(new SystemConfigDocument()).IsValid.Should().BeTrue();
        validator.Validate(new SystemConfigDocument { Kpi = new KpiSettings { MinCellSize = 1 } }).IsValid.Should().BeFalse();
        validator.Validate(new SystemConfigDocument { Kpi = new KpiSettings { MinCellSize = 21 } }).IsValid.Should().BeFalse();
    }
}
