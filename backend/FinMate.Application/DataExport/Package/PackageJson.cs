using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.Application.Common;

namespace FinMate.Application.DataExport.Package;

/// <summary>Cách ghi JSON trong gói (D13): camelCase như API, enum là chữ, thời điểm theo giờ VN (+07:00), tiếng Việt không bị mã hoá \u.</summary>
public static class PackageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(), new VietnamTimeConverter() },
    };

    private sealed class VietnamTimeConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => writer.WriteStringValue(VietnamTime.ToLocal(value).ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", System.Globalization.CultureInfo.InvariantCulture));
    }
}
