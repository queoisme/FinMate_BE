using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Support;

public sealed record SupportTicketDto(Guid Id, string Subject, string Body, string Status, string? Reply, string? ResolutionNote, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static SupportTicketDto From(SupportTicket t) => new(t.Id, t.Subject, t.Body, t.Status.ToString(), t.Reply, t.ResolutionNote, t.CreatedAt, t.UpdatedAt);
}

public sealed record SupportTicketPage(int Total, IReadOnlyList<SupportTicketDto> Items);

/// <param name="Available">false = trợ lý không dùng được lúc này (LLM sập): nhân sự vẫn xử lý khiếu nại bình thường.</param>
/// <param name="Verified">false = câu trả lời gợi ý bị bỏ vì chứa số tiền không có trong khiếu nại; <c>Summary</c> khi đó chỉ là tiêu đề.</param>
public sealed record TicketAssistDto(bool Available, string? Summary, string? SuggestedReply, bool Verified);

public sealed record TicketAssistResult(string Summary, string? SuggestedReply, bool Verified, AgentUsage? Usage);

public sealed record TicketAssistMessage(string Role, string Text);

/// <summary>
/// Luồng khiếu nại. Mọi chuyển trạng thái đi qua <see cref="TicketStateMachine"/> (sai trạng thái → 409, sai người → 403) và để lại một dòng audit_logs chỉ có mã khiếu nại, trạng thái cũ/mới và loại người làm
/// (không nội dung). Trợ lý chỉ GỢI Ý: <see cref="AssistAsync"/> không đổi gì trên khiếu nại; câu trả lời chỉ ra ngoài khi nhân sự gửi nó qua <see cref="StaffTransitionAsync"/>.
/// </summary>
public sealed class SupportTicketService(ISupportTicketRepository tickets, IAuditLogService audit, IAgentAiClient ai)
{
    private const int MaxSubject = 120;
    private const int MaxBody = 2000;
    private const int MaxNote = 500;
    public const int MaxOpenPerUser = 10;

    public async Task<SupportTicketDto> CreateAsync(Guid userId, string? subject, string? body, string? ip, CancellationToken ct = default)
    {
        subject = subject?.Trim();
        body = body?.Trim();
        if (string.IsNullOrEmpty(subject) || subject.Length > MaxSubject)
        {
            throw new BusinessRuleException("invalid_ticket_subject", $"Tiêu đề phải có 1–{MaxSubject} ký tự.");
        }

        if (string.IsNullOrEmpty(body) || body.Length > MaxBody)
        {
            throw new BusinessRuleException("invalid_ticket_body", $"Nội dung phải có 1–{MaxBody} ký tự.");
        }

        if (await tickets.CountOpenForUserAsync(userId, ct) >= MaxOpenPerUser)
        {
            throw new BusinessRuleException("ticket_limit_reached", $"Bạn đang có {MaxOpenPerUser} khiếu nại chưa đóng; đợi xử lý xong rồi gửi thêm.");
        }

        var now = DateTimeOffset.UtcNow;
        var ticket = new SupportTicket { Id = Guid.NewGuid(), UserId = userId, Subject = subject, Body = body, Status = SupportTicketStatus.Open, CreatedAt = now, UpdatedAt = now };
        await tickets.AddAsync(ticket, ct);
        await audit.LogAsync(AuditEvents.SupportTicketCreated, userId, ip, new { ticketId = ticket.Id }, ct);
        return SupportTicketDto.From(ticket);
    }

    public async Task<SupportTicketPage> ListMineAsync(Guid userId, SupportTicketStatus? status, int limit, int offset, CancellationToken ct = default)
    {
        var (total, items) = await tickets.ListForUserAsync(userId, status, Math.Clamp(limit, 1, 100), Math.Max(0, offset), ct);
        return new SupportTicketPage(total, items.Select(SupportTicketDto.From).ToList());
    }

    public async Task<SupportTicketDto> GetMineAsync(Guid userId, Guid id, CancellationToken ct = default)
        => SupportTicketDto.From(await tickets.GetByIdForUserAsync(id, userId, ct) ?? throw new NotFoundException("Khiếu nại", id));

