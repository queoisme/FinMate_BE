using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.MarketData;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.MarketData;

public class FuelParsingTests
{
    private static readonly MarketDataSettings Settings = new();

    /// <summary>Cấu trúc thật của webgia.com, 2026-11-06.</summary>
    private const string WebgiaPage = """
        <html><body><nav><a href="/x">Giá xăng dầu</a></nav>
        <h1>Giá bán lẻ xăng dầu Petrolimex hôm nay</h1>
        <table><thead><tr><th>Sản phẩm</th><th>Vùng 1</th><th>Vùng 2</th></tr></thead><tbody>
        <tr><td>Xăng E10 RON 95 mức 5</td><td>22.650</td><td>23.100</td></tr>
        <tr><td>Xăng E10 RON 95 mức 3</td><td>21.150</td><td>21.570</td></tr>
        <tr><td>Xăng E5 RON 92 mức 2</td><td>20.890</td><td>21.300</td></tr>
        <tr><td>Dầu DO 0,001S mức 5</td><td>30.090</td><td>30.690</td></tr>
        <tr><td>Dầu DO 0,05S mức 2</td><td>28.690</td><td>29.260</td></tr>
        <tr><td>Dầu hỏa 2-K</td><td>28.600</td><td>29.170</td></tr>
        </tbody></table><p>Đơn vị: đồng / lít</p></body></html>
        """;

    /// <summary>Cấu trúc thật của giaxanghomnay.com: có hai cột tăng giảm đứng trước và dùng dấu phẩy.</summary>
    private const string GiaxangPage = """
        <html><body><table><tr><th>Xăng/dầu</th><th>Tăng giảm</th><th>Tăng giảm kỳ trước*</th><th>Giá vùng 1</th><th>Giá vùng 2</th></tr>
        <tr><td>Xăng E5 RON 92 Mức 2</td><td>0</td><td>-2,310</td><td>20,890</td><td>21,300</td></tr>
        <tr><td>Xăng E10 RON 95 Mức 5</td><td>0</td><td>-1,580</td><td>22,650</td><td>23,100</td></tr>
        <tr><td>Xăng E10 RON 95 Mức 3</td><td>0</td><td>-2,080</td><td>21,150</td><td>21,570</td></tr>
        <tr><td>Dầu hỏa 2-K</td><td>0</td><td>-410</td><td>28,600</td><td>29,170</td></tr>
        <tr><td>DO 0,05S Mức 2</td><td>0</td><td>-180</td><td>28,690</td><td>29,260</td></tr>
        <tr><td>DO 0,001S Mức 5</td><td>0</td><td>-180</td><td>30,090</td><td>30,690</td></tr>
        </table></body></html>
        """;

    private static IReadOnlyList<FuelReading> Read(string html) => FuelTableParser.Parse(HtmlTextExtractor.PageText(html));

    [Fact]
    public void ReadsTheDotFormatPage()
    {
        var readings = Read(WebgiaPage);

        readings.Should().HaveCount(6);
        readings.Should().Contain(new FuelReading(FuelKeys.E10Ron95Level3, 21150, 21570));
        readings.Should().Contain(new FuelReading(FuelKeys.Kerosene, 28600, 29170));
    }

    [Fact]
    public void ReadsTheCommaFormatPageAndSkipsTheChangeColumns()
    {
        var readings = Read(GiaxangPage);

        readings.Should().Contain(new FuelReading(FuelKeys.E5Ron92Level2, 20890, 21300));
        readings.Should().Contain(new FuelReading(FuelKeys.Diesel0001Level5, 30090, 30690));
    }

    [Fact]
    public void BothRealPagesProduceTheSameNumbers()
        => Read(WebgiaPage).Should().BeEquivalentTo(Read(GiaxangPage));

    [Theory]
    [InlineData("Xăng E10 RON 95 mức 3 giảm 2.080 đồng còn 21.150 đồng một lít, E5 RON 92 mức 2 tăng 100 đồng.")]
    [InlineData("Giá E10 RON 95 mức 3 hiện là 21.150 đồng/lít, vùng 2 là 21.570 đồng/lít.")]
    public void ProseInAnArticleIsNeverAPriceRow(string text)
        => FuelTableParser.Parse(text).Should().BeEmpty();

