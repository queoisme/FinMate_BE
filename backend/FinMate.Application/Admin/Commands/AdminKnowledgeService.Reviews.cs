using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Admin.Commands;

public sealed partial class AdminKnowledgeService
{
    private const string CreatedAtField = "created_at";

    /// <summary>
    /// S3: ghi người soạn ngay sau khi AI Service nhận tài liệu. Ghi hỏng thì tài liệu đó sẽ không bao giờ duyệt được (không biết người soạn), nên bù bằng cách gỡ nó
    /// ở AI Service (Draft → Retired, cố gắng tối đa) rồi báo lỗi để người soạn gửi lại. Gỡ cũng hỏng thì tài liệu nằm ở Draft, duyệt bị 409, gỡ tay được.
    /// </summary>
    private async Task RecordSubmissionAsync(Guid documentId, Guid adminId, CancellationToken ct)
    {
        try
        {
            await reviews!.AddAsync(new KnowledgeReview
            {
                DocumentId = documentId, SubmittedBy = adminId, SubmittedAt = DateTimeOffset.UtcNow, Status = KnowledgeReviewStatus.Draft,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning("Không ghi được người soạn cho tài liệu tri thức {DocumentId}; gỡ tài liệu ở AI Service ({ExceptionType})", documentId, ex.GetType().Name);
            await TryRetireOrphanAsync(documentId, adminId);
            throw new ServiceUnavailableException(KnowledgeErrorCodes.SubmitFailed, "Chưa lưu được tài liệu. Vui lòng gửi lại.");
        }
    }

    private async Task TryRetireOrphanAsync(Guid documentId, Guid adminId)
    {
        try
        {
            await client.SendAsync(new AiRulesRequest(HttpMethod.Post, $"documents/{documentId}/retire", null, null, adminId, Area), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning("Không gỡ được tài liệu tri thức mồ côi {DocumentId} ({ExceptionType}); tài liệu ở lại Draft và không duyệt được", documentId, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Người duyệt có quyền (đọc DB), tài liệu có người soạn được ghi lại và người đó không phải người duyệt. Không có dòng ghi: chỉ tài liệu tạo ở AI Service
    /// TRƯỚC mốc triển khai mới được duyệt (S3); tài liệu mới mà thiếu dòng → 409.
    /// </summary>
    private async Task<KnowledgeReview?> RequireDecidableAsync(Guid adminId, Guid documentId, CancellationToken ct)
    {
        await RequireApproverAsync(adminId, ct);
        if (reviews is null)
        {
            return null;
        }

        var review = await reviews.GetAsync(documentId, ct);
        if (review is null)
        {
            await RequireLegacyAsync(documentId, ct);
            return null;
        }

        if (review.SubmittedBy == adminId)
        {
            throw new ForbiddenException(KnowledgeErrorCodes.OwnDraft, "Bạn không thể tự duyệt hay từ chối tài liệu do chính mình gửi.");
        }

        return review;
    }

    private async Task RequireLegacyAsync(Guid documentId, CancellationToken ct)
    {
        var trackedSince = await reviews!.GetTrackedSinceAsync(ct);
        var document = await GetDocumentAsync(documentId, ct);
        var createdAt = document is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(CreatedAtField, out var c) && c.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(c.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : (DateTimeOffset?)null;
        if (trackedSince is null || createdAt is null || createdAt >= trackedSince)
        {
            throw new ConflictException(KnowledgeErrorCodes.ReviewMissing, "Không có thông tin người gửi tài liệu này nên không duyệt được. Gỡ tài liệu rồi gửi lại.");
        }
    }

    /// <summary>Quyền duyệt đọc lại từ DB (D15), ngoài policy theo vai trò trong JWT ở controller.</summary>
    private async Task RequireApproverAsync(Guid adminId, CancellationToken ct)
    {
        if (access is not null)
        {
            await access.RequireAsync(adminId, OpsCapability.ApproveAgentContent, ct);
        }
    }

    /// <summary>Ghi quyết định; tài liệu cũ (không có dòng) được thêm một dòng không có người soạn (D16, S3).</summary>
    private async Task RecordDecisionAsync(Guid documentId, KnowledgeReview? review, KnowledgeReviewStatus status, Guid adminId, string? note, CancellationToken ct)
    {
        if (reviews is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (review is null)
        {
            await reviews.AddAsync(new KnowledgeReview
            {
                DocumentId = documentId, SubmittedBy = null, SubmittedAt = now, Status = status, DecidedBy = adminId, DecidedAt = now, DecisionNote = note,
            }, ct);
            return;
        }

        review.Status = status;
        review.DecidedBy = adminId;
        review.DecidedAt = now;
        review.DecisionNote = note;
        await reviews.SaveChangesAsync(ct);
    }

    /// <summary>Ghép người soạn và trạng thái duyệt vào từng tài liệu của danh sách AI Service trả về.</summary>
    private async Task<JsonElement?> EnrichAsync(JsonElement? page, CancellationToken ct)
    {
        if (reviews is null || page is not { ValueKind: JsonValueKind.Object } value || JsonNode.Parse(value.GetRawText()) is not JsonObject root
            || root["items"] is not JsonArray items)
        {
            return page;
        }

        var ids = items.Select(i => Guid.TryParse(i?["id"]?.GetValue<string>(), out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
        var found = await reviews.GetManyAsync(ids, ct);
        foreach (var item in items.OfType<JsonObject>())
        {
            var review = Guid.TryParse(item["id"]?.GetValue<string>(), out var id) ? found.GetValueOrDefault(id) : null;
            item["submitted_by"] = review?.SubmittedBy?.ToString();
            item["review_status"] = review?.Status.ToString().ToLowerInvariant();
            item["decision_note"] = review?.DecisionNote;
        }

        return JsonSerializer.Deserialize<JsonElement>(root.ToJsonString());
    }
}
