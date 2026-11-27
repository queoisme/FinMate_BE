using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.Application.Common.Exceptions;

namespace FinMate.Application.ForecastConfig;

/// <summary>
/// Áp phần thay đổi (kiểu JSON merge patch, RFC 7396) lên hai nhóm đang có (D5): chỉ gửi trường đổi; mảng (<c>holidays</c>) thay nguyên mảng.
/// Nhóm ngoài <c>forecast</c>/<c>limits</c> → 400 <see cref="ForecastConfigErrorCodes.OutOfScope"/>; trường không có trong nhóm (gõ sai tên) → 400
/// <see cref="ForecastConfigErrorCodes.UnknownField"/>, vì System.Text.Json mặc định bỏ qua trường lạ và sai chính tả sẽ âm thầm không đổi gì.
/// <c>null</c> không xoá được trường nào (mọi tham số đều bắt buộc) → 400.
/// </summary>
public static class ForecastConfigPatch
{
    private static readonly string[] AllowedSections = [ForecastConfigSections.ForecastKey, ForecastConfigSections.LimitsKey];

    public static ForecastConfigSections Apply(ForecastConfigSections current, JsonElement changes)
    {
        if (changes.ValueKind != JsonValueKind.Object)
        {
            throw new BadRequestException(ForecastConfigErrorCodes.InvalidValue, "changes phải là một object.");
        }

        var target = current.ToNode();
        foreach (var section in changes.EnumerateObject())
        {
            if (!AllowedSections.Contains(section.Name, StringComparer.Ordinal))
            {
                throw new BadRequestException(ForecastConfigErrorCodes.OutOfScope, $"Chỉ được đổi nhóm forecast và limits; '{section.Name}' nằm ngoài phạm vi.");
            }

            if (section.Value.ValueKind != JsonValueKind.Object)
            {
                throw new BadRequestException(ForecastConfigErrorCodes.InvalidValue, $"{section.Name} phải là một object.");
            }

            Merge(target[section.Name]!.AsObject(), section.Value, section.Name);
        }

        return ForecastConfigSections.FromNode(target);
    }

    private static void Merge(JsonObject target, JsonElement patch, string path)
    {
        foreach (var field in patch.EnumerateObject())
        {
            var fieldPath = $"{path}.{field.Name}";
            if (!target.ContainsKey(field.Name))
            {
                throw new BadRequestException(ForecastConfigErrorCodes.UnknownField, $"Không có tham số '{fieldPath}'.");
            }

            if (field.Value.ValueKind == JsonValueKind.Null)
            {
                throw new BadRequestException(ForecastConfigErrorCodes.InvalidValue, $"'{fieldPath}' không được để trống.");
            }

            if (field.Value.ValueKind == JsonValueKind.Object && target[field.Name] is JsonObject child)
            {
                Merge(child, field.Value, fieldPath);
                continue;
            }

            target[field.Name] = JsonNode.Parse(field.Value.GetRawText());
        }
    }
}
