using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Admin.Commands;

/// <param name="Action">Tên việc đã làm (<c>create</c>, <c>clone</c>, <c>activate</c>, <c>archive</c>, <c>pattern.add</c>...), để ghi nhật ký.</param>
/// <param name="Path">Đường dẫn dưới <c>/admin/provider-formats/</c>, đã escape từng đoạn; rỗng là gốc.</param>
public sealed record AdminProviderFormatWrite(
    Guid AdminId,
    HttpMethod Method,
    string Path,
    string Action,
    JsonElement? Body,
    string? IpAddress);

public interface IAdminProviderFormatsService
{
    /// <summary>Đọc (danh sách, chi tiết, kiểm tra) và thử một thông báo: chuyển tiếp, không ghi nhật ký.</summary>
    Task<JsonElement?> ReadAsync(
        HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query, JsonElement? body, CancellationToken ct = default);

    /// <summary>Ghi: chuyển tiếp rồi, CHỈ khi AI Service chấp nhận, ghi một dòng audit_logs (không kèm thân yêu cầu).</summary>
    Task<JsonElement?> WriteAsync(AdminProviderFormatWrite command, CancellationToken ct = default);
}

public class AdminProviderFormatsService : IAdminProviderFormatsService
{
    private readonly IAiRulesAdminClient _client;
    private readonly IAuditLogService _auditLogService;

    public AdminProviderFormatsService(IAiRulesAdminClient client, IAuditLogService auditLogService)
    {
        _client = client;
        _auditLogService = auditLogService;
    }

    public Task<JsonElement?> ReadAsync(
        HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query, JsonElement? body, CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(method, path, query, body, Area: AiRulesRequest.FormatsArea), ct);

    public async Task<JsonElement?> WriteAsync(AdminProviderFormatWrite command, CancellationToken ct = default)
    {
        var result = await _client.SendAsync(
            new AiRulesRequest(command.Method, command.Path, null, command.Body, command.AdminId, AiRulesRequest.FormatsArea), ct);

        // Thân yêu cầu có regex và mẫu thông báo: không đưa vào nhật ký admin chung. Chi tiết trước/sau và lý do nằm ở kb_audit_log của AI Service.
        await _auditLogService.LogAsync(
            AuditEvents.AdminProviderFormatChanged,
            command.AdminId,
            command.IpAddress,
            new { command.Action, command.Path, Reason = StringOf(command.Body, "reason") },
            ct);

        return result;
    }

    private static string? StringOf(JsonElement? json, string name)
        => json is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;
}
