using System.Globalization;
using System.Text;
using FinMate.Application.Common.Exceptions;

namespace FinMate.Application.Community;

/// <summary>Con trỏ trang. "Mới nhất" dùng vị trí bài cuối để bài mới đăng không làm lặp trang; "nhiều vote" và danh sách quán dùng số thứ tự vì thứ tự tính khi đọc.</summary>
public static class CommunityCursor
{
    private const string KeyPrefix = "k";
    private const string OffsetPrefix = "o";
    private const char Separator = '|';
    private const int MaxOffset = 2000;

    public static string FromPosition(DateTimeOffset createdAt, Guid id)
        => Encode($"{KeyPrefix}{Separator}{createdAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}{Separator}{id:N}");

    public static string FromOffset(int offset) => Encode($"{OffsetPrefix}{Separator}{offset.ToString(CultureInfo.InvariantCulture)}");

    public static (DateTimeOffset CreatedAt, Guid Id)? ReadPosition(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        var parts = Decode(cursor);
        if (parts is [KeyPrefix, var ticks, var id]
            && long.TryParse(ticks, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value <= DateTimeOffset.MaxValue.UtcTicks
            && Guid.TryParseExact(id, "N", out var guid))
        {
            return (new DateTimeOffset(value, TimeSpan.Zero), guid);
        }

        throw Invalid();
    }

    public static int ReadOffset(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        var parts = Decode(cursor);
        if (parts is [OffsetPrefix, var text]
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
            && offset <= MaxOffset)
        {
            return offset;
        }

        throw Invalid();
    }

    private static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static string[] Decode(string cursor)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(Separator);
        }
        catch (FormatException)
        {
            throw Invalid();
        }
    }

    private static BusinessRuleException Invalid() => new(CommunityErrorCodes.InvalidInput, "Con trỏ trang không hợp lệ.");
}
