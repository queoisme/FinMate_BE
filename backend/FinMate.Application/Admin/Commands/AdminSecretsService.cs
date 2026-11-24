using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
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

    /// <summary>
    /// Chép mọi key đang chỉ nằm ở biến môi trường (của backend và của AI Service) vào kho mã hóa trong DB, để chỉnh được trên trang quản trị. Key đã ở DB giữ nguyên.
    /// Trả <c>imported</c> và <c>failed</c> (chỉ tên key, không có giá trị).
    /// </summary>
    Task<JsonElement?> ImportFromEnvironmentAsync(Guid adminId, string? ip, string? reason, CancellationToken ct = default);

    /// <summary>Xóa key trong DB: AI Service quay về key ở biến môi trường nếu có.</summary>
    Task<JsonElement?> DeleteAsync(Guid adminId, string? ip, string name, string? reason, CancellationToken ct = default);
}

public class AdminSecretsService : IAdminSecretsService
{
    private readonly IAiRulesAdminClient _client;
    private readonly IAuditLogService _auditLogService;
    private readonly ISecretVault _vault;
    private readonly ISecretTester _tester;

    public AdminSecretsService(IAiRulesAdminClient client, IAuditLogService auditLogService, ISecretVault vault, ISecretTester tester)
    {
        _client = client;
        _auditLogService = auditLogService;
        _vault = vault;
        _tester = tester;
    }

    /// <summary>
    /// Gộp hai kho vào một danh sách: key của AI Service (Gemini, OpenRouter, Google Vision) hỏi qua AI Service, key của backend (Brevo, Google OAuth, FCM) đọc ở kho của chính backend. Mỗi mục có
    /// <c>owner</c> (<c>ai</c> hoặc <c>backend</c>) và <c>can_store</c> (kho đó đã có khóa chủ chưa). AI Service ngừng thì vẫn hiện key của backend kèm <c>ai_unavailable</c>: đăng nhập và email
    /// không được mất đường quản trị chỉ vì dịch vụ AI gặp sự cố.
    /// </summary>
    public async Task<JsonElement?> ListAsync(CancellationToken ct = default)
    {
        var items = new List<object>();
        var aiAvailable = false;
        var aiUnavailable = false;
        try
        {
            var ai = await _client.SendAsync(new AiRulesRequest(HttpMethod.Get, string.Empty, Area: AiRulesRequest.SecretsArea), ct);
            if (ai is { ValueKind: JsonValueKind.Object } root)
            {
                aiAvailable = root.TryGetProperty("available", out var flag) && flag.ValueKind == JsonValueKind.True;
                if (root.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    items.AddRange(list.EnumerateArray().Select(item => (object)AiItem(item, aiAvailable)));
                }
            }
        }
        catch (Exception ex) when (ex is FinMateException or HttpRequestException)
        {
            aiUnavailable = true;
        }

        items.AddRange((await _vault.ListAsync(ct)).Select(s => (object)new Dictionary<string, object?>
        {
            ["name"] = s.Name, ["title"] = s.Title, ["source"] = s.Source, ["configured"] = s.Configured, ["last4"] = s.Last4,
            ["updated_at"] = s.UpdatedAt, ["multiline"] = s.Multiline, ["plain"] = s.Plain, ["placeholder"] = s.Placeholder, ["owner"] = "backend", ["can_store"] = _vault.CanStore,
        }));

        return JsonSerializer.SerializeToElement(new { available = aiAvailable || _vault.CanStore, ai_unavailable = aiUnavailable, items });
    }

    private static Dictionary<string, object?> AiItem(JsonElement item, bool canStore)
    {
        var map = item.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        map["owner"] = "ai";
        map["can_store"] = canStore;
        return map;
    }

    private static Dictionary<string, object?> Shape(SecretStatus s, bool canStore) => new()
    {
        ["name"] = s.Name, ["title"] = s.Title, ["source"] = s.Source, ["configured"] = s.Configured, ["last4"] = s.Last4,
        ["updated_at"] = s.UpdatedAt, ["multiline"] = s.Multiline, ["plain"] = s.Plain, ["placeholder"] = s.Placeholder, ["owner"] = "backend", ["can_store"] = canStore,
    };

