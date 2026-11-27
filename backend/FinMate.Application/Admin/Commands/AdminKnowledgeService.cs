using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Admin.Commands;

/// <summary>
/// Kho tri thức của agent qua ai-service. Tài liệu đi Draft → Approved → Retired (hoặc Draft → Retired khi bị từ chối); chỉ tài liệu Approved còn hiệu lực mới được agent dùng.
/// Mỗi lần GỬI, DUYỆT, TỪ CHỐI, GỠ để lại một dòng audit_logs (mã tài liệu, không nội dung). Danh sách ứng viên chỉ có mã băm và số lần gặp: không bao giờ tự vào kho.
/// Phase 95: backend ghi người soạn (<c>knowledge_reviews</c>) vì AI Service không nhận id; người duyệt / từ chối phải khác người soạn và có quyền
/// <c>ApproveAgentContent</c> đọc từ DB.
/// </summary>
public interface IAdminKnowledgeService
{
    /// <summary>Danh sách của AI Service, mỗi tài liệu được ghép thêm <c>submitted_by</c>, <c>review_status</c>, <c>decision_note</c>.</summary>
    Task<JsonElement?> ListDocumentsAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default);

    /// <summary>Một tài liệu kèm toàn bộ nội dung, để người duyệt đọc trước khi duyệt.</summary>
    Task<JsonElement?> GetDocumentAsync(Guid documentId, CancellationToken ct = default);

    Task<JsonElement?> SubmitAsync(Guid adminId, JsonElement body, string? ip, CancellationToken ct = default);

    Task<JsonElement?> ApproveAsync(Guid adminId, Guid documentId, string? ip, CancellationToken ct = default);

    /// <summary>Phase 95: từ chối bản nháp kèm ghi chú (AI Service: Draft → Retired).</summary>
    Task<JsonElement?> RejectAsync(Guid adminId, Guid documentId, string? note, string? ip, CancellationToken ct = default);

    Task<JsonElement?> RetireAsync(Guid adminId, Guid documentId, string? ip, CancellationToken ct = default);

    Task<JsonElement?> ListCandidatesAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default);

    /// <summary>Câu hỏi chưa trả lời được gom theo chủ đề (nhiều lần gặp nhất trước), để biết cần viết tài liệu về mảng nào.</summary>
    Task<JsonElement?> ListCandidateTopicsAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default);

    Task<JsonElement?> ResolveCandidateAsync(Guid adminId, Guid candidateId, JsonElement body, CancellationToken ct = default);
}

public static class KnowledgeErrorCodes
{
    public const string ReviewMissing = "KNOWLEDGE_REVIEW_MISSING";
    public const string OwnDraft = "KNOWLEDGE_OWN_DRAFT";
    public const string SubmitFailed = "KNOWLEDGE_SUBMIT_FAILED";
    public const string NoteRequired = "KNOWLEDGE_NOTE_REQUIRED";
}

