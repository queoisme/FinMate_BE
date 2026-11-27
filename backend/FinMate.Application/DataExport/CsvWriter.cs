using System.Text;

namespace FinMate.Application.DataExport;

/// <summary>S1: Excel đặt vùng Việt Nam tách cột bằng <c>;</c>, nơi khác bằng <c>,</c>.</summary>
public enum CsvDelimiter
{
    Comma,
    Semicolon,
}

/// <summary>Một ô CSV. Ô chữ được chống injection; ô số là số nguyên in trần (kể cả số âm), không bao giờ bị thêm tiền tố.</summary>
public readonly record struct CsvCell
{
    private CsvCell(string? text, long? number)
    {
        TextValue = text;
        NumberValue = number;
    }

    public string? TextValue { get; }
    public long? NumberValue { get; }

    public static CsvCell Text(string? value) => new(value, null);

    public static CsvCell Number(long value) => new(null, value);
}

/// <summary>
/// Ghi CSV theo luồng (Phase 91, BR-EXP-02/03, S1): UTF-8 có BOM để Excel nhận ra tiếng Việt, <b>không</b> có dòng <c>sep=</c> (có nó Excel bỏ qua BOM),
/// dấu phân cách là tham số, xuống dòng CRLF, RFC 4180: ô được bọc ngoặc kép khi chứa dấu phân cách đang dùng, <c>"</c>, CR, LF hoặc có khoảng trắng
/// đầu/cuối; <c>"</c> nhân đôi. Ô chữ bắt đầu bằng <c>= + - @</c>, tab hay CR được thêm <c>'</c> phía trước (trước khi bọc ngoặc) để bảng tính không chạy
/// như công thức. Không giữ cả file trong bộ nhớ.
/// </summary>
public sealed class CsvWriter : IAsyncDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    private readonly Stream _stream;
    private readonly StreamWriter _writer;
    private readonly char _delimiter;
    private bool _bomWritten;

    public CsvWriter(Stream stream, CsvDelimiter delimiter)
    {
        _stream = stream;
        _delimiter = delimiter == CsvDelimiter.Semicolon ? ';' : ',';
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 16 * 1024, leaveOpen: true)
        {
            NewLine = "\r\n",
        };
    }

    public async Task WriteRowAsync(IReadOnlyList<CsvCell> cells, CancellationToken ct = default)
    {
        await EnsureBomAsync(ct);
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                await _writer.WriteAsync(_delimiter);
            }

            await _writer.WriteAsync(Format(cells[i], _delimiter));
        }

        await _writer.WriteAsync("\r\n");
    }

    public async ValueTask DisposeAsync()
    {
        await EnsureBomAsync(CancellationToken.None);
        await _writer.FlushAsync();
        await _writer.DisposeAsync();
    }

    /// <summary>Một ô đã định dạng, chưa kèm dấu phân cách. Dùng chung cho mọi nơi ghi CSV.</summary>
    public static string Format(CsvCell cell, char delimiter)
    {
        if (cell.NumberValue is { } number)
        {
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var text = cell.TextValue;
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (Array.IndexOf(FormulaStarts, text[0]) >= 0)
        {
            text = "'" + text;
        }

        return NeedsQuotes(text, delimiter) ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    private static bool NeedsQuotes(string text, char delimiter)
        => text.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]);

    private async Task EnsureBomAsync(CancellationToken ct)
    {
        if (!_bomWritten)
        {
            _bomWritten = true;
            await _stream.WriteAsync(Bom, ct);
        }
    }
}
