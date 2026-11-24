using FinMate.Domain.Entities;

namespace FinMate.Application.FinanceMethods;

/// <summary>Một nhóm trong bảng phân bổ của phương pháp. <paramref name="Pct"/> là giá trị HIỆU LỰC của tham số <paramref name="Key"/>; null khi nhóm không gắn tham số.</summary>
public sealed record PlanGroup(string? Key, string Label, string Kind, IReadOnlyList<string> Categories, int? Pct);

/// <param name="FixedByCategory">Khoản cố định khai trong khảo sát, theo slug danh mục (tiền nhà, hóa đơn). Không bao giờ bị co.</param>
/// <param name="FixedOtherVnd">Khoản cố định không thuộc danh mục chi nào (trả nợ).</param>
/// <param name="DeclaredFlexible">Chi linh hoạt khai báo theo slug, đã đổi từ khoảng % ra đồng và làm tròn xuống nghìn.</param>
/// <param name="Groups">Bảng phân bổ; rỗng = một nhóm chi không trần chứa mọi danh mục.</param>
public sealed record PlanInput(
    long IncomeVnd,
    IReadOnlyDictionary<string, long> FixedByCategory,
    long FixedOtherVnd,
    IReadOnlyDictionary<string, long> DeclaredFlexible,
    IReadOnlyList<PlanGroup> Groups);

/// <param name="DeclaredVnd">Cố định + linh hoạt người dùng khai.</param>
/// <param name="LimitVnd">Hạn mức đề xuất = cố định + linh hoạt sau khi co cho vừa trần nhóm.</param>
public sealed record PlanCategoryLine(string Slug, string? GroupKey, long FixedVnd, long DeclaredVnd, long LimitVnd, long ReducedByVnd);

public sealed record FinancialPlan(
    long IncomeVnd,
    long FixedVnd,
    long PlannedSpendVnd,
    long SurplusVnd,
    long SaveTargetVnd,
    IReadOnlyList<PlanCategoryLine> Categories,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Kế hoạch tháng từ số liệu khảo sát và bảng phân bổ của phương pháp (đặc tả 2026-11-23-onboarding-financial-plan-design.md §6). Hàm thuần, tất định:
/// mọi số tiền do code tính, không bao giờ do LLM. Mọi số làm tròn XUỐNG nghìn đồng; phần lẻ rơi vào số dư nên kế hoạch + dư luôn bằng thu nhập.
/// </summary>
public static class FinancialPlanCalculator
{
    public const string WarningDeficit = "deficit";
    public const string WarningBelowSaveTarget = "below_save_target";
    public const string WarningFixedExceedsGroupPrefix = "fixed_exceeds_group:";

    private const long Rounding = 1_000;

    public static FinancialPlan Calculate(PlanInput input)
    {
        var income = input.IncomeVnd;
        var warnings = new List<string>();
        var slugs = input.FixedByCategory.Keys.Union(input.DeclaredFlexible.Keys).ToList();
        long FixedOf(string slug) => input.FixedByCategory.GetValueOrDefault(slug);
        long FlexOf(string slug) => input.DeclaredFlexible.GetValueOrDefault(slug);

        // Mặc định: giữ nguyên chi khai báo (không đẩy người dùng tiêu nhiều lên cho đủ trần).
        var flexLimit = slugs.ToDictionary(s => s, FlexOf);
        var groupOf = new Dictionary<string, string?>();

        foreach (var group in input.Groups.Where(g => g.Kind == AllocationKinds.Spend))
        {
            var members = group.Categories.Where(slugs.Contains).ToList();
            foreach (var slug in members)
            {
                groupOf[slug] = group.Key;
            }

            if (group.Key is null || group.Pct is not { } pct)
            {
                continue;
            }

            var cap = RoundDown(Percent(income, pct));
            var fixedTotal = members.Sum(FixedOf);
            var flexTotal = members.Sum(FlexOf);
            if (fixedTotal + flexTotal <= cap)
            {
                continue;
            }

            if (fixedTotal > cap)
            {
                warnings.Add(WarningFixedExceedsGroupPrefix + group.Key);
            }

            var room = Math.Max(0, cap - fixedTotal);
            foreach (var slug in members.Where(s => FlexOf(s) > 0))
            {
                // Int128: khai báo (≤ 1e11) × room (≤ 1e11) vượt long.
                flexLimit[slug] = RoundDown((long)((Int128)FlexOf(slug) * room / flexTotal));
            }
        }

        var lines = slugs
            .Select(slug =>
            {
                var declared = FixedOf(slug) + FlexOf(slug);
                var limit = FixedOf(slug) + flexLimit[slug];
                return new PlanCategoryLine(slug, groupOf.GetValueOrDefault(slug), FixedOf(slug), declared, limit, declared - limit);
            })
            .OrderByDescending(l => l.LimitVnd)
            .ThenBy(l => l.Slug, StringComparer.Ordinal)
            .ToList();

        var plannedSpend = lines.Sum(l => l.LimitVnd) + input.FixedOtherVnd;
        var surplus = income - plannedSpend;
        var saveTarget = input.Groups
            .Where(g => g.Kind == AllocationKinds.Save && g.Pct is not null)
            .Sum(g => RoundDown(Percent(income, g.Pct!.Value)));

        if (surplus < 0)
        {
            warnings.Add(WarningDeficit);
        }
        else if (surplus < saveTarget)
        {
            warnings.Add(WarningBelowSaveTarget);
        }

        return new FinancialPlan(
            income,
            input.FixedByCategory.Values.Sum() + input.FixedOtherVnd,
            plannedSpend,
            surplus,
            saveTarget,
            lines,
            warnings);
    }

    private static long Percent(long amount, int pct) => (long)((Int128)amount * pct / 100);

    private static long RoundDown(long amount) => amount / Rounding * Rounding;
}
