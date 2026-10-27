using System.Text.Json;
using FinMate.Domain.Entities;

namespace FinMate.Application.Budgets;

/// <summary>
/// Hai ngưỡng cảnh báo ngân sách của MỘT người dùng: T1 (nhắc nhẹ) và T2 (cảnh báo mạnh), tính theo % hạn mức đã dùng.
/// Mức thứ ba — "vượt hạn mức" — cố định ở 100% và không chỉnh được, nên không nằm ở đây.
///
/// Giá trị đến từ tham số của phương pháp người dùng đang dùng (<c>finance_methods.param_defs</c>, người dùng ghi đè trong
/// <c>user_methods.params</c>); không có phương pháp nào định nghĩa chúng thì dùng <see cref="Default"/> (70, 90 — hành vi trước khi có
/// phương pháp). Ràng buộc: 1 ≤ T1 &lt; T2 ≤ 99.
/// </summary>
public sealed record AlertThresholds(int T1Pct, int T2Pct)
{
    /// <summary>Mốc T1 thấp nhất có thể (cận dưới của tham số) — truy vấn của job lưới vét dùng nó làm điều kiện lọc thô.</summary>
    public const int MinT1Pct = 1;

    public static AlertThresholds Default { get; } = new(70, 90);

    public static bool IsValid(AlertThresholds thresholds)
        => thresholds.T1Pct >= MinT1Pct && thresholds.T1Pct < thresholds.T2Pct && thresholds.T2Pct <= 99;

    /// <summary>
    /// Tính ngưỡng hiệu lực của một phương pháp: giá trị người dùng ghi đè nếu hợp lệ (đúng khoảng [min, max] và tham số cho phép sửa),
    /// không thì mặc định của phương pháp. Cặp ghi đè vi phạm 1 ≤ T1 &lt; T2 ≤ 99 bị bỏ HẾT và quay về mặc định; mặc định cũng sai thì
    /// trả null để người gọi thử phương pháp khác hoặc dùng <see cref="Default"/>.
    ///
    /// KHÔNG bao giờ ném: đường cảnh báo chạy sau khi giao dịch đã lưu, nên JSON hỏng không được làm hỏng giao dịch. Trả null cho mọi dữ liệu lạ.
    /// </summary>
    public static AlertThresholds? FromMethod(string paramDefsJson, string overridesJson)
    {
        try
        {
            using var defsDoc = JsonDocument.Parse(paramDefsJson);
            using var overridesDoc = JsonDocument.Parse(overridesJson);

            var t1 = FindDefinition(defsDoc.RootElement, FinanceParamKeys.T1Pct);
            var t2 = FindDefinition(defsDoc.RootElement, FinanceParamKeys.T2Pct);
            if (t1 is null || t2 is null)
            {
                return null;
            }

            var withOverrides = new AlertThresholds(t1.Effective(overridesDoc.RootElement), t2.Effective(overridesDoc.RootElement));
            if (IsValid(withOverrides))
            {
                return withOverrides;
            }

            var defaults = new AlertThresholds(t1.Default, t2.Default);
            return IsValid(defaults) ? defaults : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static ParamDefinition? FindDefinition(JsonElement defs, string key)
    {
        if (defs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var def in defs.EnumerateArray())
        {
            if (def.GetProperty("key").GetString() == key)
            {
                return new ParamDefinition(
                    key,
                    checked((int)def.GetProperty("default").GetInt64()),
                    def.GetProperty("min").GetInt64(),
                    def.GetProperty("max").GetInt64(),
                    def.GetProperty("userEditable").GetBoolean());
            }
        }

        return null;
    }

    private sealed record ParamDefinition(string Key, int Default, long Min, long Max, bool UserEditable)
    {
        public int Effective(JsonElement overrides)
        {
            if (UserEditable
                && overrides.ValueKind == JsonValueKind.Object
                && overrides.TryGetProperty(Key, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var number)
                && number >= Min
                && number <= Max)
            {
                return (int)number;
            }

            return Default;
        }
    }
}
