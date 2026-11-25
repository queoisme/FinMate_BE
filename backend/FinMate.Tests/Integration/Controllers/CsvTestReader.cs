using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Đọc CSV bằng một trình đọc thật (D18: <see cref="TextFieldParser"/> có sẵn trong .NET), trả từng dòng thành từ điển theo tên cột.</summary>
internal static class CsvTestReader
{
    public static List<Dictionary<string, string>> Read(byte[] bytes, char delimiter = ',')
    {
        using var parser = new TextFieldParser(new MemoryStream(bytes), Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(delimiter.ToString());
        var header = parser.ReadFields()!;
        var rows = new List<Dictionary<string, string>>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()!;
            rows.Add(header.Zip(fields).ToDictionary(p => p.First, p => p.Second));
        }

        return rows;
    }
}
