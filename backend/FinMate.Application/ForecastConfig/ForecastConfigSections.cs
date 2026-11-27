using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.ForecastConfig;

public static class ForecastConfigErrorCodes
{
    public const string OutOfScope = "FORECAST_CONFIG_OUT_OF_SCOPE";
    public const string UnknownField = "FORECAST_CONFIG_UNKNOWN_FIELD";
    public const string InvalidValue = "FORECAST_CONFIG_INVALID_VALUE";
    public const string ReasonRequired = "FORECAST_CONFIG_REASON_REQUIRED";
    public const string NoteRequired = "FORECAST_CONFIG_NOTE_REQUIRED";
    public const string Stale = "FORECAST_CONFIG_DRAFT_STALE";
    public const string PendingExists = "FORECAST_CONFIG_PENDING_EXISTS";
    public const string WrongStatus = "FORECAST_CONFIG_WRONG_STATUS";
    public const string OwnDraft = "FORECAST_CONFIG_OWN_DRAFT";
    public const string NotAuthor = "FORECAST_CONFIG_NOT_AUTHOR";
    public const string DraftChanged = "FORECAST_CONFIG_DRAFT_CHANGED";
    public const string InvalidSource = "FORECAST_CONFIG_INVALID_SOURCE";
}

/// <summary>
/// Hai nhóm của <see cref="SystemConfigDocument"/> mà Staff được soạn và OpsManager duyệt (Phase 95, A): <c>forecast</c> và <c>limits</c> (ngưỡng T1/T2 mặc định).
/// Bản nháp chỉ lưu đúng hai nhóm này, nên không thể mang thay đổi ở nhóm khác lên runtime.
/// </summary>
public sealed record ForecastConfigSections(ForecastSettings Forecast, LimitSettings Limits)
{
    public const string Scope = "forecast";
    public const string ForecastKey = "forecast";
    public const string LimitsKey = "limits";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static ForecastConfigSections Of(SystemConfigDocument document) => new(document.Forecast, document.Limits);

    /// <summary>Tài liệu đầy đủ: <paramref name="document"/> với hai nhóm được thay.</summary>
    public SystemConfigDocument ApplyTo(SystemConfigDocument document) => document with { Forecast = Forecast, Limits = Limits };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public JsonObject ToNode() => JsonSerializer.SerializeToNode(this, Json)!.AsObject();

    public static ForecastConfigSections Deserialize(string json)
        => JsonSerializer.Deserialize<ForecastConfigSections>(json, Json) ?? throw new JsonException("Bản nháp rỗng.");

    public static ForecastConfigSections FromNode(JsonObject node)
    {
        try
        {
            return node.Deserialize<ForecastConfigSections>(Json) ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new Common.Exceptions.BadRequestException(ForecastConfigErrorCodes.InvalidValue, "Có giá trị sai kiểu (ví dụ chữ ở trường số).");
        }
    }
}
