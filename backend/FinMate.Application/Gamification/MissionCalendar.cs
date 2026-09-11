using FinMate.Application.Common;
using FinMate.Domain.Enums;

namespace FinMate.Application.Gamification;

/// <summary>Biên chu kỳ của mission theo lịch VN.</summary>
public static class MissionCalendar
{
    /// <summary>
    /// Mốc quy ước cho mission one_time: chúng không có chu kỳ, nhưng unique index
    /// <c>(user_id, mission_id, period_start)</c> cần một giá trị cố định để mỗi user chỉ có
    /// đúng một dòng cho cả đời.
    /// </summary>
    public static readonly DateOnly OneTimeEpoch = new(2000, 1, 1);

    public static (DateOnly Start, DateOnly End) PeriodFor(MissionPeriodType periodType, DateOnly today) => periodType switch
    {
        MissionPeriodType.Daily => (today, today),
        MissionPeriodType.Weekly => WeekOf(today),
        MissionPeriodType.OneTime => (OneTimeEpoch, DateOnly.MaxValue),
        _ => (today, today),
    };

    /// <summary>Tuần bắt đầu từ thứ Hai.</summary>
    public static (DateOnly Start, DateOnly End) WeekOf(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        var start = date.AddDays(-daysSinceMonday);
        return (start, start.AddDays(6));
    }

    public static DateOnly Today() => VietnamTime.Today();
}
