using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Support;

public sealed record OpsTicketQuery(
    SupportTicketStatus? Status,
    SupportTicketPriority? Priority,
    SupportTicketCategory? Category,
    string? Assignee,
    bool Overdue,
    int Offset,
    int Limit);

/// <summary>
/// Phía đội vận hành. Hàng đợi chỉ hiện thông tin tóm tắt; đọc nội dung ca cần là người phụ trách, ca chưa ai nhận, hoặc có quyền điều phối.
/// Audit chỉ ghi id, trạng thái, không ghi nội dung.
/// </summary>
public interface IOpsTicketService
{
    Task<OpsTicketPageDto> QueueAsync(Guid actorId, OpsTicketQuery query, CancellationToken ct = default);
    Task<OpsTicketDetailDto> GetAsync(Guid actorId, Guid ticketId, CancellationToken ct = default);
    Task<OpsTicketDetailDto> ClaimAsync(Guid actorId, Guid ticketId, string? ip, CancellationToken ct = default);
    Task<OpsTicketDetailDto> ReplyAsync(Guid actorId, Guid ticketId, string? body, bool internalNote, CancellationToken ct = default);
    Task<OpsTicketDetailDto> SetStatusAsync(Guid actorId, Guid ticketId, SupportTicketStatus status, string? ip, CancellationToken ct = default);
    Task<OpsTicketDetailDto> EscalateAsync(Guid actorId, Guid ticketId, string? note, string? ip, CancellationToken ct = default);
    Task<OpsTicketDetailDto> AssignAsync(Guid actorId, Guid ticketId, Guid assigneeUserId, string? ip, CancellationToken ct = default);
    Task<OpsTicketDetailDto> SetPriorityAsync(Guid actorId, Guid ticketId, SupportTicketPriority priority, string? ip, CancellationToken ct = default);
    Task<SupportAttachmentContent> GetAttachmentAsync(Guid actorId, Guid ticketId, Guid attachmentId, string? ip, CancellationToken ct = default);
}

public sealed class OpsTicketService : IOpsTicketService
{
    private const int MaxPageSize = 100;

    private readonly ISupportRepository _support;
    private readonly IOpsAccessService _access;
    private readonly IUserRepository _users;
    private readonly ISystemConfigProvider _config;
    private readonly IAttachmentProtector _protector;
    private readonly IPushNotificationService _push;
    private readonly IAuditLogService _audit;

    public OpsTicketService(
        ISupportRepository support,
        IOpsAccessService access,
        IUserRepository users,
        ISystemConfigProvider config,
        IAttachmentProtector protector,
        IPushNotificationService push,
        IAuditLogService audit)
    {
        _support = support;
        _access = access;
        _users = users;
        _config = config;
        _protector = protector;
        _push = push;
        _audit = audit;
    }

    public async Task<OpsTicketPageDto> QueueAsync(Guid actorId, OpsTicketQuery query, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        var offset = Math.Max(query.Offset, 0);
        var limit = Math.Clamp(query.Limit, 1, MaxPageSize);

        Guid? assignee = null;
        var unassigned = false;
        switch (query.Assignee?.Trim().ToLowerInvariant())
        {
            case null or "":
                break;
            case "me":
                assignee = actor.Id;
                break;
            case "none":
                unassigned = true;
                break;
            default:
                if (!Guid.TryParse(query.Assignee, out var parsed))
                {
                    throw new BusinessRuleException(SupportErrorCodes.InvalidInput, "Giá trị assignee phải là me, none hoặc một id.");
                }

                assignee = parsed;
                break;
        }

        var now = DateTimeOffset.UtcNow;
        var (items, total) = await _support.QueueAsync(
            new OpsTicketFilter(actor.Id, now, query.Status, query.Priority, query.Category, assignee, unassigned, query.Overdue, offset, limit), ct);
        return new OpsTicketPageDto(items.Select(r => SupportMapper.ToOpsDto(r, now)).ToList(), total, offset, limit);
    }

    public async Task<OpsTicketDetailDto> GetAsync(Guid actorId, Guid ticketId, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        EnsureNotOwnTicket(actor, ticket);
        if (ticket.AssigneeUserId is not null)
        {
            EnsureHandler(actor, ticket);
        }

        return await DetailAsync(ticketId, ct);
    }

