using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Admin.Commands;

/// <param name="Resource">Nhóm luật bị đổi (brands, keywords, item-cache, ai-instructions, history), để ghi nhật ký.</param>
/// <param name="Key">Khóa của dòng bị đổi (tên thương hiệu, id từ khóa, tên món...), nếu có.</param>
public sealed record AdminAiRuleWrite(
    Guid AdminId,
    HttpMethod Method,
    string Path,
    string Resource,
    string? Key,
    IReadOnlyDictionary<string, string?>? Query,
    JsonElement? Body,
    string? IpAddress);

public interface IAdminAiRulesService
{
    /// <summary>Đọc (hoặc thử luật trước khi lưu): chuyển tiếp, không ghi nhật ký.</summary>
    Task<JsonElement?> ReadAsync(
        HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query, JsonElement? body, CancellationToken ct = default);

    /// <summary>Thay đổi luật: chuyển tiếp rồi, CHỈ khi AI Service chấp nhận, ghi một dòng audit_logs.</summary>
    Task<JsonElement?> WriteAsync(AdminAiRuleWrite command, CancellationToken ct = default);
}

public class AdminAiRulesService : IAdminAiRulesService
{
    private readonly IAiRulesAdminClient _client;
    private readonly IAuditLogService _auditLogService;

    public AdminAiRulesService(IAiRulesAdminClient client, IAuditLogService auditLogService)
    {
        _client = client;
        _auditLogService = auditLogService;
    }

    public Task<JsonElement?> ReadAsync(
        HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query, JsonElement? body, CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(method, path, query, body), ct);

    public async Task<JsonElement?> WriteAsync(AdminAiRuleWrite command, CancellationToken ct = default)
    {
        var result = await _client.SendAsync(
            new AiRulesRequest(command.Method, command.Path, command.Query, command.Body, command.AdminId), ct);

        // Nội dung luật trước/sau do AI Service giữ (bill_rule_audit); ở đây chỉ ghi ai đổi cái gì, để lần theo từ nhật ký admin chung.
        await _auditLogService.LogAsync(
            AuditEvents.AdminAiRuleChanged,
            command.AdminId,
            command.IpAddress,
            new { command.Resource, command.Key, Method = command.Method.Method, Reason = ReasonOf(command.Body) },
            ct);

        return result;
    }

    private static string? ReasonOf(JsonElement? body)
        => body is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
            ? reason.GetString()
            : null;
}