    /// <summary>Chủ khiếu nại xác nhận đã giải quyết (<c>Responded → Resolved</c>) hoặc phản đối kết quả (<c>→ Reopened</c>).</summary>
    public async Task<SupportTicketDto> OwnerTransitionAsync(Guid userId, Guid id, SupportTicketStatus to, string? ip, CancellationToken ct = default)
    {
        var ticket = await tickets.GetByIdForUserAsync(id, userId, ct) ?? throw new NotFoundException("Khiếu nại", id);
        await ApplyAsync(ticket, to, TicketStateMachine.OwnerActors, TicketActor.Owner, userId, null, null, ip, ct);
        return SupportTicketDto.From(ticket);
    }

    public async Task<SupportTicketPage> StaffListAsync(SupportTicketStatus? status, string? search, int limit, int offset, CancellationToken ct = default)
    {
        var (total, items) = await tickets.ListAllAsync(status, search?.Trim(), Math.Clamp(limit, 1, 100), Math.Max(0, offset), ct);
        return new SupportTicketPage(total, items.Select(SupportTicketDto.From).ToList());
    }

    public async Task<SupportTicketDto> StaffGetAsync(Guid id, CancellationToken ct = default)
        => SupportTicketDto.From(await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Khiếu nại", id));

    /// <param name="reply">Bắt buộc khi chuyển sang Responded: câu trả lời nhân sự ĐÃ GỬI.</param>
    /// <param name="note">Bắt buộc khi quản lý quyết định cuối (Resolved / Rejected sau ManagerReview).</param>
    public async Task<SupportTicketDto> StaffTransitionAsync(Guid staffId, UserRole role, Guid id, SupportTicketStatus to, string? reply, string? note, string? ip, CancellationToken ct = default)
    {
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Khiếu nại", id);
        var actors = TicketStateMachine.StaffActors(role);
        var actor = actors.Contains(TicketActor.Manager) ? TicketActor.Manager : TicketActor.Support;
        await ApplyAsync(ticket, to, actors, actor, staffId, reply, note, ip, ct);
        return SupportTicketDto.From(ticket);
    }

    /// <summary>Tóm tắt và câu trả lời GỢI Ý. Không đổi khiếu nại, không gửi gì cho người dùng. Trợ lý hỏng thì <c>Available = false</c> và khiếu nại vẫn xử lý bình thường.</summary>
    public async Task<TicketAssistDto> AssistAsync(Guid id, CancellationToken ct = default)
    {
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Khiếu nại", id);
        var history = ticket.Reply is null ? [] : new List<TicketAssistMessage> { new("staff", ticket.Reply) };
        try
        {
            var result = await ai.TicketAssistAsync(ticket.Subject, ticket.Body, history, ct);
            return result is null ? new TicketAssistDto(false, null, null, false) : new TicketAssistDto(true, result.Summary, result.SuggestedReply, result.Verified);
        }
        catch (AIServiceUnavailableException)
        {
            return new TicketAssistDto(false, null, null, false);
        }
    }

    private async Task ApplyAsync(SupportTicket ticket, SupportTicketStatus to, IReadOnlySet<TicketActor> actors, TicketActor actor, Guid actorId, string? reply, string? note, string? ip, CancellationToken ct)
    {
        var from = ticket.Status;
        if (!TicketStateMachine.Exists(from, to))
        {
            throw new ConflictException("illegal_ticket_transition", $"Không thể chuyển khiếu nại từ {from} sang {to}.");
        }

        if (!TicketStateMachine.IsLegal(from, to, actors))
        {
            throw new ForbiddenException("Vai trò của bạn không được thực hiện bước này của khiếu nại.");
        }

        if (to == SupportTicketStatus.Responded)
        {
            reply = reply?.Trim();
            if (string.IsNullOrEmpty(reply) || reply.Length > MaxBody)
            {
                throw new BusinessRuleException("invalid_ticket_reply", $"Câu trả lời phải có 1–{MaxBody} ký tự.");
            }

            ticket.Reply = reply;
        }

        if (from == SupportTicketStatus.ManagerReview)
        {
            note = note?.Trim();
            if (string.IsNullOrEmpty(note) || note.Length < 3 || note.Length > MaxNote)
            {
                throw new BusinessRuleException("invalid_ticket_note", $"Quyết định cuối cần ghi chú 3–{MaxNote} ký tự.");
            }

            ticket.ResolutionNote = note;
        }

        ticket.Status = to;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await tickets.UpdateAsync(ticket, ct);
        await audit.LogAsync(AuditEvents.SupportTicketTransitioned, actorId, ip, new { ticketId = ticket.Id, from = from.ToString(), to = to.ToString(), actor = actor.ToString() }, ct);
    }
}
