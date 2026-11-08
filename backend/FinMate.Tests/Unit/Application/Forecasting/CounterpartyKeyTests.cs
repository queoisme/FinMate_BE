using FinMate.Application.Forecasting;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class CounterpartyKeyTests
{
    [Theory]
    [InlineData("Netflix", "netflix")]
    [InlineData("  NETFLIX  ", "netflix")]
    [InlineData("Điện lực Hà Nội", "dien luc ha noi")]
    [InlineData("Tiền nhà - T11/2026", "tien nha t11 2026")]
    [InlineData("CỬA HÀNG ỨNG DỤNG", "cua hang ung dung")]
    public void From_FoldsCaseAccentsAndPunctuation(string input, string expected)
        => CounterpartyKey.From(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--- ///")]
    public void From_NothingUsable_IsNull(string? input)
        => CounterpartyKey.From(input).Should().BeNull();

    [Fact]
    public void From_LongName_IsCutToTheColumnWidth()
        => CounterpartyKey.From(new string('a', 300))!.Length.Should().Be(CounterpartyKey.MaxLength);

    [Fact]
    public void From_EveryVietnameseVowelWithAMark_FoldsToItsBaseLetter()
    {
        // Bảng gấp dấu là hai chuỗi song song: lệch độ dài thì Zip cắt mất đuôi một cách im lặng.
        const string marked = "àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ";
        CounterpartyKey.From(marked).Should().MatchRegex("^[aeiouyd]+$").And.HaveLength(marked.Length);
    }
}
