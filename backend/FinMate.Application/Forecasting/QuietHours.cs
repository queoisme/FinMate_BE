using FinMate.Application.Common;

namespace FinMate.Application.Forecasting;

/// <summary>Giờ yên lặng theo phút trong ngày (giờ VN). Khoảng được phép qua nửa đêm (22:00–07:00). Bắt đầu = kết thúc nghĩa là không có giờ yên lặng.</summary>
public readonly record struct QuietHours(int StartMinute, int EndMinute)
{
    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 24 * MinutesPerHour;

    public bool Contains(DateTimeOffset instant)
    {
        var minute = MinuteOfDay(instant);
        if (StartMinute == EndMinute)
        {
            return false;
        }

        return StartMinute < EndMinute
            ? minute >= StartMinute && minute < EndMinute
            : minute >= StartMinute || minute < EndMinute;
    }

    /// <summary>Lúc giờ yên lặng đang diễn ra kết thúc (giờ VN). Chỉ có nghĩa khi <see cref="Contains"/> đúng.</summary>
    public DateTimeOffset EndAfter(DateTimeOffset instant)
    {
        var local = VietnamTime.ToLocal(instant);
        var endToday = new DateTimeOffset(local.Date, VietnamTime.Offset).AddMinutes(EndMinute);
        return endToday > local ? endToday : endToday.AddDays(1);
    }

    public static string Format(int minute) => $"{minute / MinutesPerHour:00}:{minute % MinutesPerHour:00}";

    /// <summary>"HH:mm" → phút trong ngày; null nếu sai định dạng.</summary>
    public static int? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !TimeOnly.TryParseExact(text.Trim(), "HH:mm", out var time))
        {
            return null;
        }

        return (time.Hour * MinutesPerHour) + time.Minute;
    }

    private static int MinuteOfDay(DateTimeOffset instant)
    {
        var local = VietnamTime.ToLocal(instant);
        return ((local.Hour * MinutesPerHour) + local.Minute) % MinutesPerDay;
    }
}
