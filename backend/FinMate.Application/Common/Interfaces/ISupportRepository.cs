using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public sealed record OpsTicketFilter(
    Guid ActorId,
    DateTimeOffset Now,
    SupportTicketStatus? Status,
    SupportTicketPriority? Priority,
    SupportTicketCategory? Category,
    Guid? AssigneeUserId,
    bool UnassignedOnly,
    bool OverdueOnly,
    int Offset,
    int Limit);

public sealed record OpsTicketRow(SupportTicket Ticket, string? RequesterName, string? AssigneeName);

public sealed record SupportMessageRow(SupportTicketMessage Message, string? AuthorName);

public sealed record SupportAttachmentInfo(Guid Id, Guid MessageId, string ContentType, int SizeBytes, DateTimeOffset CreatedAt);

public interface ISupportRepository
{
    void AddTicket(SupportTicket ticket);
    void AddMessage(SupportTicketMessage message);
    void AddAttachment(SupportTicketAttachment attachment);

    /// <summary>Đường của khách: luôn lọc theo người tạo.</summary>
    Task<SupportTicket?> GetForRequesterAsync(Guid ticketId, Guid userId, CancellationToken ct = default);
    Task<List<SupportTicket>> ListForRequesterAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountOpenForRequesterAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Đường của đội vận hành: người gọi đã qua <c>IOpsAccessService</c>.</summary>
    Task<SupportTicket?> GetByIdAsync(Guid ticketId, CancellationToken ct = default);
    Task<OpsTicketRow?> GetRowAsync(Guid ticketId, CancellationToken ct = default);
    Task<(List<OpsTicketRow> Items, int Total)> QueueAsync(OpsTicketFilter filter, CancellationToken ct = default);

    Task<List<SupportMessageRow>> ListMessagesAsync(Guid ticketId, bool includeInternal, CancellationToken ct = default);
    Task<SupportTicketMessage?> GetMessageAsync(Guid ticketId, Guid messageId, CancellationToken ct = default);

    /// <summary>Thông tin ảnh, không nạp nội dung.</summary>
    Task<List<SupportAttachmentInfo>> ListAttachmentsAsync(Guid ticketId, CancellationToken ct = default);
    Task<int> CountAttachmentsAsync(Guid messageId, CancellationToken ct = default);
    Task<SupportTicketAttachment?> GetAttachmentAsync(Guid ticketId, Guid attachmentId, CancellationToken ct = default);

    Task SetAttachmentExpiryAsync(Guid ticketId, DateTimeOffset deleteAfter, CancellationToken ct = default);
    Task<List<SupportTicket>> ListResolvedBeforeAsync(DateTimeOffset before, int limit, CancellationToken ct = default);
    Task<int> DeleteExpiredAttachmentsAsync(DateTimeOffset now, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
