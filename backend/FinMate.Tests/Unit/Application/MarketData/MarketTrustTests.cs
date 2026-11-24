using System.Runtime.CompilerServices;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.MarketData;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.MarketData;

/// <summary>Nguồn uy tín và thông cáo giá xăng của Bộ Công Thương. Dữ liệu mẫu là trang thật ngày 19/11/2026 (tải ngày 2026-11-23).</summary>
public class MarketTrustTests
{
    private static readonly MarketDataSettings Settings = new();

    private static string ReleaseText([CallerFilePath] string here = "")
        => HtmlTextExtractor.PageText(File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "Fixtures", "moit-fuel-release-2026-11-19.html")));

    private static FuelReading Price(long region1, long? region2 = null) => new(FuelKeys.E10Ron95Level3, region1, region2);

    private static readonly IReadOnlyDictionary<string, FuelReading> NoPrices = new Dictionary<string, FuelReading>();

    [Fact]
    public void ReadsTheCeilingPricesOfTheRealRelease()
    {
        var readings = FuelReleaseParser.Parse(ReleaseText(), Settings);

        readings.Should().BeEquivalentTo(new[]
        {
            new FuelReading(FuelKeys.E5Ron92Level2, 18_576, null),
            new FuelReading(FuelKeys.E10Ron95Level3, 19_100, null),
            new FuelReading(FuelKeys.Diesel005Level2, 26_347, null),
        });
        FuelReadingValidator.ValidateRelease(readings, Settings).Should().BeNull();
    }

    [Fact]
    public void TheDecreaseAmountIsNeverReadAsAPrice()
        => FuelReleaseParser.Parse("Xăng E10RON95-III: giảm 745 đồng/lít so với giá bán tối đa hiện hành", Settings).Should().BeEmpty();

    [Fact]
    public void FindsTheReleaseLinkOnTheSameSiteOnly()
    {
        const string index = """
            <a href="/tin-tuc/mot-so-thong-tin-ve-viec-dieu-hanh-gia-mat-hang-xang-ngay-31-10.html">Một số thông tin về việc điều hành giá mặt hàng xăng dầu ngày 31/10</a>
            <a href="https://bao.vn/thong-tin.html">Thông tin về việc điều hành giá xăng dầu ngày 19/11/2026</a>
            <a href="/tin-tuc/thong-tin-ve-viec-dieu-hang-gia-xang-dau-ngay-19-11-2026.html">Thông tin về việc điều hành giá xăng dầu ngày 19/11/2026</a>
            """;

        FuelReleaseParser.FindLatestRelease(index, new Uri("https://moit.gov.vn/"), Settings)!.AbsoluteUri
            .Should().Be("https://moit.gov.vn/tin-tuc/thong-tin-ve-viec-dieu-hang-gia-xang-dau-ngay-19-11-2026.html");
    }

    [Theory]
    [InlineData("https://www.moit.gov.vn/a", "official")]
    [InlineData("https://tin.24h.com.vn/a", "licensed")]
    [InlineData("https://webgia.com/gia-vang/", MarketTrust.Untrusted)]
    public void TierComesFromTheRootDomain(string url, string tier)
        => MarketTrust.TierOf(url, Settings).Should().Be(tier);

    [Fact]
    public void OneOfficialSourceIsEnough()
    {
        var result = FuelPriceConfirmer.Confirm([new SourceFuelTable("moit.gov.vn", [Price(19_100)], Official: true)], NoPrices, Settings);

        result.Confirmed.Should().ContainSingle().Which.Should().Be(Price(19_100));
        result.Domains![FuelKeys.E10Ron95Level3].Should().Equal("moit.gov.vn");
    }

    [Fact]
    public void AnOfficialSourceWinsOverLicensedSourcesThatDisagree()
    {
        var result = FuelPriceConfirmer.Confirm(
            [new SourceFuelTable("moit.gov.vn", [Price(19_100)], Official: true), new SourceFuelTable("a.vn", [Price(19_500, 19_900)]), new SourceFuelTable("b.vn", [Price(19_500, 19_900)])],
            NoPrices,
            Settings);

        result.Confirmed.Should().ContainSingle().Which.Should().Be(Price(19_100));
    }

    [Fact]
    public void TwoOfficialSourcesThatDisagreeConfirmNothing()
        => FuelPriceConfirmer.Confirm(
            [new SourceFuelTable("moit.gov.vn", [Price(19_100)], Official: true), new SourceFuelTable("petrolimex.com.vn", [Price(19_200)], Official: true)],
            NoPrices,
            Settings).Confirmed.Should().BeEmpty();

    [Fact]
    public void TwoDomainsOfOneOwnerCountAsOneSource()
        => FuelPriceConfirmer.Confirm(
            [new SourceFuelTable("tuoitre.vn", [Price(19_100, 19_500)], Owner: "cơ quan báo"), new SourceFuelTable("nld.com.vn", [Price(19_100, 19_500)], Owner: "cơ quan báo")],
            NoPrices,
            Settings).Confirmed.Should().BeEmpty();

    [Fact]
    public void ASourceWithoutRegion2DoesNotRepeatAnUnchangedPrice()
    {
        var latest = new Dictionary<string, FuelReading> { [FuelKeys.E10Ron95Level3] = Price(19_100, 19_480) };

        FuelPriceConfirmer.Confirm([new SourceFuelTable("moit.gov.vn", [Price(19_100)], Official: true)], latest, Settings).Confirmed.Should().BeEmpty();
    }

    [Fact]
    public void TheDefaultTrustedListIsValid()
    {
        var licensed = Settings.TrustedDomains.Where(d => d.Tier == MarketTrustedDomain.Licensed).ToList();

        licensed.Should().NotBeEmpty();
        licensed.Should().OnlyContain(d => d.LicenseRef.Length > 0, "nguồn có giấy phép phải ghi số giấy phép");
        Settings.TrustedDomains.Should().OnlyContain(d => MarketDomains.OfHost(d.Domain) == d.Domain);
    }
}
