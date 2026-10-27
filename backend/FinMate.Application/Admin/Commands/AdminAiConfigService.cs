using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Admin.Commands;

/// <param name="Document">Tên tài liệu cấu hình (intent_cues, chat_dictionary, merchant_lexicon, parsing_vocab, thresholds, app_screens).</param>
/// <param name="Action"><c>draft</c> (lưu bản nháp) hoặc <c>activate</c> (kích hoạt một phiên bản, kể cả để hoàn tác), để ghi nhật ký.</param>
/// <param name="Version">Phiên bản bị tác động nếu đã biết trước (kích hoạt); với bản nháp lấy từ phản hồi của AI Service.</param>
public sealed record AdminAiConfigWrite(
    Guid AdminId,
    string Path,
    string Document,
    string Action,
    int? Version,
    JsonElement? Body,
    string? IpAddress);

public interface IAdminAiConfigService
{
    /// <summary>Đọc (danh sách tài liệu, bản đang chạy, lịch sử, xem trước): chuyển tiếp, không ghi nhật ký.</summary>
    Task<JsonElement?> ReadAsync(
        HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query, JsonElement? body, CancellationToken ct = default);

    /// <summary>Lưu bản nháp hoặc kích hoạt: chuyển tiếp rồi, CHỈ khi AI Service chấp nhận, ghi một dòng audit_logs.</summary>
    Task<JsonElement?> WriteAsync(AdminAiConfigWrite command, CancellationToken ct = default);
}

public class AdminAiConfigService : IAdminAiConfigService
{
    private readonly IAiRulesAdminClient _client;
    private readonly IAuditLogService _auditLogService;

    public AdminAiConfigService(IAiRulesAdminClient client, IAuditLogService auditLogService)
    {
        _client = client;
        _auditLogService = auditLogService;
    }

    public Task<JsonElement?> ReadAsync(
        HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query, JsonElement? body, CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(method, path, query, body, Area: AiRulesRequest.ConfigArea), ct);

    public async Task<JsonElement?> WriteAsync(AdminAiConfigWrite command, CancellationToken ct = default)
    {
        var result = await _client.SendAsync(
            new AiRulesRequest(HttpMethod.Post, command.Path, null, command.Body, command.AdminId, AiRulesRequest.ConfigArea), ct);

        // Nội dung từng phiên bản và lý do nằm ở config_documents của AI Service; ở đây chỉ ghi ai đổi tài liệu nào, phiên bản nào, để lần theo từ nhật ký admin chung.
        await _auditLogService.LogAsync(
            AuditEvents.AdminAiConfigChanged,
            command.AdminId,
            command.IpAddress,
            new
            {
                command.Document,
                command.Action,
                Version = command.Version ?? IntOf(result, "version"),
                Note = StringOf(command.Body, "note"),
                Force = command.Action == "activate" ? BoolOf(command.Body, "force") ?? false : (bool?)null,  // kích hoạt luôn ghi rõ có ép bỏ qua cổng đo hay không
            },
            ct);

        return result;
    }

    private static int? IntOf(JsonElement? json, string name)
        => json is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.Number && found.TryGetInt32(out var number)
            ? number
            : null;

    private static string? StringOf(JsonElement? json, string name)
        => json is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;

    private static bool? BoolOf(JsonElement? json, string name)
        => json is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var found) && found.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? found.GetBoolean()
            : null;
}
