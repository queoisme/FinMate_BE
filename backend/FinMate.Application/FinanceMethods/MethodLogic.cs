using System.Text.Json;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.FinanceMethods;

/// <summary>Một tham số trong <c>finance_methods.param_defs</c>. Giá trị luôn là số nguyên (phần trăm, đồng, phút).</summary>
public sealed record ParamDef(string Key, string Label, string Type, long Default, long Min, long Max, bool UserEditable);

public static class MethodParams
{
    /// <summary>Đọc <c>param_defs</c>; mục thiếu trường hoặc sai kiểu bị bỏ qua chứ không làm hỏng cả danh sách.</summary>
    public static IReadOnlyList<ParamDef> ParseDefs(string? json)
    {
        var defs = new List<ParamDef>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return defs;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return defs;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (TryReadDef(item, out var def))
                {
                    defs.Add(def);
                }
            }
        }
        catch (JsonException)
        {
            // JSON hỏng: coi như phương pháp không có tham số nào.
        }

        return defs;
    }

    /// <summary>Phần người dùng ghi đè (<c>user_methods.params</c>): object {khóa: số nguyên}.</summary>
    public static Dictionary<string, long> ParseOverrides(string? json)
    {
        var overrides = new Dictionary<string, long>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return overrides;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return overrides;
            }

            foreach (var item in doc.RootElement.EnumerateObject())
            {
                if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt64(out var value))
                {
                    overrides[item.Name] = value;
                }
            }
        }
        catch (JsonException)
        {
            // Giữ những gì đã đọc được.
        }

        return overrides;
    }

    public static bool IsAllowed(ParamDef def, long value) => def.UserEditable && value >= def.Min && value <= def.Max;

    /// <summary>
    /// Giá trị đang có hiệu lực của từng tham số: ghi đè của người dùng nếu hợp lệ (tham số cho phép sửa và nằm trong [min, max]),
    /// không thì mặc định HIỆN HÀNH của phương pháp — admin đổi mặc định thì người chưa tự chỉnh đổi theo.
    /// </summary>
    public static Dictionary<string, long> Effective(IReadOnlyList<ParamDef> defs, IReadOnlyDictionary<string, long> overrides)
        => defs.ToDictionary(
            d => d.Key,
            d => overrides.TryGetValue(d.Key, out var value) && IsAllowed(d, value) ? value : d.Default);

    private static bool TryReadDef(JsonElement item, out ParamDef def)
    {
        def = null!;
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String
            || !TryLong(item, "default", out var value) || !TryLong(item, "min", out var min) || !TryLong(item, "max", out var max))
        {
            return false;
        }

        def = new ParamDef(
            key.GetString()!,
            item.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString()! : string.Empty,
            item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString()! : string.Empty,
            value,
            min,
            max,
            item.TryGetProperty("userEditable", out var editable) && editable.ValueKind == JsonValueKind.True);
        return true;
    }

    private static bool TryLong(JsonElement item, string name, out long value)
    {
        value = 0;
        return item.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }
}

/// <summary>Điều kiện chọn của một phương pháp: <c>weights</c> CỘNG ĐIỂM, <c>excludes</c> LOẠI HẲN khỏi shortlist.</summary>
public sealed record MethodEligibility(IReadOnlyDictionary<string, int> Weights, IReadOnlySet<string> Excludes)
{
    public static MethodEligibility Parse(string? json)
        => new(FinanceJson.ReadIntMap(json, "weights"), FinanceJson.ReadStringList(json, "excludes").ToHashSet());
}

/// <summary>Kết quả chọn: một cách chính, tối đa một cách bổ trợ, mỗi cách kèm lý do hiển thị cho người dùng.</summary>
public sealed record MethodSelection(FinanceMethod Primary, string PrimaryReason, FinanceMethod? Support, string? SupportReason);

/// <summary>
/// Bộ chọn phương pháp bằng RULE — vừa là đường chính khi người dùng không đồng ý gửi cho AI, vừa là đường lui khi AI lỗi hoặc trả sai.
/// Thuần: không đụng DB hay mạng, nên test được bằng dữ liệu gieo thật.
/// </summary>
public static class MethodSelector
{
    /// <summary>Cách bổ trợ chỉ được chọn khi điểm của nó ít nhất bằng chừng này điểm của cách chính — để "bổ trợ" không phải một cái gợi ý vớ vẩn cho đủ cặp.</summary>
    private const double SupportMinShareOfPrimary = 0.5;

