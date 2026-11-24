using FinMate.Application.Admin.SystemConfig;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>Danh sách nguồn uy tín trong cấu hình hệ thống.</summary>
public class MarketTrustConfigValidatorTests
{
    private readonly SystemConfigDocumentValidator _validator = new();

    private static SystemConfigDocument With(params MarketTrustedDomain[] domains)
        => new() { MarketData = new MarketDataSettings { TrustedDomains = domains } };

    private static MarketTrustedDomain Licensed(string domain, string license = "1/GP-STTTT") => new() { Domain = domain, Tier = MarketTrustedDomain.Licensed, Owner = domain, LicenseRef = license };

    [Fact]
    public void ALicensedSourceMustCarryItsLicense()
    {
        _validator.Validate(With(Licensed("bao.vn"))).IsValid.Should().BeTrue();
        _validator.Validate(With(Licensed("bao.vn", license: ""))).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("www.bao.vn")]
    [InlineData("Bao.vn")]
    [InlineData("https://bao.vn")]
    [InlineData("bao")]
    public void OnlyRootDomainsAreAccepted(string domain)
        => _validator.Validate(With(Licensed(domain))).IsValid.Should().BeFalse();

    [Fact]
    public void DomainsMustNotRepeat()
        => _validator.Validate(With(Licensed("bao.vn"), Licensed("bao.vn"))).IsValid.Should().BeFalse();

    [Fact]
    public void TheCpiSourceMustBeAnOfficialDomain()
    {
        var official = new SystemConfigDocument { EconomicFeeds = new EconomicFeedsSettings { CpiIndexUrl = "https://www.nso.gov.vn/?s=cpi" } };
        var untrusted = new SystemConfigDocument { EconomicFeeds = new EconomicFeedsSettings { CpiIndexUrl = "https://cpi.example.vn/" } };
        var licensed = official with { MarketData = new MarketDataSettings { TrustedDomains = [Licensed("nso.gov.vn")] } };

        _validator.Validate(official).IsValid.Should().BeTrue();
        _validator.Validate(untrusted).IsValid.Should().BeFalse();
        _validator.Validate(licensed).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ReleaseNamesMustMapToKnownFuels()
    {
        var doc = new SystemConfigDocument { MarketData = new MarketDataSettings { FuelReleaseNames = [new FuelReleaseName { Name = "Xăng A", FuelKey = "khong_co" }] } };

        _validator.Validate(doc).IsValid.Should().BeFalse();
    }
}
