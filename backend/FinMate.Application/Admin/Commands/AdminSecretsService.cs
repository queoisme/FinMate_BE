using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Admin.Commands;

public interface IAdminSecretsService
{
    /// <summary>Trạng thái từng key (nguồn, 4 ký tự cuối, lúc đổi) và đã có khóa chủ để lưu chưa. Không có giá trị key.</summary>
    Task<JsonElement?> ListAsync(CancellationToken ct = default);

    /// <summary>Thử key đang dùng bằng một lệnh gọi chỉ-đọc tới nhà cung cấp. Không ghi gì, không để lại nhật ký.</summary>
    Task<JsonElement?> TestAsync(string name, CancellationToken ct = default);

    /// <summary>Đặt hoặc ghi đè key. <paramref name="body"/> chứa giá trị key: chỉ được chuyển tiếp, không log, không đưa vào nhật ký admin.</summary>
    Task<JsonElement?> SetAsync(Guid adminId, string? ip, string name, JsonElement body, CancellationToken ct = default);

    /// <summary>Xóa key trong DB: AI Service quay về key ở biến môi trường nếu có.</summary>
    Task<JsonElement?> DeleteAsync(Guid adminId, string? ip, string name, string? reason, CancellationToken ct = default);
}

public class AdminSecretsService : IAdminSecretsService
{
    private readonly IAiRulesAdminClient _client;
    private readonly IAuditLogService _auditLogService;

    public AdminSecretsService(IAiRulesAdminClient client, IAuditLogService auditLogService)
    {
        _client = client;
        _auditLogService = auditLogService;
    }

    public Task<JsonElement?> ListAsync(CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(HttpMethod.Get, string.Empty, Area: AiRulesRequest.SecretsArea), ct);

    public Task<JsonElement?> TestAsync(string name, CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(HttpMethod.Post, $"{Uri.EscapeDataString(name)}/test", Area: AiRulesRequest.SecretsArea), ct);

    public async Task<JsonElement?> SetAsync(Guid adminId, string? ip, string name, JsonElement body, CancellationToken ct = default)
    {
        var result = await _client.SendAsync(
            new AiRulesRequest(HttpMethod.Put, Uri.EscapeDataString(name), null, body, adminId, AiRulesRequest.SecretsArea), ct);

        // Chỉ tên key, hành động và lý do: tuyệt đối không đưa thân yêu cầu (có giá trị key) vào nhật ký.
        await LogAsync(adminId, ip, name, "set", ReasonOf(body), ct);
        return result;
    }

    public async Task<JsonElement?> DeleteAsync(Guid adminId, string? ip, string name, string? reason, CancellationToken ct = default)
    {
        var query = reason is null ? null : new Dictionary<string, string?> { ["reason"] = reason };
        var result = await _client.SendAsync(
            new AiRulesRequest(HttpMethod.Delete, Uri.EscapeDataString(name), query, null, adminId, AiRulesRequest.SecretsArea), ct);

        await LogAsync(adminId, ip, name, "delete", reason, ct);
        return result;
    }

    private Task LogAsync(Guid adminId, string? ip, string name, string action, string? reason, CancellationToken ct)
        => _auditLogService.LogAsync(AuditEvents.AdminSecretChanged, adminId, ip, new { Name = name, Action = action, Reason = reason }, ct);

    private static string? ReasonOf(JsonElement body)
        => body is { ValueKind: JsonValueKind.Object } && body.TryGetProperty("reason", out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;
}
