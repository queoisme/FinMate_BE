using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Admin.Commands;

/// <summary>
/// Kho tri thức của agent qua ai-service. Tài liệu đi Draft → Approved → Retired; chỉ tài liệu Approved còn hiệu lực mới được agent dùng. Mỗi lần GỬI, DUYỆT, GỠ để lại một dòng audit_logs
/// (mã tài liệu, không nội dung). Danh sách ứng viên chỉ có mã băm và số lần gặp: không bao giờ tự vào kho.
/// </summary>
public interface IAdminKnowledgeService
{
    Task<JsonElement?> ListDocumentsAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default);

    /// <summary>Một tài liệu kèm toàn bộ nội dung, để người duyệt đọc trước khi duyệt.</summary>
    Task<JsonElement?> GetDocumentAsync(Guid documentId, CancellationToken ct = default);

    Task<JsonElement?> SubmitAsync(Guid adminId, JsonElement body, string? ip, CancellationToken ct = default);

    Task<JsonElement?> ApproveAsync(Guid adminId, Guid documentId, string? ip, CancellationToken ct = default);

    Task<JsonElement?> RetireAsync(Guid adminId, Guid documentId, string? ip, CancellationToken ct = default);

    Task<JsonElement?> ListCandidatesAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default);

    Task<JsonElement?> ResolveCandidateAsync(Guid adminId, Guid candidateId, JsonElement body, CancellationToken ct = default);
}

public class AdminKnowledgeService(IAiRulesAdminClient client, IAuditLogService audit) : IAdminKnowledgeService
{
    private const string Area = AiRulesRequest.KnowledgeArea;

    public Task<JsonElement?> ListDocumentsAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default)
        => client.SendAsync(new AiRulesRequest(HttpMethod.Get, "documents", query, Area: Area), ct);

    public Task<JsonElement?> GetDocumentAsync(Guid documentId, CancellationToken ct = default)
        => client.SendAsync(new AiRulesRequest(HttpMethod.Get, $"documents/{documentId}", null, Area: Area), ct);

    public async Task<JsonElement?> SubmitAsync(Guid adminId, JsonElement body, string? ip, CancellationToken ct = default)
    {
        var result = await client.SendAsync(new AiRulesRequest(HttpMethod.Post, "documents", null, body, adminId, Area), ct);
        await audit.LogAsync(AuditEvents.AdminKnowledgeSubmitted, adminId, ip, new { DocumentId = StringOf(result, "id"), Chunks = IntOf(result, "chunks") }, ct);
        return result;
    }

    public Task<JsonElement?> ApproveAsync(Guid adminId, Guid documentId, string? ip, CancellationToken ct = default)
        => TransitionAsync(adminId, documentId, "approve", AuditEvents.AdminKnowledgeApproved, ip, ct);

    public Task<JsonElement?> RetireAsync(Guid adminId, Guid documentId, string? ip, CancellationToken ct = default)
        => TransitionAsync(adminId, documentId, "retire", AuditEvents.AdminKnowledgeRetired, ip, ct);

    public Task<JsonElement?> ListCandidatesAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default)
        => client.SendAsync(new AiRulesRequest(HttpMethod.Get, "candidates", query, Area: Area), ct);

    public Task<JsonElement?> ResolveCandidateAsync(Guid adminId, Guid candidateId, JsonElement body, CancellationToken ct = default)
        => client.SendAsync(new AiRulesRequest(HttpMethod.Post, $"candidates/{candidateId}/resolve", null, body, adminId, Area), ct);

    private async Task<JsonElement?> TransitionAsync(Guid adminId, Guid documentId, string action, string auditEvent, string? ip, CancellationToken ct)
    {
        var result = await client.SendAsync(new AiRulesRequest(HttpMethod.Post, $"documents/{documentId}/{action}", null, null, adminId, Area), ct);
        await audit.LogAsync(auditEvent, adminId, ip, new { DocumentId = documentId }, ct); // chỉ ghi SAU khi ai-service chấp nhận (bị từ chối thì ném và không tới đây)
        return result;
    }

    private static string? StringOf(JsonElement? json, string name)
        => json is { ValueKind: JsonValueKind.Object } v && v.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;

    private static int? IntOf(JsonElement? json, string name)
        => json is { ValueKind: JsonValueKind.Object } v && v.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : null;
}