    public const int MaxReasonLength = 240;

    public static bool CanBePrimary(FinanceMethodRole role) => role is FinanceMethodRole.Primary or FinanceMethodRole.Both;

    public static bool CanBeSupport(FinanceMethodRole role) => role is FinanceMethodRole.Supporting or FinanceMethodRole.Both;

    /// <summary>Phương pháp đang bật và KHÔNG có signal nào của người dùng nằm trong <c>excludes</c>, theo thứ tự hiển thị.</summary>
    public static IReadOnlyList<FinanceMethod> Shortlist(IEnumerable<FinanceMethod> methods, IReadOnlyDictionary<string, int> signals)
        => methods
            .Where(m => m.IsActive && !MethodEligibility.Parse(m.Eligibility).Excludes.Any(signals.ContainsKey))
            .OrderBy(m => m.SortOrder)
            .ToList();

    /// <summary>Σ (trọng số của phương pháp × trọng số signal của người dùng) trên các signal hai bên cùng có.</summary>
    public static int Score(FinanceMethod method, IReadOnlyDictionary<string, int> signals)
    {
        var weights = MethodEligibility.Parse(method.Eligibility).Weights;
        return weights.Where(w => signals.ContainsKey(w.Key)).Sum(w => w.Value * signals[w.Key]);
    }

    /// <summary>null khi shortlist không có cách nào làm được vai trò chính.</summary>
    public static MethodSelection? SelectByRule(IReadOnlyList<FinanceMethod> shortlist, IReadOnlyDictionary<string, int> signals)
    {
        var scored = shortlist.Select(m => (Method: m, Score: Score(m, signals))).ToList();

        // Hoà điểm thì theo thứ tự hiển thị (SortOrder), nên khi không signal nào khớp cách theo dõi tối giản (đứng đầu) thắng.
        var primary = scored
            .Where(x => CanBePrimary(x.Method.Role))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Method.SortOrder)
            .Cast<(FinanceMethod Method, int Score)?>()
            .FirstOrDefault();
        if (primary is null)
        {
            return null;
        }

        var support = scored
            .Where(x => CanBeSupport(x.Method.Role)
                && x.Method.Id != primary.Value.Method.Id
                && x.Score > 0
                && x.Score >= primary.Value.Score * SupportMinShareOfPrimary)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Method.SortOrder)
            .Cast<(FinanceMethod Method, int Score)?>()
            .FirstOrDefault();

        return new MethodSelection(
            primary.Value.Method,
            primary.Value.Method.WhyText,
            support?.Method,
            support?.Method.WhyText);
    }

    /// <summary>
    /// Kiểm lại phản hồi của AI Ở PHÍA BACKEND (ai-service đã kiểm một lần; đây là lớp thứ hai vì backend là bên ghi vào DB và hiển thị cho người dùng).
    /// null khi có bất kỳ điều gì sai: mã ngoài shortlist, bổ trợ trùng chính, sai vai trò, lý do rỗng/quá dài/xuống dòng. Không sửa, không cắt.
    /// </summary>
    public static MethodSelection? FromAiPick(
        string primaryCode,
        string primaryReason,
        string? supportCode,
        string? supportReason,
        IReadOnlyList<FinanceMethod> shortlist)
    {
        var primary = shortlist.FirstOrDefault(m => m.Code == primaryCode);
        if (primary is null || !CanBePrimary(primary.Role) || !IsValidReason(primaryReason))
        {
            return null;
        }

        if (supportCode is null)
        {
            return new MethodSelection(primary, primaryReason.Trim(), null, null);
        }

        var support = shortlist.FirstOrDefault(m => m.Code == supportCode);
        if (support is null || support.Id == primary.Id || !CanBeSupport(support.Role) || supportReason is null || !IsValidReason(supportReason))
        {
            return null;
        }

        return new MethodSelection(primary, primaryReason.Trim(), support, supportReason.Trim());
    }

    private static bool IsValidReason(string reason)
    {
        var trimmed = reason.Trim();
        return trimmed.Length is > 0 and <= MaxReasonLength && !trimmed.Contains('\n') && !trimmed.Contains('\r');
    }
}
