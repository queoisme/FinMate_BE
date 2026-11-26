using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;

namespace FinMate.Application.Kpi;

public enum KpiGranularity
{
    Day,
    Week,
    Month,
}

public static class KpiErrorCodes
{
    public const string InvalidRange = "KPI_INVALID_RANGE";
}

/// <summary>Một kỳ của dashboard, hai đầu là ngày VN (tính cả hai). <see cref="Partial"/>: kỳ bị cắt bởi <c>from</c>/<c>to</c> hoặc chưa kết thúc.</summary>
public sealed record KpiPeriod(DateOnly Start, DateOnly End, bool Partial)
{
    /// <summary>[00:00 VN của ngày đầu, 00:00 VN của ngày sau ngày cuối), offset 0 để Npgsql nhận.</summary>
    public DateTimeOffset StartUtc => KpiPeriods.StartOfDayUtc(Start);
    public DateTimeOffset EndUtc => KpiPeriods.StartOfDayUtc(End.AddDays(1));

    public bool Contains(DateOnly day) => day >= Start && day <= End;
}

public static class KpiPeriods
{
    /// <summary>BR-KPI-06: khoảng tối đa, tính cả hai đầu.</summary>
    public const int MaxDays = 366;

    public static DateTimeOffset StartOfDayUtc(DateOnly day)
        => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), VietnamTime.Offset).ToUniversalTime();

    /// <summary>Kiểm tham số của <c>/ops/kpi/summary</c>. Sai thì 422 <see cref="KpiErrorCodes.InvalidRange"/>.</summary>
    public static (DateOnly From, DateOnly To, KpiGranularity Granularity) Validate(DateOnly? from, DateOnly? to, string? granularity, DateOnly today)
    {
        if (from is not { } f || to is not { } t)
        {
            throw Invalid("Cần cả from và to (yyyy-MM-dd).");
        }

        if (f > t)
        {
            throw Invalid("from phải trước hoặc bằng to.");
        }

        if (t > today)
        {
            throw Invalid("to không được sau hôm nay.");
        }

        if (t.DayNumber - f.DayNumber + 1 > MaxDays)
        {
            throw Invalid($"Khoảng tối đa {MaxDays} ngày.");
        }

        var parsed = string.IsNullOrWhiteSpace(granularity)
            ? KpiGranularity.Day
            : Enum.TryParse<KpiGranularity>(granularity, ignoreCase: true, out var g) && Enum.IsDefined(g) && !int.TryParse(granularity, out _)
                ? g
                : throw Invalid("granularity là day, week hoặc month.");
        return (f, t, parsed);
    }

    /// <summary>Chia [from, to] thành kỳ. Tuần bắt đầu thứ Hai; tháng là tháng lịch. Kỳ đầu và cuối bị cắt theo khoảng.</summary>
    public static IReadOnlyList<KpiPeriod> Split(DateOnly from, DateOnly to, KpiGranularity granularity, DateOnly today)
    {
        var periods = new List<KpiPeriod>();
        var cursor = from;
        while (cursor <= to)
        {
            var (fullStart, fullEnd) = FullPeriodOf(cursor, granularity);
            var start = fullStart < from ? from : fullStart;
            var end = fullEnd > to ? to : fullEnd;
            var partial = start != fullStart || end != fullEnd || fullEnd >= today;
            periods.Add(new KpiPeriod(start, end, partial));
            cursor = end.AddDays(1);
        }

        return periods;
    }

    private static (DateOnly Start, DateOnly End) FullPeriodOf(DateOnly day, KpiGranularity granularity)
    {
        switch (granularity)
        {
            case KpiGranularity.Week:
                var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
                return (monday, monday.AddDays(6));
            case KpiGranularity.Month:
                var first = new DateOnly(day.Year, day.Month, 1);
                return (first, first.AddMonths(1).AddDays(-1));
            default:
                return (day, day);
        }
    }

    private static BusinessRuleException Invalid(string message) => new(KpiErrorCodes.InvalidRange, message);
}
