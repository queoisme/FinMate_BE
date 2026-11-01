using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.FinanceMethods;
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

    [Theory]
    [InlineData(70, 90, true)]
    [InlineData(1, 2, true)]
    [InlineData(98, 99, true)]
    [InlineData(90, 70, false)]    // T1 phải nhỏ hơn T2
    [InlineData(80, 80, false)]
    [InlineData(0, 50, false)]     // T1 từ 1%
    [InlineData(50, 100, false)]   // 100% là mức "vượt hạn mức" cố định, không cấu hình
    public void TheDefaultAlertThresholds_MustBeOrdered_AndStayBelow100(int t1, int t2, bool valid)
    {
        _validator.Validate(new SystemConfigDocument { Limits = new LimitSettings { AlertT1Percent = t1, AlertT2Percent = t2 } }).IsValid.Should().Be(valid);
    }

    [Fact]
    public void TheLifeBuoyDefault_ComesFromTheConfigDefaults_NotFromASecondCopyOfTheNumbers()
    {
        var defaults = new LimitSettings();

        (defaults.AlertT1Percent, defaults.AlertT2Percent).Should().Be((70, 90));
        FinMate.Application.Budgets.AlertThresholds.Default.Should().Be(new FinMate.Application.Budgets.AlertThresholds(defaults.AlertT1Percent, defaults.AlertT2Percent));
    }

    [Fact]
    public void AnOldStoredConfig_WithTheRetiredLimitFields_StillLoads_WithTheNewDefaults()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<SystemConfigDocument>(
            """{"limits":{"budgetNearPercent":80,"budgetOverPercent":100}}""", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        (old.Limits.AlertT1Percent, old.Limits.AlertT2Percent).Should().Be((70, 90));
        _validator.Validate(old).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TheMaintenanceMessage_CannotBeEmptyOrHuge()
    {
        _validator.Validate(new SystemConfigDocument { Maintenance = new MaintenanceSettings { Enabled = true, Message = "" } }).IsValid.Should().BeFalse();
        _validator.Validate(new SystemConfigDocument { Maintenance = new MaintenanceSettings { Message = new string('x', 501) } }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(180, true)]
    [InlineData(4, false)]
    [InlineData(181, false)]
    public void QrMatchWindow_MustBeBetween5And180Minutes(int minutes, bool valid)
    {
        _validator.Validate(new SystemConfigDocument { QrTransfer = new QrTransferSettings { MatchWindowMinutes = minutes } }).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("dl.vietqr.io", true)]
    [InlineData("pay.example.com", true)]
    [InlineData("https://dl.vietqr.io", false)]   // không kèm https://
    [InlineData("dl.vietqr.io/pay", false)]       // không kèm đường dẫn
    [InlineData("dl.vietqr.io:8443", false)]      // không kèm cổng
    [InlineData("192.168.1.10", false)]           // không IP trần
    [InlineData("localhost", false)]              // phải có tên miền đầy đủ
    public void QrDeeplinkHost_MustBeABareDomainName(string host, bool valid)
    {
        _validator.Validate(new SystemConfigDocument { QrTransfer = new QrTransferSettings { DeeplinkHost = host } }).IsValid.Should().Be(valid);
    }

    [Fact]
    public void QrDefaultMessage_MustFitTheMaximumLength_AndHaveNoControlCharacters()
    {
        _validator.Validate(new SystemConfigDocument { QrTransfer = new QrTransferSettings { MaxMessageLength = 20, DefaultMessage = new string('a', 21) } }).IsValid.Should().BeFalse();
        _validator.Validate(new SystemConfigDocument { QrTransfer = new QrTransferSettings { DefaultMessage = "a\nb" } }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 50, true)]        // 0 phút = luôn tạo mới
    [InlineData(1440, 100, true)]
    [InlineData(-1, 50, false)]
    [InlineData(1441, 50, false)]
    [InlineData(10, 0, false)]       // 0% sẽ cho mọi cách bổ trợ điểm dương lọt qua
    [InlineData(10, 101, false)]
    public void FinanceMethods_ReuseMinutesAndSupportShare_AreBounded(int reuseMinutes, int supportPercent, bool valid)
    {
        var settings = new FinanceMethodSettings { RecommendationReuseMinutes = reuseMinutes, SupportMinSharePercent = supportPercent };
        _validator.Validate(new SystemConfigDocument { FinanceMethods = settings }).IsValid.Should().Be(valid);
    }

    [Fact]
    public void FinanceMethods_DefaultsKeepTheValuesThatUsedToBeHardCoded()
    {
        var defaults = new SystemConfigDocument().FinanceMethods;
        defaults.RecommendationReuseMinutes.Should().Be(10);
        defaults.SupportMinSharePercent.Should().Be(MethodSelector.DefaultSupportMinSharePercent).And.Be(50);
    }

    [Fact]
    public void QrReturnUrl_MustBeHttps()
    {
        _validator.Validate(new SystemConfigDocument { QrTransfer = new QrTransferSettings { DeeplinkReturnUrl = "http://www.vietqr.io/deeplink" } }).IsValid.Should().BeFalse();
    }
}
