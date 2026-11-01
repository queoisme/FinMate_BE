using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Admin.Commands;

/// <param name="Version">Phiên bản model muốn kích hoạt (tên trong sổ <c>nlu_model_versions</c>, vd. <c>finmate-nlu-v16-s2</c>).</param>
/// <param name="Body"><c>{"force": false, "expected_active": "finmate-nlu-v11", "reason": "..."}</c>; mọi trường tùy chọn.</param>
public sealed record AdminRasaModelActivation(Guid AdminId, string Version, JsonElement? Body, string? IpAddress);

public interface IAdminRasaService
{
    /// <summary>Trạng thái Rasa (phiên bản, model đang chạy, số lượt huấn luyện) từ AI Service. Rasa tắt thì AI Service trả <c>available: false</c> (200).</summary>
    Task<JsonElement?> StatusAsync(CancellationToken ct = default);

    /// <summary>Ý định, xếp hạng và thực thể Rasa đọc được từ một câu. Chỉ đọc: không ghi gì, không để lại nhật ký.</summary>
    Task<JsonElement?> ParseAsync(JsonElement body, CancellationToken ct = default);

    /// <summary>Mọi phiên bản model đã huấn luyện và đăng ký (kèm số đo, bản đang chạy, mỗi bản có qua cổng đo không). Chỉ đọc.</summary>
    Task<JsonElement?> ModelsAsync(CancellationToken ct = default);

    /// <summary>Kích hoạt một phiên bản: chuyển tiếp rồi, CHỈ khi AI Service chấp nhận (202), ghi một dòng audit_logs (ai, bản nào, có ép qua cổng đo không). Kết quả nạp (vài phút) ở <c>activation</c> của <c>ModelsAsync</c>.</summary>
    Task<JsonElement?> ActivateModelAsync(AdminRasaModelActivation command, CancellationToken ct = default);
}

public class AdminRasaService : IAdminRasaService
{
    private readonly IAiRulesAdminClient _client;
    private readonly IAuditLogService _auditLogService;

    public AdminRasaService(IAiRulesAdminClient client, IAuditLogService auditLogService)
    {
        _client = client;
        _auditLogService = auditLogService;
    }

    public Task<JsonElement?> StatusAsync(CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(HttpMethod.Get, "status", Area: AiRulesRequest.RasaArea), ct);

    public Task<JsonElement?> ParseAsync(JsonElement body, CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(HttpMethod.Post, "parse", Body: body, Area: AiRulesRequest.RasaArea), ct);

    public Task<JsonElement?> ModelsAsync(CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(HttpMethod.Get, "models", Area: AiRulesRequest.RasaArea), ct);

    public async Task<JsonElement?> ActivateModelAsync(AdminRasaModelActivation command, CancellationToken ct = default)
    {
        var result = await _client.SendAsync(
            new AiRulesRequest(HttpMethod.Post, $"models/{Uri.EscapeDataString(command.Version)}/activate", null, command.Body, command.AdminId, AiRulesRequest.RasaArea), ct);

        // Số đo, lý do và các điều kiện cổng đo nằm ở sổ nlu_model_versions của AI Service; ở đây chỉ ghi ai YÊU CẦU đổi sang bản nào, từ bản nào, có ép không.
        await _auditLogService.LogAsync(
            AuditEvents.AdminRasaModelActivationRequested,
            command.AdminId,
            command.IpAddress,
            new
            {
                command.Version,
                PreviousVersion = StringOf(result, "previous_version"),
                Forced = BoolOf(result, "forced") ?? false,
                Reason = StringOf(command.Body, "reason"),
            },
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
