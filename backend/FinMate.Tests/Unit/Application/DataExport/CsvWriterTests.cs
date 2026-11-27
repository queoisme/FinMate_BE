using System.Text;
using FinMate.Application.DataExport;
using FluentAssertions;
using Microsoft.VisualBasic.FileIO;
using Xunit;

namespace FinMate.Tests.Unit.Application.DataExport;

/// <summary>
/// Phase 91 (BR-EXP-02, BR-EXP-03, S1): UTF-8 có BOM, không có dòng <c>sep=</c>, CRLF, RFC 4180 theo dấu phân cách đang dùng, chống CSV injection ở ô chữ,
/// số nguyên không bao giờ bị thêm tiền tố. Đọc lại bằng <see cref="TextFieldParser"/> (D18) để chắc một trình đọc CSV thật hiểu đúng.
/// </summary>
public class CsvWriterTests
{
    private static async Task<byte[]> WriteAsync(CsvDelimiter delimiter, params CsvCell[][] rows)
    {
        using var stream = new MemoryStream();
        await using (var writer = new CsvWriter(stream, delimiter))
        {
            foreach (var row in rows)
            {
                await writer.WriteRowAsync(row);
            }
        }

        return stream.ToArray();
    }

    private static string Body(byte[] bytes) => new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

    private static List<string[]> Parse(byte[] bytes, CsvDelimiter delimiter)
    {
        using var parser = new TextFieldParser(new MemoryStream(bytes), Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(delimiter == CsvDelimiter.Comma ? "," : ";");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            rows.Add(parser.ReadFields()!);
        }

        return rows;
    }

    [Theory]
    [InlineData(CsvDelimiter.Comma)]
    [InlineData(CsvDelimiter.Semicolon)]
    public async Task StartsWithTheUtf8Bom_HasNoSepLine_AndEndsEveryRowWithCrlf(CsvDelimiter delimiter)
    {
        var bytes = await WriteAsync(delimiter, [CsvCell.Text("Ngày"), CsvCell.Text("Số tiền")], [CsvCell.Text("2026-11-25"), CsvCell.Number(45_000)]);

        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var sep = delimiter == CsvDelimiter.Comma ? "," : ";";
        Body(bytes).Should().Be($"Ngày{sep}Số tiền\r\n2026-11-25{sep}45000\r\n");
        Body(bytes).Should().NotContain("sep=");
    }

    [Fact]
    public async Task AnEmptyFile_StillHasTheBom()
    {
        (await WriteAsync(CsvDelimiter.Comma)).Should().Equal(0xEF, 0xBB, 0xBF);
    }

    [Theory]
    [InlineData(CsvDelimiter.Comma, "a,b", "\"a,b\"")]
    [InlineData(CsvDelimiter.Comma, "a;b", "a;b")]
    [InlineData(CsvDelimiter.Semicolon, "a;b", "\"a;b\"")]
    [InlineData(CsvDelimiter.Semicolon, "a,b", "a,b")]
    [InlineData(CsvDelimiter.Comma, "Cà phê \"Highlands\"", "\"Cà phê \"\"Highlands\"\"\"")]
    [InlineData(CsvDelimiter.Comma, "dòng 1\r\ndòng 2", "\"dòng 1\r\ndòng 2\"")]
    [InlineData(CsvDelimiter.Comma, "chỉ\nLF", "\"chỉ\nLF\"")]
    [InlineData(CsvDelimiter.Comma, " có khoảng trắng đầu", "\" có khoảng trắng đầu\"")]
    [InlineData(CsvDelimiter.Comma, "khoảng trắng cuối ", "\"khoảng trắng cuối \"")]
    [InlineData(CsvDelimiter.Comma, "bình thường", "bình thường")]
    public async Task QuotesOnlyWhenTheCellNeedsIt_ForTheDelimiterInUse(CsvDelimiter delimiter, string value, string expected)
    {
        Body(await WriteAsync(delimiter, [CsvCell.Text(value)])).Should().Be(expected + "\r\n");
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+84901234567", "'+84901234567")]
    [InlineData("-50000", "'-50000")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tthụt lề", "'\tthụt lề")]
    [InlineData("\rCR đầu", "\"'\rCR đầu\"")]
    [InlineData("a=b", "a=b")]
    public async Task ATextCellStartingWithAFormulaCharacter_GetsAQuotePrefix(string value, string expected)
    {
        Body(await WriteAsync(CsvDelimiter.Comma, [CsvCell.Text(value)])).Should().Be(expected + "\r\n");
    }

    [Theory]
    [InlineData(CsvDelimiter.Comma)]
    [InlineData(CsvDelimiter.Semicolon)]
    public async Task AHyperlinkFormula_IsPrefixedThenQuoted_AndReadsBackAsPlainText(CsvDelimiter delimiter)
    {
        var bytes = await WriteAsync(delimiter, [CsvCell.Text("=HYPERLINK(\"x\")")]);

        Body(bytes).Should().Be("\"'=HYPERLINK(\"\"x\"\")\"\r\n");
        Parse(bytes, delimiter).Single().Should().Equal("'=HYPERLINK(\"x\")");
    }

    [Fact]
    public async Task Numbers_AreBareIntegers_EvenWhenNegative_AndNullTextIsEmpty()
    {
        Body(await WriteAsync(CsvDelimiter.Comma, [CsvCell.Number(-1_250_000), CsvCell.Number(0), CsvCell.Text(null), CsvCell.Text("")]))
            .Should().Be("-1250000,0,,\r\n");
    }

    [Theory]
    [InlineData(CsvDelimiter.Comma)]
    [InlineData(CsvDelimiter.Semicolon)]
    public async Task ARealCsvReader_GetsEveryCellBack(CsvDelimiter delimiter)
    {
        var bytes = await WriteAsync(delimiter,
            [CsvCell.Text("Ghi chú"), CsvCell.Text("Số tiền")],
            [CsvCell.Text("ăn trưa, cà phê; \"ngon\"\r\nlần 2"), CsvCell.Number(-30_000)],
            [CsvCell.Text("Phở bò Hà Nội"), CsvCell.Number(55_000)]);

        Parse(bytes, delimiter).Should().BeEquivalentTo(new[]
        {
            new[] { "Ghi chú", "Số tiền" },
            new[] { "ăn trưa, cà phê; \"ngon\"\r\nlần 2", "-30000" },
            new[] { "Phở bò Hà Nội", "55000" },
        }, o => o.WithStrictOrdering());
    }
}