public sealed partial class AdminKnowledgeService(
    IAiRulesAdminClient client,
    IAuditLogService audit,
    IKnowledgeReviewRepository? reviews = null,
    IOpsAccessService? access = null,
    ILogger<AdminKnowledgeService>? logger = null) : IAdminKnowledgeService
{
    private const string Area = AiRulesRequest.KnowledgeArea;
    private const int MinNoteLength = 3;
    private const int MaxNoteLength = 1000;

    public async Task<JsonElement?> ListDocumentsAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default)
        => await EnrichAsync(await client.SendAsync(new AiRulesRequest(HttpMethod.Get, "documents", query, Area: Area), ct), ct);

    public Task<JsonElement?> GetDocumentAsync(Guid documentId, CancellationToken ct = default)
        => client.SendAsync(new AiRulesRequest(HttpMethod.Get, $"documents/{documentId}", null, Area: Area), ct);

    public async Task<JsonElement?> SubmitAsync(Guid adminId, JsonElement body, string? ip, CancellationToken ct = default)
    {
        var result = await client.SendAsync(new AiRulesRequest(HttpMethod.Post, "documents", null, body, adminId, Area), ct);
        var documentId = Guid.TryParse(StringOf(result, "id"), out var id) ? id : (Guid?)null;
        if (reviews is not null && documentId is { } created)
        {
            await RecordSubmissionAsync(created, adminId, ct);
        }

        await audit.LogAsync(AuditEvents.AdminKnowledgeSubmitted, adminId, ip, new { DocumentId = documentId?.ToString(), Chunks = IntOf(result, "chunks") }, ct);
        return result;
    }

    public async Task<JsonElement?> ApproveAsync(Guid adminId, Guid documentId, string? ip, CancellationToken ct = default)
    {
        var review = await RequireDecidableAsync(adminId, documentId, ct);
        var result = await TransitionAsync(adminId, documentId, "approve", AuditEvents.AdminKnowledgeApproved, ip, ct, review?.SubmittedBy);
        await RecordDecisionAsync(documentId, review, KnowledgeReviewStatus.Approved, adminId, null, ct);
        return result;
    }

    public async Task<JsonElement?> RejectAsync(Guid adminId, Guid documentId, string? note, string? ip, CancellationToken ct = default)
    {
        var clean = note?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Length < MinNoteLength || clean.Length > MaxNoteLength)
        {
            throw new BusinessRuleException(KnowledgeErrorCodes.NoteRequired, $"Cần ghi chú từ chối {MinNoteLength}–{MaxNoteLength} ký tự.");
        }

        var review = await RequireDecidableAsync(adminId, documentId, ct);
        var result = await TransitionAsync(adminId, documentId, "retire", AuditEvents.AdminKnowledgeRejected, ip, ct, review?.SubmittedBy);
        await RecordDecisionAsync(documentId, review, KnowledgeReviewStatus.Rejected, adminId, clean, ct);
        return result;
    }

    public async Task<JsonElement?> RetireAsync(Guid adminId, Guid documentId, string? ip, CancellationToken ct = default)
    {
        await RequireApproverAsync(adminId, ct);
        var result = await TransitionAsync(adminId, documentId, "retire", AuditEvents.AdminKnowledgeRetired, ip, ct);
        if (reviews is not null && await reviews.GetAsync(documentId, ct) is { } review)
        {
            review.Status = KnowledgeReviewStatus.Retired;
            await reviews.SaveChangesAsync(ct);
        }

        return result;
    }

    public Task<JsonElement?> ListCandidatesAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default)
        => client.SendAsync(new AiRulesRequest(HttpMethod.Get, "candidates", query, Area: Area), ct);

    public Task<JsonElement?> ListCandidateTopicsAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default)
        => client.SendAsync(new AiRulesRequest(HttpMethod.Get, "candidate-topics", query, Area: Area), ct);

    public Task<JsonElement?> ResolveCandidateAsync(Guid adminId, Guid candidateId, JsonElement body, CancellationToken ct = default)
        => client.SendAsync(new AiRulesRequest(HttpMethod.Post, $"candidates/{candidateId}/resolve", null, body, adminId, Area), ct);

    private async Task<JsonElement?> TransitionAsync(
        Guid adminId, Guid documentId, string action, string auditEvent, string? ip, CancellationToken ct, Guid? submittedBy = null)
    {
        var result = await client.SendAsync(new AiRulesRequest(HttpMethod.Post, $"documents/{documentId}/{action}", null, null, adminId, Area), ct);
        await audit.LogAsync(auditEvent, adminId, ip, new { DocumentId = documentId, SubmittedBy = submittedBy }, ct); // chỉ ghi SAU khi ai-service chấp nhận
        return result;
    }

    private static string? StringOf(JsonElement? json, string name)
        => json is { ValueKind: JsonValueKind.Object } v && v.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;

    private static int? IntOf(JsonElement? json, string name)
        => json is { ValueKind: JsonValueKind.Object } v && v.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : null;
}