    public async Task<OpsTicketDetailDto> ClaimAsync(Guid actorId, Guid ticketId, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        EnsureNotOwnTicket(actor, ticket);
        SupportTicketRules.EnsureNotClosed(ticket);

        if (ticket.AssigneeUserId is not null)
        {
            throw new ConflictException(SupportErrorCodes.AlreadyAssigned, "Yêu cầu này đã có người nhận.");
        }

        ticket.AssigneeUserId = actor.Id;
        if (ticket.Status is SupportTicketStatus.New or SupportTicketStatus.Escalated)
        {
            ticket.Status = SupportTicketStatus.InProgress;
        }

        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _support.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsTicketClaimed, actor.Id, ip, new { ticketId }, ct);
        return await DetailAsync(ticketId, ct);
    }

    public async Task<OpsTicketDetailDto> ReplyAsync(Guid actorId, Guid ticketId, string? body, bool internalNote, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        EnsureHandler(actor, ticket);
        SupportTicketRules.EnsureNotClosed(ticket);
        var cleanBody = SupportTicketRules.RequireText(body, SupportTicketRules.MaxBodyLength, "nội dung");

        var now = DateTimeOffset.UtcNow;
        _support.AddMessage(new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            AuthorUserId = actor.Id,
            Kind = internalNote ? SupportMessageKind.InternalNote : SupportMessageKind.Staff,
            Body = cleanBody,
            CreatedAt = now,
        });
        ticket.UpdatedAt = now;

        if (!internalNote)
        {
            ticket.FirstRespondedAt ??= now;
            ticket.Status = SupportTicketStatus.WaitingCustomer;
            ticket.ResolvedAt = null;
        }

        await _support.SaveChangesAsync(ct);

        if (!internalNote)
        {
            // Không kèm nội dung trả lời: màn hình khóa của điện thoại ai cũng đọc được.
            await _push.NotifyAsync(
                ticket.RequesterUserId,
                "FinMate hỗ trợ",
                "Có phản hồi mới cho yêu cầu của bạn.",
                new Dictionary<string, string> { ["action"] = "support_ticket", ["ticketId"] = ticket.Id.ToString() },
                ct);
        }

        return await DetailAsync(ticketId, ct);
    }

    public async Task<OpsTicketDetailDto> SetStatusAsync(Guid actorId, Guid ticketId, SupportTicketStatus status, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        EnsureHandler(actor, ticket);
        SupportTicketRules.EnsureNotClosed(ticket);

        if (status is not (SupportTicketStatus.InProgress or SupportTicketStatus.WaitingCustomer or SupportTicketStatus.Resolved or SupportTicketStatus.Closed))
        {
            throw new BusinessRuleException(SupportErrorCodes.InvalidStatusChange, "Không đổi được sang trạng thái này.");
        }

        var previous = ticket.Status;
        if (previous == status)
        {
            return await DetailAsync(ticketId, ct);
        }

        var now = DateTimeOffset.UtcNow;
        if (status == SupportTicketStatus.Closed)
        {
            var settings = (await _config.GetAsync(ct)).Document.Support;
            await SupportTicketClosing.CloseAsync(_support, ticket, settings, now, ct);
        }
        else
        {
            ticket.Status = status;
            ticket.ResolvedAt = status == SupportTicketStatus.Resolved ? now : null;
            ticket.UpdatedAt = now;
            await _support.SaveChangesAsync(ct);
        }

        await _audit.LogAsync(AuditEvents.OpsTicketStatusChanged, actor.Id, ip, new { ticketId, from = previous.ToString(), to = status.ToString() }, ct);
        return await DetailAsync(ticketId, ct);
    }

    public async Task<OpsTicketDetailDto> EscalateAsync(Guid actorId, Guid ticketId, string? note, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        EnsureHandler(actor, ticket);
        SupportTicketRules.EnsureNotClosed(ticket);
        var cleanNote = SupportTicketRules.RequireText(note, SupportTicketRules.MaxBodyLength, "những gì đã thử");

        var now = DateTimeOffset.UtcNow;
        _support.AddMessage(new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            AuthorUserId = actor.Id,
            Kind = SupportMessageKind.InternalNote,
            Body = cleanNote,
            CreatedAt = now,
        });
        ticket.Status = SupportTicketStatus.Escalated;
        ticket.UpdatedAt = now;
        await _support.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsTicketEscalated, actor.Id, ip, new { ticketId }, ct);
        return await DetailAsync(ticketId, ct);
    }

    public async Task<OpsTicketDetailDto> AssignAsync(Guid actorId, Guid ticketId, Guid assigneeUserId, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ManageTickets, ct);
        var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        EnsureNotOwnTicket(actor, ticket);
        SupportTicketRules.EnsureNotClosed(ticket);

        var assignee = await _users.GetByIdAsync(assigneeUserId, ct);
        if (assignee is null || assignee.IsLocked || !OpsPermissions.For(assignee.Role).HasFlag(OpsCapability.HandleTickets) || assignee.Id == ticket.RequesterUserId)
        {
            throw new BusinessRuleException(SupportErrorCodes.InvalidAssignee, "Không giao được yêu cầu này cho người đó.");
        }

        var previous = ticket.AssigneeUserId;
        ticket.AssigneeUserId = assignee.Id;
        if (ticket.Status is SupportTicketStatus.New or SupportTicketStatus.Escalated)
        {
            ticket.Status = SupportTicketStatus.InProgress;
        }

        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _support.SaveChangesAsync(ct);

        // Khách đồng ý với người phụ trách trước đó, không phải người mới.
        if (previous != assignee.Id)
        {
            await _support.EndGrantsAsync(ticket.Id, ticket.UpdatedAt, ct);
        }

        await _audit.LogAsync(AuditEvents.OpsTicketAssigned, actor.Id, ip, new { ticketId, from = previous, to = assignee.Id }, ct);
        return await DetailAsync(ticketId, ct);
    }

    public async Task<OpsTicketDetailDto> SetPriorityAsync(Guid actorId, Guid ticketId, SupportTicketPriority priority, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ManageTickets, ct);
        var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        EnsureNotOwnTicket(actor, ticket);
        SupportTicketRules.EnsureNotClosed(ticket);

        if (!Enum.IsDefined(priority))
        {
            throw new BusinessRuleException(SupportErrorCodes.InvalidInput, "Mức ưu tiên không hợp lệ.");
        }

        var previous = ticket.Priority;
        if (previous == priority)
        {
            return await DetailAsync(ticketId, ct);
        }

        ticket.Priority = priority;
        SupportTicketRules.ApplyDueDates(ticket, (await _config.GetAsync(ct)).Document.Support);
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _support.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsTicketPriorityChanged, actor.Id, ip, new { ticketId, from = previous.ToString(), to = priority.ToString() }, ct);
        return await DetailAsync(ticketId, ct);
    }

    public async Task<SupportAttachmentContent> GetAttachmentAsync(Guid actorId, Guid ticketId, Guid attachmentId, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        EnsureHandler(actor, ticket);

        var attachment = await _support.GetAttachmentAsync(ticketId, attachmentId, ct)
            ?? throw new NotFoundException("SupportTicketAttachment", attachmentId);
        var data = _protector.Unprotect(attachment.Id, attachment.Data, attachment.Nonce)
            ?? throw new NotFoundException("SupportTicketAttachment", attachmentId);

        await _audit.LogAsync(AuditEvents.OpsTicketAttachmentViewed, actor.Id, ip, new { ticketId, attachmentId }, ct);
        return new SupportAttachmentContent(data, attachment.ContentType);
    }

    private static void EnsureNotOwnTicket(User actor, SupportTicket ticket)
    {
        if (ticket.RequesterUserId == actor.Id)
        {
            throw new ForbiddenException("Bạn không thể xử lý yêu cầu của chính mình.");
        }
    }

    /// <summary>Người đang phụ trách, hoặc người có quyền điều phối. Không phải ca của chính mình.</summary>
    private static void EnsureHandler(User actor, SupportTicket ticket)
    {
        EnsureNotOwnTicket(actor, ticket);
        if (ticket.AssigneeUserId != actor.Id && !OpsPermissions.For(actor.Role).HasFlag(OpsCapability.ManageTickets))
        {
            throw new ForbiddenException("Bạn không phụ trách yêu cầu này.");
        }
    }

    private async Task<OpsTicketDetailDto> DetailAsync(Guid ticketId, CancellationToken ct)
    {
        var row = await _support.GetRowAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        var messages = await _support.ListMessagesAsync(ticketId, includeInternal: true, ct);
        var attachments = (await _support.ListAttachmentsAsync(ticketId, ct)).ToLookup(a => a.MessageId);
        return new OpsTicketDetailDto(
            SupportMapper.ToOpsDto(row, DateTimeOffset.UtcNow),
            messages.Select(m => new OpsMessageDto(
                m.Message.Id,
                m.Message.Kind.ToString(),
                m.Message.AuthorUserId,
                m.AuthorName,
                m.Message.Body,
                m.Message.CreatedAt,
                attachments[m.Message.Id].Select(SupportMapper.ToDto).ToList())).ToList());
    }
}
