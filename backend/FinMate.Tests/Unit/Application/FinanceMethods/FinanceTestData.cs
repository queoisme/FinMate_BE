using System.Text.Json;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Tests.Unit.Application.FinanceMethods;

/// <summary>Dựng dữ liệu tối thiểu cho test đơn vị của cách quản lý tài chính, không cần DB.</summary>
internal static class FinanceTestData
{
    public static string Json(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static string Eligibility(Dictionary<string, int>? weights = null, params string[] excludes)
        => Json(new { weights = weights ?? new Dictionary<string, int>(), excludes });

    public static string AlertParams(string extraKey = "other")
        => Json(new object[]
        {
            new { key = "t1_pct", label = "T1", type = "percent", @default = 70, min = 1, max = 99, userEditable = true },
            new { key = "t2_pct", label = "T2", type = "percent", @default = 90, min = 2, max = 99, userEditable = true },
            new { key = extraKey, label = "X", type = "int", @default = 5, min = 1, max = 9, userEditable = false },
        });

    public static FinanceMethod Method(
        string code,
        FinanceMethodRole role = FinanceMethodRole.Primary,
        Dictionary<string, int>? weights = null,
        string[]? excludes = null,
        int sortOrder = 0,
        bool isActive = true,
        GoalMode goalMode = GoalMode.TrackOnly,
        string? paramDefs = null,
        int version = 1) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = $"Method {code}",
        Summary = $"Summary {code}",
        Role = role,
        GoalMode = goalMode,
        Eligibility = Eligibility(weights, excludes ?? []),
        ParamDefs = paramDefs ?? "[]",
        WhyText = $"Why {code}",
        StarterSteps = Json(new[] { "a", "b", "c" }),
        SortOrder = sortOrder,
        IsActive = isActive,
        Version = version,
    };

    public static Dictionary<string, int> Signals(params (string Key, int Weight)[] signals)
        => signals.ToDictionary(s => s.Key, s => s.Weight);
}
