using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Application.FinanceMethods;

public sealed record JarCategoryDto(string Slug, string Name);

/// <param name="Key">Tham số kiểu % của cách quản lý quyết định phần chia cho hũ.</param>
/// <param name="AllocationPct">Phần trăm thu nhập đang có hiệu lực cho hũ (người dùng chỉnh được ở màn Cách quản lý).</param>
/// <param name="AllocatedCents">Số tiền được chia = thu nhập × %; null khi chưa khai thu nhập ("chưa biết", không phải 0).</param>
/// <param name="SpentCents">Chi tiêu tháng này của các danh mục thuộc hũ.</param>
/// <param name="RemainingCents">Còn lại = được chia − đã chi (âm khi vượt); null khi chưa biết số được chia.</param>
/// <param name="UsedPct">Đã dùng bao nhiêu % số được chia (làm tròn xuống); null khi không có số được chia.</param>
/// <param name="Level"><c>none</c> (không có số được chia), <c>ok</c>, <c>t1</c>, <c>t2</c> hoặc <c>over</c>: so với ngưỡng cảnh báo của chính người dùng.</param>
public sealed record JarDto(
    string Key,
    string Label,
    int AllocationPct,
    long? AllocatedCents,
    long SpentCents,
    long? RemainingCents,
    int? UsedPct,
    string Level,
    IReadOnlyList<JarCategoryDto> Categories);

/// <summary>Các hũ của MỘT cách quản lý người dùng đang dùng, tính cho tháng hiện tại theo giờ Việt Nam.</summary>
/// <param name="UnassignedSpentCents">Chi tiêu tháng này nằm ngoài mọi hũ (danh mục chưa gán vào hũ, danh mục riêng của người dùng, chưa phân loại).</param>
public sealed record JarSetDto(
    Guid UserMethodId,
    string MethodCode,
    string MethodName,
    string? Title,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long? IncomeCents,
    IReadOnlyList<JarDto> Jars,
    long UnassignedSpentCents,
    int T1Pct,
    int T2Pct);

public interface IJarService
{
    /// <summary>Hũ của từng cách quản lý đang dùng có khối "hũ" (cách chính trước). Rỗng khi không có cách nào như vậy.</summary>
    Task<IReadOnlyList<JarSetDto>> GetMineAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Chia chi tiêu tháng này theo hũ. Dùng đúng nguồn của báo cáo "chi theo danh mục" (<see cref="IReportRepository.GetCategorySpendAsync"/>: đã loại khoản vay/mượn và chuyển khoản nội bộ)
/// nên số của hũ khớp báo cáo. Danh mục thuộc hũ được ghép theo ID của danh mục HỆ THỐNG, không theo slug, để danh mục riêng của người dùng trùng tên không bị tính nhầm vào hũ.
/// </summary>
public class JarService : IJarService
{
    private readonly IFinanceMethodRepository _methods;
    private readonly IUserMethodRepository _userMethods;
    private readonly IUserRepository _users;
    private readonly IReportRepository _reports;
    private readonly ICategoryRepository _categories;
    private readonly IAlertThresholdProvider _thresholds;

    public JarService(
        IFinanceMethodRepository methods,
        IUserMethodRepository userMethods,
        IUserRepository users,
        IReportRepository reports,
        ICategoryRepository categories,
        IAlertThresholdProvider thresholds)
    {
        _methods = methods;
        _userMethods = userMethods;
        _users = users;
        _reports = reports;
        _categories = categories;
        _thresholds = thresholds;
    }

    private sealed record JarLayout(string Key, string? Label, IReadOnlyList<string> Categories);

    private sealed record JarsBlock(string? Title, IReadOnlyList<JarLayout> Jars);

