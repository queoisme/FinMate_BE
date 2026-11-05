using FinMate.Domain.Entities;

namespace FinMate.Application.Support;

public record SupportAttachmentDto(Guid Id, string ContentType, int SizeBytes, DateTimeOffset CreatedAt);

/// <summary>DTO cho khách: không có người phụ trách, không có ghi chú nội bộ, không có hạn SLA.</summary>
public record SupportTicketDto(
    Guid Id,
    string Category,
    string Status,
    string Subject,
    int? Rating,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt);

public record SupportMessageDto(Guid Id, string Kind, string Body, DateTimeOffset CreatedAt, IReadOnlyList<SupportAttachmentDto> Attachments);

public record SupportTicketDetailDto(SupportTicketDto Ticket, IReadOnlyList<SupportMessageDto> Messages);

public record OpsTicketDto(
    Guid Id,
    Guid RequesterUserId,
    string? RequesterName,
    Guid? AssigneeUserId,
    string? AssigneeName,
    string Category,
    string Priority,
    string Status,
    string Subject,
    DateTimeOffset FirstResponseDueAt,
    DateTimeOffset ResolveDueAt,
    DateTimeOffset? FirstRespondedAt,
    bool FirstResponseOverdue,
    bool ResolutionOverdue,
    int? Rating,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt);

public record OpsMessageDto(Guid Id, string Kind, Guid? AuthorUserId, string? AuthorName, string Body, DateTimeOffset CreatedAt, IReadOnlyList<SupportAttachmentDto> Attachments);

public record OpsTicketDetailDto(OpsTicketDto Ticket, IReadOnlyList<OpsMessageDto> Messages);

public record OpsTicketPageDto(IReadOnlyList<OpsTicketDto> Items, int Total, int Offset, int Limit);

public record SupportAttachmentContent(byte[] Data, string ContentType);

internal static class SupportMapper
{
    public static SupportTicketDto ToCustomerDto(SupportTicket t)
        => new(t.Id, t.Category.ToString(), t.Status.ToString(), t.Subject, t.Rating, t.CreatedAt, t.UpdatedAt, t.ResolvedAt, t.ClosedAt);

    public static SupportAttachmentDto ToDto(Common.Interfaces.SupportAttachmentInfo a)
        => new(a.Id, a.ContentType, a.SizeBytes, a.CreatedAt);

    public static OpsTicketDto ToOpsDto(Common.Interfaces.OpsTicketRow row, DateTimeOffset now)
    {
        var t = row.Ticket;
        return new OpsTicketDto(
            t.Id,
            t.RequesterUserId,
            row.RequesterName,
            t.AssigneeUserId,
            row.AssigneeName,
            t.Category.ToString(),
            t.Priority.ToString(),
            t.Status.ToString(),
            t.Subject,
            t.FirstResponseDueAt,
            t.ResolveDueAt,
            t.FirstRespondedAt,
            (t.FirstRespondedAt ?? now) > t.FirstResponseDueAt,
            (t.ResolvedAt ?? t.ClosedAt ?? now) > t.ResolveDueAt,
            t.Rating,
            t.CreatedAt,
            t.UpdatedAt,
            t.ResolvedAt,
            t.ClosedAt);
    }
}
