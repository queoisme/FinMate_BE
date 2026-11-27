using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinMate.Application.Kpi;

/// <summary>
/// Số đếm của một nhóm con (nguồn giao dịch, gói thuê bao) trong dashboard KPI. Nhỏ hơn ngưỡng ô nhỏ thì bị che (BR-KPI-07):
/// JSON là chuỗi <c>"&lt;5"</c> thay vì số, để một gói chỉ có một hai người không chỉ ra được ai.
/// </summary>
[JsonConverter(typeof(KpiCountJsonConverter))]
public readonly record struct KpiCount(long Value, int? MaskedBelow)
{
    public bool Masked => MaskedBelow is not null;

    public static KpiCount Exact(long value) => new(value, null);

    /// <summary>1 đến <paramref name="minCellSize"/> − 1 thì che; 0 vẫn là 0 vì không chỉ ra ai.</summary>
    public static KpiCount Mask(long value, int minCellSize)
        => value > 0 && value < minCellSize ? new(0, minCellSize) : Exact(value);

    public override string ToString() => Masked ? $"<{MaskedBelow}" : Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class KpiCountJsonConverter : JsonConverter<KpiCount>
{
    public override KpiCount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return KpiCount.Exact(reader.GetInt64());
        }

        var text = reader.GetString();
        if (text is { Length: > 1 } && text[0] == '<' && int.TryParse(text.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture, out var below))
        {
            return new KpiCount(0, below);
        }

        throw new JsonException("Số đếm KPI không hợp lệ.");
    }

    public override void Write(Utf8JsonWriter writer, KpiCount value, JsonSerializerOptions options)
    {
        if (value.Masked)
        {
            writer.WriteStringValue(value.ToString());
        }
        else
        {
            writer.WriteNumberValue(value.Value);
        }
    }
}