    public async Task<IReadOnlyList<JarSetDto>> GetMineAsync(Guid userId, CancellationToken ct = default)
    {
        var active = (await _userMethods.GetActiveAsync(userId, ct)).OrderBy(a => a.Role).ToList();   // cách chính trước
        if (active.Count == 0)
        {
            return [];
        }

        var methods = (await _methods.GetMethodsByIdsAsync(active.Select(a => a.MethodId), ct)).ToDictionary(m => m.Id);
        var withJars = active
            .Where(a => methods.ContainsKey(a.MethodId))
            .Select(a => (UserMethod: a, Method: methods[a.MethodId], Block: ReadJarsBlock(methods[a.MethodId].HomeLayout)))
            .Where(x => x.Block is not null)
            .ToList();
        if (withJars.Count == 0)
        {
            return [];
        }

        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        long? income = user.MonthlyIncomeCents is > 0 ? user.MonthlyIncomeCents : null;
        var thresholds = (await _thresholds.ResolveAsync(userId, ct)).Thresholds;

        var (year, month) = VietnamTime.YearMonthOf(DateTimeOffset.UtcNow);
        var (periodStart, periodEnd) = VietnamTime.MonthRange(year, month);
        var first = new DateOnly(year, month, 1);
        var spend = await _reports.GetCategorySpendAsync(userId, first, first.AddMonths(1).AddDays(-1), ct);
        var systemCategories = (await _categories.GetSystemListAsync(includeInactive: true, ct)).ToDictionary(c => c.Slug);

        var result = new List<JarSetDto>();
        foreach (var (userMethod, method, block) in withJars)
        {
            var defs = MethodParams.ParseDefs(method.ParamDefs);
            var effective = MethodParams.Effective(defs, MethodParams.ParseOverrides(userMethod.Params));
            var assignedIds = new HashSet<Guid>();

            var jars = new List<JarDto>();
            foreach (var jar in block!.Jars)
            {
                var def = defs.FirstOrDefault(d => d.Key == jar.Key);
                if (def is null)
                {
                    continue;   // tham số đã bị admin xóa sau khi lưu bố cục: bỏ hũ này thay vì làm hỏng cả trang (admin bị chặn từ lúc lưu)
                }

                var pct = (int)effective.GetValueOrDefault(jar.Key, def.Default);
                var categories = jar.Categories.Where(systemCategories.ContainsKey).Select(slug => systemCategories[slug]).ToList();
                var ids = categories.Select(c => c.Id).ToHashSet();
                assignedIds.UnionWith(ids);

                var spent = spend.Where(s => s.CategoryId is { } id && ids.Contains(id)).Sum(s => s.SpentCents);
                long? allocated = income is { } total ? total * pct / 100 : null;
                jars.Add(new JarDto(
                    jar.Key,
                    string.IsNullOrWhiteSpace(jar.Label) ? def.Label : jar.Label!,
                    pct,
                    allocated,
                    spent,
                    allocated is { } a ? a - spent : null,
                    allocated is > 0 ? (int)(spent * 100 / allocated.Value) : null,
                    LevelOf(spent, allocated, thresholds.T1Pct, thresholds.T2Pct),
                    categories.Select(c => new JarCategoryDto(c.Slug, c.Name)).ToList()));
            }

            var unassigned = spend.Where(s => s.CategoryId is not { } id || !assignedIds.Contains(id)).Sum(s => s.SpentCents);
            result.Add(new JarSetDto(
                userMethod.Id,
                method.Code,
                method.Name,
                block.Title,
                periodStart.ToOffset(VietnamTime.Offset),
                periodEnd.ToOffset(VietnamTime.Offset),
                income,
                jars,
                unassigned,
                thresholds.T1Pct,
                thresholds.T2Pct));
        }

        return result;
    }

    /// <summary>So với ngưỡng của người dùng bằng phép nhân số nguyên, cùng cách <c>BudgetAlertEvaluator</c> so (không có số thực).</summary>
    private static string LevelOf(long spent, long? allocated, int t1, int t2)
    {
        if (allocated is not > 0)
        {
            return "none";
        }

        if (spent >= allocated.Value)
        {
            return "over";
        }

        if (spent * 100 >= allocated.Value * t2)
        {
            return "t2";
        }

        return spent * 100 >= allocated.Value * t1 ? "t1" : "ok";
    }

    /// <summary>Khối "hũ" của bố cục (đã được admin kiểm khi lưu); hỏng hoặc không có thì null, không bao giờ ném.</summary>
    private static JarsBlock? ReadJarsBlock(string homeLayout)
    {
        try
        {
            using var doc = JsonDocument.Parse(homeLayout);
            foreach (var block in doc.RootElement.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var type) || type.GetString() != FinanceHomeBlocks.Jars
                    || !block.TryGetProperty("jars", out var jars) || jars.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var layouts = jars.EnumerateArray()
                    .Where(j => j.ValueKind == JsonValueKind.Object && j.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String)
                    .Select(j => new JarLayout(
                        j.GetProperty("key").GetString()!,
                        j.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString() : null,
                        j.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array
                            ? cats.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList()
                            : []))
                    .ToList();
                return new JarsBlock(block.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : null, layouts);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Bố cục hỏng: không có hũ để tính, trang chủ vẫn vẽ các khối còn lại.
        }

        return null;
    }
}