    [Fact]
    public void ARowWithOnlyOnePriceIsIgnored()
        => FuelTableParser.Parse("Xăng E10 RON 95 mức 3 21.150").Should().BeEmpty();

    [Fact]
    public void AnAmountChangeOfSmallMagnitudeIsNotAPrice()
        => FuelTableParser.Parse("Xăng E10 RON 95 mức 3 100 200 21.150").Should().BeEmpty();

    [Fact]
    public void ATableMissingARequiredFuelFails()
    {
        var error = FuelReadingValidator.ValidateTable([new FuelReading(FuelKeys.E10Ron95Level3, 21150, 21570)], Settings);

        error.Should().Be(FuelErrorCodes.MissingFuels);
    }

    [Theory]
    [InlineData(5_000, 5_100, FuelErrorCodes.PriceOutOfRange)]
    [InlineData(70_000, 71_000, FuelErrorCodes.PriceOutOfRange)]
    [InlineData(22_000, 21_000, FuelErrorCodes.RegionMismatch)]
    [InlineData(22_000, 22_400, null)]
    public void PricesMustBePlausible(long region1, long region2, string? expected)
        => FuelReadingValidator.ValidatePrice(new FuelReading(FuelKeys.E10Ron95Level3, region1, region2), Settings).Should().Be(expected);

    private static IReadOnlyList<FuelReading> Table(params long[] region1)
        => region1.Select(price => new FuelReading(FuelKeys.E10Ron95Level3, price, price + 400)).ToList();

    private static readonly IReadOnlyDictionary<string, FuelReading> NoPrices = new Dictionary<string, FuelReading>();

    [Fact]
    public void TwoSourcesAgreeing_IsConfirmed()
    {
        var result = FuelPriceConfirmer.Confirm([Table(21150), Table(21150)], NoPrices, Settings);

        result.Confirmed.Should().ContainSingle().Which.Region1PriceCents.Should().Be(21150);
    }

    [Fact]
    public void OneSourceAlone_IsNotEnough()
        => FuelPriceConfirmer.Confirm([Table(21150)], NoPrices, Settings).Confirmed.Should().BeEmpty();

    [Fact]
    public void OneSourceIsEnoughWhenConfiguredSo()
        => FuelPriceConfirmer.Confirm([Table(21150)], NoPrices, Settings with { MinAgreeingSources = 1 }).Confirmed.Should().ContainSingle();

    [Fact]
    public void SourcesThatDisagree_ConfirmNothing()
        => FuelPriceConfirmer.Confirm([Table(21150), Table(21650)], NoPrices, Settings).Confirmed.Should().BeEmpty();

    [Fact]
    public void AnEvenSplitWithEnoughOnBothSidesConfirmsNothing()
        => FuelPriceConfirmer.Confirm([Table(21150), Table(21150), Table(21650), Table(21650)], NoPrices, Settings).Confirmed.Should().BeEmpty();

    [Fact]
    public void AnUnchangedPrice_AddsNothing()
    {
        var latest = new Dictionary<string, FuelReading> { [FuelKeys.E10Ron95Level3] = Table(21150)[0] };

        FuelPriceConfirmer.Confirm([Table(21150), Table(21150)], latest, Settings).Confirmed.Should().BeEmpty();
    }

    [Fact]
    public void AnImplausibleJump_IsHeldBackEvenWhenSourcesAgree()
    {
        var latest = new Dictionary<string, FuelReading> { [FuelKeys.E10Ron95Level3] = Table(21150)[0] };

        var result = FuelPriceConfirmer.Confirm([Table(30000), Table(30000)], latest, Settings);

        result.Confirmed.Should().BeEmpty();
        result.HeldBack.Should().ContainSingle();
    }

    [Fact]
    public void DetectKindRecognisesEachType()
    {
        MarketDataService.DetectKind(WebgiaPage, Settings).Should().Be(FinMate.Domain.Enums.MarketSourceKind.PriceTable);
        MarketDataService.DetectKind("<rss version=\"2.0\"><channel></channel></rss>", Settings).Should().Be(FinMate.Domain.Enums.MarketSourceKind.Feed);
        MarketDataService.DetectKind("<html><body><p>Tin tức</p></body></html>", Settings).Should().Be(FinMate.Domain.Enums.MarketSourceKind.Page);
    }
}
