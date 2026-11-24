using System.Runtime.CompilerServices;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.MarketData;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.MarketData;

/// <summary>Bộ đọc giá vàng. Dữ liệu mẫu là trang thật của 24h và CafeF ngày 2026-11-23 (đã bỏ script và style), hai nguồn hạng licensed có số giấy phép.</summary>
public class GoldTableParserTests
{
    private static readonly GoldSettings Settings = new();

    private static string Page(string file, [CallerFilePath] string here = "")
        => HtmlTextExtractor.PageText(File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "Fixtures", file)));

    private static readonly GoldReading Sjc = new("sjc_bar", 130_000_000, 135_000_000);

    [Theory]
    [InlineData("gold-24h-2026-11-23.html")]
    [InlineData("gold-cafef-2026-11-23.html")]
    public void ReadsTheSjcBarPriceOfTheRealPages(string file)
        => GoldTableParser.Parse(Page(file), Settings).Should().Equal(Sjc);

    [Fact]
    public void ChangeColumnsAndPercentagesAreSkipped()
        => GoldTableParser.Parse("Giá vàng miếng SJC Mua vào 130,000 1,500 (1.17%) Bán ra 135,000 1,500 (1.12%) Đơn vị: nghìn đồng/ lượng", Settings).Should().Equal(Sjc);

    [Fact]
    public void APricePerChiIsConvertedToPerLuong()
        => GoldTableParser.Parse("Vàng SJC 1L 13.000.000 13.500.000 Đơn vị: đồng / chỉ", Settings with { Rows = [new GoldRow { Key = "sjc_bar", Label = "x", Names = ["Vàng SJC 1L"] }] })
            .Should().Equal(Sjc);

    [Theory]
    [InlineData("vàng miếng SJC lên mốc 135 triệu đồng/lượng. Đơn vị: nghìn đồng/lượng")]
    [InlineData("Tìm mã vàng nhanh SJC DOJI HN 130,000 135,000 Đơn vị: nghìn đồng/lượng")]
    [InlineData("Vàng miếng SJC 130.00 1.50 135.00 1.50 Đơn vị: triệu đồng/ lượng")]
    [InlineData("SJC 130,000 135,000")]
    [InlineData("SJC 130,000 135,000 Đơn vị: tỉ đồng/lượng")]
    public void SentencesOtherBrandsAndUnknownUnitsAreNotPrices(string text)
        => GoldTableParser.Parse(text, Settings).Should().BeEmpty();

    [Fact]
    public void ABuyPriceAboveTheSellPriceIsRejected()
        => GoldTableParser.Parse("SJC 135,000 130,000 Đơn vị: nghìn đồng/lượng", Settings).Should().BeEmpty();

    [Fact]
    public void DisabledReadsNothing()
        => GoldTableParser.Parse("SJC 130,000 135,000 Đơn vị: nghìn đồng/lượng", Settings with { Enabled = false }).Should().BeEmpty();
}
