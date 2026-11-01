using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Admin.Commands;

/// <param name="Body"><c>{"prompt_version", "instruction_text", "activate"}</c>.</param>
public sealed record AdminMethodPromptCreate(Guid AdminId, JsonElement Body, string? IpAddress);

/// <param name="Version">Phiên bản muốn dùng (kể cả quay về bản cũ).</param>
/// <param name="Body"><c>{"expected_active": "method-select-v1"}</c>; tùy chọn.</param>
public sealed record AdminMethodPromptActivate(Guid AdminId, string Version, JsonElement? Body, string? IpAddress);

public interface IAdminMethodPromptService
{
    /// <summary>Mọi phiên bản chỉ dẫn (mới nhất trước) và bản đang dùng. Chỉ đọc.</summary>
    Task<JsonElement?> ListAsync(CancellationToken ct = default);

    /// <summary>Thêm một phiên bản (không sửa tại chỗ). CHỈ khi AI Service chấp nhận (201) mới ghi audit_logs (không kèm nội dung chỉ dẫn).</summary>
    Task<JsonElement?> CreateAsync(AdminMethodPromptCreate command, CancellationToken ct = default);

    /// <summary>Chuyển bản đang dùng. CHỈ khi AI Service chấp nhận mới ghi audit_logs (từ bản nào sang bản nào).</summary>
    Task<JsonElement?> ActivateAsync(AdminMethodPromptActivate command, CancellationToken ct = default);
}

public class AdminMethodPromptService : IAdminMethodPromptService
{
    private readonly IAiRulesAdminClient _client;
    private readonly IAuditLogService _auditLogService;

    public AdminMethodPromptService(IAiRulesAdminClient client, IAuditLogService auditLogService)
    {
        _client = client;
        _auditLogService = auditLogService;
    }

    public Task<JsonElement?> ListAsync(CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(HttpMethod.Get, string.Empty, Area: AiRulesRequest.MethodPromptsArea), ct);

    public async Task<JsonElement?> CreateAsync(AdminMethodPromptCreate command, CancellationToken ct = default)
    {
        var result = await _client.SendAsync(
            new AiRulesRequest(HttpMethod.Post, string.Empty, null, command.Body, command.AdminId, AiRulesRequest.MethodPromptsArea), ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminMethodPromptCreated,
            command.AdminId,
            command.IpAddress,
            new { Version = StringOf(result, "prompt_version"), Activated = BoolOf(result, "is_active") ?? false },
            ct);

        return result;
    }

    public async Task<JsonElement?> ActivateAsync(AdminMethodPromptActivate command, CancellationToken ct = default)
    {
        var result = await _client.SendAsync(
            new AiRulesRequest(HttpMethod.Post, $"{Uri.EscapeDataString(command.Version)}/activate", null, command.Body, command.AdminId, AiRulesRequest.MethodPromptsArea), ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminMethodPromptActivated,
            command.AdminId,
            command.IpAddress,
            new { command.Version, PreviousVersion = StringOf(result, "previous_version") },
            ct);

        return result;
    }

    private static string? StringOf(JsonElement? json, string name)
        => json is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;

    private static bool? BoolOf(JsonElement? json, string name)
        => json is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var found) && found.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? found.GetBoolean()
            : null;
}
