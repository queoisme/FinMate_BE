using System.Text.Json;

namespace FinMate.Application.FinanceMethods;

/// <summary>
/// Đọc các cột JSON của khuôn "cách quản lý tài chính" (<c>param_defs</c>, <c>eligibility</c>, <c>signals</c>, <c>show_if</c>...).
/// Mọi hàm đọc KHÔNG BAO GIỜ ném: admin có thể lưu JSON sai hình dạng (CHECK ở DB chỉ kiểm object/array), và một dòng cấu hình hỏng
/// không được làm hỏng cả luồng gợi ý của người dùng — dữ liệu lạ coi như không có.
/// </summary>
internal static class FinanceJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Object {khóa: số nguyên} (ở <paramref name="property"/> nếu có); phần tử không phải số nguyên bị bỏ qua.</summary>
    public static Dictionary<string, int> ReadIntMap(string? json, string? property = null)
    {
        var map = new Dictionary<string, int>();
        using var doc = Parse(json);
        if (doc is null || !TryElement(doc.RootElement, property, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return map;
        }

        foreach (var item in element.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt32(out var value))
            {
                map[item.Name] = value;
            }
        }

        return map;
    }

    /// <summary>Mảng chuỗi (ở <paramref name="property"/> nếu có); phần tử không phải chuỗi bị bỏ qua.</summary>
    public static List<string> ReadStringList(string? json, string? property = null)
    {
        var list = new List<string>();
        using var doc = Parse(json);
        if (doc is null || !TryElement(doc.RootElement, property, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
            {
                list.Add(text);
            }
        }

        return list;
    }

    /// <summary>JSON dùng lại được sau khi tài liệu gốc đã đóng, hoặc null khi rỗng/hỏng.</summary>
    public static JsonElement? ToElement(string? json)
    {
        using var doc = Parse(json);
        return doc?.RootElement.Clone();
    }

    private static bool TryElement(JsonElement root, string? property, out JsonElement element)
    {
        if (property is null)
        {
            element = root;
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out element))
        {
            return true;
        }

        element = default;
        return false;
    }

    private static JsonDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
