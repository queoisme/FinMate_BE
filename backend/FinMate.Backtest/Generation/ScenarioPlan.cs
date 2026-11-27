using FinMate.Domain.Enums;

namespace FinMate.Backtest.Generation;

public sealed record OneOffExpense(DateOnly Date, int MinuteOfDay, long Cents, string Slug, string Merchant);

public sealed record SkippedPeriod(string Item, DateOnly Month, bool PayBothNextMonth);

public sealed record PriceChangePlan(string Item, DateOnly FromMonth, double Factor);

/// <summary>
/// Tham số của một lớp phủ kịch bản (đặc tả §6), rút từ luồng <c>scenario.{mã}</c>. K0 là bản rỗng. K5h dùng chung dữ liệu với K5
/// (luồng <c>scenario.K5</c>), chỉ khác cấu hình ngày lễ của mô hình.
/// </summary>
public sealed record ScenarioPlan(string Code)
{
    public const string ProjectIncome = "projects";
    public const string SalesIncome = "sales";

    public IReadOnlyList<OneOffExpense> OneOffs { get; init; } = [];

    /// <summary>K2: tên khoản thu định kỳ, hoặc <see cref="ProjectIncome"/> / <see cref="SalesIncome"/>.</summary>
    public string? IncomeTarget { get; init; }

    public DateOnly? LateMonth { get; init; }

    public int LateDays { get; init; }

    public double IncomeFactor { get; init; } = 1.0;

    public IReadOnlyList<SkippedPeriod> Skips { get; init; } = [];

    public IReadOnlyList<PriceChangePlan> PriceChanges { get; init; } = [];

    public double TetMultiplier { get; init; } = 1.0;

    /// <summary>K5: số ngày trước mùng 1 mà thưởng Tết của từng nguồn lương về.</summary>
    public IReadOnlyList<(string Item, int DaysBefore)> TetBonuses { get; init; } = [];

    public long LixiCents { get; init; }

    public DateOnly? LixiDay { get; init; }

    public DateOnly? StartDay { get; init; }

    public static readonly DateOnly TetSpendStart = GeneratorSpec.Tet2026.AddDays(-14);
    public static readonly DateOnly TetSpendEnd = GeneratorSpec.Tet2026.AddDays(7);

    public bool IsTet => Code is ScenarioCodes.K5 or ScenarioCodes.K5h;

    /// <summary>Hệ số nhân vào λ của chi linh hoạt ngày <paramref name="date"/> do kịch bản (§5.1 v1.1, §6).</summary>
    public double FlexLambdaFactor(DateOnly date) => IsTet && date >= TetSpendStart && date <= TetSpendEnd ? TetMultiplier : 1.0;

    public double IncomeFactorOn(DateOnly due) => LateMonth is { } m && due >= m.AddMonths(2) ? IncomeFactor : 1.0;
}

public static class ScenarioPlans
{
    private const long Million = 1_000_000;
    private const int MinGapDays = 60;

    private static readonly DateOnly WindowFirstMonth = new(2025, 8, 1);

    private static readonly IReadOnlyList<(string Slug, string Merchant)> OneOffKinds =
        [(Slugs.Health, "Bệnh viện"), (Slugs.Other, "Sửa chữa"), (Slugs.Shopping, "Điện máy")];

    private static readonly IReadOnlyList<string> BigFixedItems = ["Phần mềm thiết kế", "Tiền nhà", "Phòng trọ", "Thuê sạp", "Trả góp nhà"];

