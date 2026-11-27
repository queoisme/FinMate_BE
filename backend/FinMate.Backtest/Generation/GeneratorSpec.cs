using FinMate.Application.Common;

namespace FinMate.Backtest.Generation;

/// <summary>
/// Hằng số của đặc tả bộ sinh (<c>docs/evaluation/forecast-backtest-generator-spec.md</c>). <see cref="Version"/> phải bằng phiên bản của tài liệu;
/// đổi bất kỳ hằng nào ở đây là đổi đặc tả và phải tăng phiên bản (§0).
/// </summary>
public static class GeneratorSpec
{
    public const string Version = "1.1";

    public const ulong DefaultRootSeed = 20261127;

    public const int SeedsPerPersona = 20;

    public static readonly DateOnly DataStart = new(2025, 1, 1);
    public static readonly DateOnly EvaluationStart = new(2025, 7, 1);
    public static readonly DateOnly EvaluationEnd = new(2026, 6, 30);
    public static readonly DateOnly DataEnd = new(2026, 7, 31);

    /// <summary>Mùng 1 Tết (§3, có nguồn trong đặc tả).</summary>
    public static readonly DateOnly Tet2025 = new(2025, 1, 29);
    public static readonly DateOnly Tet2026 = new(2026, 2, 17);

    /// <summary>K5h: khoảng ngày lễ và hệ số chọn trước (D17).</summary>
    public static readonly DateOnly HolidayConfigStart = Tet2026.AddDays(-7);
    public static readonly DateOnly HolidayConfigEnd = Tet2026.AddDays(7);
    public const int HolidayConfigMultiplierPercent = 150;

    /// <summary>B-mean30 (§9).</summary>
    public const int MeanBaselineDays = 30;

    /// <summary>Namespace của mọi id tất định trong backtest.</summary>
    public static readonly Guid IdNamespace = new("5b6f0e2c-96a1-4d3e-9b1f-0b7a6c2d9e96");

    public static readonly IReadOnlyList<string> Personas = [Persona.StableWorker, Persona.Freelancer, Persona.Student, Persona.Family, Persona.SmallTrader];

    public static Guid UserId(string persona, int seedIndex) => DeterministicGuid.V5(IdNamespace, $"user|{persona}|{seedIndex}");

    public static Guid CategoryId(string slug) => DeterministicGuid.V5(IdNamespace, $"category:{slug}");

    public static DateTimeOffset At(DateOnly date, int minuteOfDay)
        => new(date.ToDateTime(new TimeOnly(minuteOfDay / 60, minuteOfDay % 60)), VietnamTime.Offset);
}

public static class Persona
{
    public const string StableWorker = "stable_worker";
    public const string Freelancer = "freelancer";
    public const string Student = "student";
    public const string Family = "family";
    public const string SmallTrader = "small_trader";
}

public static class ScenarioCodes
{
    public const string K0 = "K0";
    public const string K1 = "K1";
    public const string K2 = "K2";
    public const string K3 = "K3";
    public const string K4 = "K4";
    public const string K5 = "K5";
    public const string K5h = "K5h";
    public const string K6 = "K6";

    /// <summary>Kịch bản của tầng 1. K5h dùng chung dữ liệu với K5, chỉ khác cấu hình ngày lễ.</summary>
    public static readonly IReadOnlyList<string> Tier1 = [K0, K1, K2, K3, K4, K5, K5h, K6];
}

/// <summary>Danh mục hệ thống có thật trong <c>CategorySeeder</c> (§4).</summary>
public static class Slugs
{
    public const string Food = "food";
    public const string Transport = "transport";
    public const string Shopping = "shopping";
    public const string Entertainment = "entertainment";
    public const string Health = "health";
    public const string Education = "education";
    public const string Housing = "housing";
    public const string Bills = "bills";
    public const string Family = "family";
    public const string Income = "income";
    public const string Loan = "loan";
    public const string Other = "other";
}
