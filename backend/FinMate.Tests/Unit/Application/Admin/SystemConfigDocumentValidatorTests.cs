using FinMate.Application.Admin.SystemConfig;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>Cấu hình hệ thống: giá trị mặc định hợp lệ, và từng quy tắc chặn đúng dữ liệu sai (màu, phiên bản, ngưỡng, URL logo).</summary>
public class SystemConfigDocumentValidatorTests
{
    private readonly SystemConfigDocumentValidator _validator = new();

    private static SystemConfigDocument WithTheme(ThemeSettings theme) => new() { Theme = theme };

    [Fact]
    public void TheDefaultDocument_IsValid()
    {
        _validator.Validate(new SystemConfigDocument()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("#2E7D6B", true)]
    [InlineData("#fff", false)]
    [InlineData("red", false)]
    [InlineData("#GGGGGG", false)]
    public void ThemeColors_MustBeSixDigitHex_OrEmpty(string color, bool valid)
    {
        _validator.Validate(WithTheme(new ThemeSettings { PrimaryColor = color })).IsValid.Should().Be(valid);
        _validator.Validate(WithTheme(new ThemeSettings { PrimaryColor = null })).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.8, true)]
    [InlineData(1.5, true)]
    [InlineData(0.7, false)]
    [InlineData(1.6, false)]
    public void TextScale_IsLimitedSoTheAppStaysUsable(double scale, bool valid)
    {
        _validator.Validate(WithTheme(new ThemeSettings { TextScale = scale })).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("https://cdn.example.com/logo.png", true)]
    [InlineData("http://cdn.example.com/logo.png", false)]
    [InlineData("javascript:alert(1)", false)]
    public void TheLogoUrl_MustBeHttps(string url, bool valid)
    {
        _validator.Validate(WithTheme(new ThemeSettings { LogoUrl = url })).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("Be Vietnam Pro", true)]
    [InlineData("Roboto", true)]
    [InlineData("Font;DROP TABLE", false)]
    public void TheFontName_IsPlainTextOnly(string font, bool valid)
    {
        _validator.Validate(WithTheme(new ThemeSettings { FontFamily = font })).IsValid.Should().Be(valid);
    }

    [Fact]
    public void TheMinimumAppVersion_CannotExceedTheLatest()
    {
        var bad = new SystemConfigDocument { AppVersion = new AppVersionSettings { MinVersion = "2.0.0", LatestVersion = "1.9.9" } };
        var good = new SystemConfigDocument { AppVersion = new AppVersionSettings { MinVersion = "1.2.0", LatestVersion = "1.10.0" } };  // 1.10 > 1.9: so sánh theo số, không theo chữ

        _validator.Validate(bad).IsValid.Should().BeFalse();
        _validator.Validate(good).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.x")]
    public void AppVersions_MustBeThreeNumbers(string version)
    {
        _validator.Validate(new SystemConfigDocument { AppVersion = new AppVersionSettings { MinVersion = version } }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TheNearThreshold_MustBeBelowTheOverThreshold()
    {
        _validator.Validate(new SystemConfigDocument { Limits = new LimitSettings { BudgetNearPercent = 100, BudgetOverPercent = 90 } }).IsValid.Should().BeFalse();
        _validator.Validate(new SystemConfigDocument { Limits = new LimitSettings { BudgetNearPercent = 80, BudgetOverPercent = 100 } }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TheMaintenanceMessage_CannotBeEmptyOrHuge()
    {
        _validator.Validate(new SystemConfigDocument { Maintenance = new MaintenanceSettings { Enabled = true, Message = "" } }).IsValid.Should().BeFalse();
        _validator.Validate(new SystemConfigDocument { Maintenance = new MaintenanceSettings { Message = new string('x', 501) } }).IsValid.Should().BeFalse();
    }
}