    public static ScenarioPlan Draw(string code, PersonaProfile profile, Rng s) => code switch
    {
        ScenarioCodes.K0 => new ScenarioPlan(code),
        ScenarioCodes.K1 => K1(s),
        ScenarioCodes.K2 => K2(profile, s),
        ScenarioCodes.K3 => K3(profile, s),
        ScenarioCodes.K4 => K4(profile, s),
        ScenarioCodes.K5 or ScenarioCodes.K5h => K5(code, profile, s),
        ScenarioCodes.K6 => new ScenarioPlan(code) { StartDay = DayIn(s, new DateOnly(2025, 7, 15), new DateOnly(2026, 4, 15)) },
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    /// <summary>Tên luồng của kịch bản: K5h dùng luồng của K5 để có cùng dữ liệu.</summary>
    public static string StreamCode(string code) => code == ScenarioCodes.K5h ? ScenarioCodes.K5 : code;

    private static ScenarioPlan K1(Rng s)
    {
        var first = DayIn(s, GeneratorSpec.EvaluationStart, GeneratorSpec.EvaluationEnd);
        var second = DayIn(s, GeneratorSpec.EvaluationStart, GeneratorSpec.EvaluationEnd);
        while (Math.Abs(second.DayNumber - first.DayNumber) < MinGapDays)
        {
            second = DayIn(s, GeneratorSpec.EvaluationStart, GeneratorSpec.EvaluationEnd);
        }

        return new ScenarioPlan(ScenarioCodes.K1) { OneOffs = [OneOff(s, first), OneOff(s, second)] };
    }

    private static OneOffExpense OneOff(Rng s, DateOnly date)
    {
        var kind = s.Pick(OneOffKinds);
        return new OneOffExpense(date, s.Int(10 * 60, 18 * 60), s.Money(3 * Million, 10 * Million), kind.Slug, kind.Merchant);
    }

    private static ScenarioPlan K2(PersonaProfile profile, Rng s)
    {
        var target = profile.Persona switch
        {
            Persona.Freelancer => ScenarioPlan.ProjectIncome,
            Persona.SmallTrader => ScenarioPlan.SalesIncome,
            _ => profile.Recurring.Where(r => r.Direction == RecurringDirection.Inflow && !r.Volatile).MaxBy(r => r.BaseCents)!.Name,
        };
        var month = WindowFirstMonth.AddMonths(s.Int(0, 7));   // 2025-08 → 2026-03
        var late = target == ScenarioPlan.ProjectIncome ? s.Int(7, 14) : s.Int(3, 10);
        var sign = s.Chance(0.5) ? 1 : -1;
        var factor = 1 + (sign * s.Uniform(0.10, 0.30));
        return new ScenarioPlan(ScenarioCodes.K2) { IncomeTarget = target, LateMonth = month, LateDays = late, IncomeFactor = factor };
    }

    private static ScenarioPlan K3(PersonaProfile profile, Rng s)
    {
        var eligible = profile.Recurring
            .Where(r => r.Cadence == RecurringCadence.Monthly && r.Direction == RecurringDirection.Outflow && r.Label == TxLabel.Recurring)
            .Select(r => r.Name)
            .ToList();
        var count = Math.Min(eligible.Count, s.Int(1, 2));
        var skips = new List<SkippedPeriod>();
        for (var i = 0; i < count; i++)
        {
            var item = s.Pick(eligible);
            eligible.Remove(item);
            skips.Add(new SkippedPeriod(item, WindowFirstMonth.AddMonths(s.Int(0, 8)), s.Chance(0.5)));   // 2025-08 → 2026-04
        }

        return new ScenarioPlan(ScenarioCodes.K3) { Skips = skips };
    }

    private static ScenarioPlan K4(PersonaProfile profile, Rng s)
    {
        var names = profile.Recurring.Select(r => r.Name).ToHashSet();
        var small = names.Contains("Internet") ? "Internet" : "Cước di động";
        var big = BigFixedItems.First(names.Contains);
        return new ScenarioPlan(ScenarioCodes.K4)
        {
            PriceChanges =
            [
                new PriceChangePlan(small, WindowFirstMonth.AddMonths(s.Int(0, 8)), 1.05),
                new PriceChangePlan(big, WindowFirstMonth.AddMonths(s.Int(0, 8)), 1.25),
            ],
        };
    }

    private static ScenarioPlan K5(string code, PersonaProfile profile, Rng s)
    {
        var multiplier = s.Uniform(1.5, 2.5);
        var bonuses = profile.Persona is Persona.StableWorker or Persona.Family
            ? profile.Recurring.Where(r => r.Direction == RecurringDirection.Inflow).Select(r => (r.Name, s.Int(3, 10))).ToList()
            : [];
        var lixi = profile.Persona == Persona.Student ? s.Money(Million, 3 * Million) : 0;
        DateOnly? lixiDay = profile.Persona == Persona.Student ? GeneratorSpec.Tet2026.AddDays(s.Int(0, 2)) : null;
        return new ScenarioPlan(code) { TetMultiplier = multiplier, TetBonuses = bonuses, LixiCents = lixi, LixiDay = lixiDay };
    }

    private static DateOnly DayIn(Rng s, DateOnly from, DateOnly to) => from.AddDays(s.Int(0, to.DayNumber - from.DayNumber));
}