    public async Task<JsonElement?> TestAsync(string name, CancellationToken ct = default)
    {
        if (BackendSecrets.Find(name) is null)
        {
            return await _client.SendAsync(new AiRulesRequest(HttpMethod.Post, $"{Uri.EscapeDataString(name)}/test", Area: AiRulesRequest.SecretsArea), ct);
        }

        var result = await _tester.TestAsync(name, ct);
        return JsonSerializer.SerializeToElement(new { ok = result.Ok, detail = result.Detail });
    }

    public async Task<JsonElement?> SetAsync(Guid adminId, string? ip, string name, JsonElement body, CancellationToken ct = default)
    {
        if (BackendSecrets.Find(name) is not null)
        {
            var value = body is { ValueKind: JsonValueKind.Object } && body.TryGetProperty("value", out var found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
            var why = ReasonOf(body);
            if (string.IsNullOrWhiteSpace(value) || why is null || why.Trim().Length < 3)
            {
                throw new BusinessRuleException("SECRET_INVALID", "Cần nhập key và lý do (ít nhất 3 ký tự).");
            }

            var saved = await _vault.SaveAsync(name, value, adminId, ct);
            await LogAsync(adminId, ip, name, "set", why, ct);
            return JsonSerializer.SerializeToElement(Shape(saved, _vault.CanStore));
        }

        var result = await _client.SendAsync(
            new AiRulesRequest(HttpMethod.Put, Uri.EscapeDataString(name), null, body, adminId, AiRulesRequest.SecretsArea), ct);

        // Chỉ tên key, hành động và lý do: tuyệt đối không đưa thân yêu cầu (có giá trị key) vào nhật ký.
        await LogAsync(adminId, ip, name, "set", ReasonOf(body), ct);
        return result;
    }

    public async Task<JsonElement?> ImportFromEnvironmentAsync(Guid adminId, string? ip, string? reason, CancellationToken ct = default)
    {
        if (!_vault.CanStore)
        {
            throw new BusinessRuleException("SECRET_STORE_UNAVAILABLE", "Backend chưa có SECRETS_MASTER_KEY nên chưa lưu được key vào hệ thống.");
        }

        var imported = new List<string>();
        var failed = new List<string>();
        foreach (var status in (await _vault.ListAsync(ct)).Where(s => s.Source == "env"))
        {
            var value = await _vault.GetAsync(status.Name, ct);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            try
            {
                await _vault.SaveAsync(status.Name, value, adminId, ct);
                imported.Add(status.Name);
            }
            catch (FinMateException)
            {
                failed.Add(status.Name);
            }
        }

        var aiUnavailable = false;
        try
        {
            var query = reason is null ? null : new Dictionary<string, string?> { ["reason"] = reason };
            var ai = await _client.SendAsync(new AiRulesRequest(HttpMethod.Post, "import-env", query, null, adminId, AiRulesRequest.SecretsArea), ct);
            if (ai is { ValueKind: JsonValueKind.Object } root)
            {
                imported.AddRange(Names(root, "imported"));
                failed.AddRange(Names(root, "failed"));
            }
        }
        catch (Exception ex) when (ex is FinMateException or HttpRequestException)
        {
            aiUnavailable = true;
        }

        await LogAsync(adminId, ip, "all", "import_env", reason, ct);
        return JsonSerializer.SerializeToElement(new { imported, failed, ai_unavailable = aiUnavailable });
    }

    private static IEnumerable<string> Names(JsonElement root, string property)
        => root.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToList()
            : [];

    public async Task<JsonElement?> DeleteAsync(Guid adminId, string? ip, string name, string? reason, CancellationToken ct = default)
    {
        if (BackendSecrets.Find(name) is not null)
        {
            var removed = await _vault.DeleteAsync(name, ct);
            await LogAsync(adminId, ip, name, "delete", reason, ct);
            return JsonSerializer.SerializeToElement(Shape(removed, _vault.CanStore));
        }

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
