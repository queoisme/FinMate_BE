using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Support;

/// <summary>Phía khách. Mọi truy vấn đều lọc theo <c>userId</c> lấy từ JWT; ca của người khác trả 404, không phải 403.</summary>
public interface ISupportTicketService
{
    Task<SupportTicketDetailDto> CreateAsync(Guid userId, SupportTicketCategory category, string? subject, string? body, CancellationToken ct = default);
    Task<IReadOnlyList<SupportTicketDto>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<SupportTicketDetailDto> GetAsync(Guid userId, Guid ticketId, CancellationToken ct = default);
    Task<SupportTicketDetailDto> ReplyAsync(Guid userId, Guid ticketId, string? body, CancellationToken ct = default);
    Task<SupportAttachmentDto> AddAttachmentAsync(Guid userId, Guid ticketId, Guid messageId, byte[] content, CancellationToken ct = default);
    Task<SupportAttachmentContent> GetAttachmentAsync(Guid userId, Guid ticketId, Guid attachmentId, CancellationToken ct = default);
    Task<SupportTicketDto> CloseAsync(Guid userId, Guid ticketId, CancellationToken ct = default);
    Task<SupportTicketDto> RateAsync(Guid userId, Guid ticketId, int rating, CancellationToken ct = default);
}

public sealed class SupportTicketService : ISupportTicketService
{
    private readonly ISupportRepository _support;
    private readonly ISystemConfigProvider _config;
    private readonly IAttachmentProtector _protector;

    public SupportTicketService(ISupportRepository support, ISystemConfigProvider config, IAttachmentProtector protector)
    {
        _support = support;
        _config = config;
        _protector = protector;
    }

    private async Task<SystemConfigDocument> EnabledConfigAsync(CancellationToken ct)
    {
        var document = (await _config.GetAsync(ct)).Document;
        if (!document.Features.Support)
        {
            throw new BusinessRuleException(SupportErrorCodes.Disabled, "Tính năng hỗ trợ chưa được bật.");
        }

        return document;
    }

    private async Task<SupportTicket> RequireTicketAsync(Guid userId, Guid ticketId, CancellationToken ct)
        => await _support.GetForRequesterAsync(ticketId, userId, ct)
            ?? throw new NotFoundException("SupportTicket", ticketId);

    public async Task<SupportTicketDetailDto> CreateAsync(Guid userId, SupportTicketCategory category, string? subject, string? body, CancellationToken ct = default)
    {
        var settings = (await EnabledConfigAsync(ct)).Support;
        if (!Enum.IsDefined(category))
        {
            throw new BusinessRuleException(SupportErrorCodes.InvalidInput, "Loại yêu cầu không hợp lệ.");
        }

        var cleanSubject = SupportTicketRules.RequireText(subject, SupportTicketRules.MaxSubjectLength, "tiêu đề");
        var cleanBody = SupportTicketRules.RequireText(body, SupportTicketRules.MaxBodyLength, "nội dung");

        if (await _support.CountOpenForRequesterAsync(userId, ct) >= settings.MaxOpenTicketsPerUser)
        {
            throw new BusinessRuleException(SupportErrorCodes.TooManyOpenTickets, "Bạn đang có quá nhiều yêu cầu chưa đóng.");
        }

        var now = DateTimeOffset.UtcNow;
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            RequesterUserId = userId,
            Category = category,
            Priority = SupportTicketRules.DefaultPriority(category),
            Status = SupportTicketStatus.New,
            Subject = cleanSubject,
            CreatedAt = now,
            UpdatedAt = now,
        };
        SupportTicketRules.ApplyDueDates(ticket, settings);
        _support.AddTicket(ticket);
        _support.AddMessage(new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            AuthorUserId = userId,
            Kind = SupportMessageKind.Customer,
            Body = cleanBody,
            CreatedAt = now,
        });
        await _support.SaveChangesAsync(ct);

        return await DetailAsync(ticket, ct);
    }

    public async Task<IReadOnlyList<SupportTicketDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        await EnabledConfigAsync(ct);
        var tickets = await _support.ListForRequesterAsync(userId, ct);
        return tickets.Select(SupportMapper.ToCustomerDto).ToList();
    }

    public async Task<SupportTicketDetailDto> GetAsync(Guid userId, Guid ticketId, CancellationToken ct = default)
    {
        await EnabledConfigAsync(ct);
        return await DetailAsync(await RequireTicketAsync(userId, ticketId, ct), ct);
    }

    public async Task<SupportTicketDetailDto> ReplyAsync(Guid userId, Guid ticketId, string? body, CancellationToken ct = default)
    {
        await EnabledConfigAsync(ct);
        var ticket = await RequireTicketAsync(userId, ticketId, ct);
        SupportTicketRules.EnsureNotClosed(ticket);
        var cleanBody = SupportTicketRules.RequireText(body, SupportTicketRules.MaxBodyLength, "nội dung");

        var now = DateTimeOffset.UtcNow;
        _support.AddMessage(new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            AuthorUserId = userId,
            Kind = SupportMessageKind.Customer,
            Body = cleanBody,
            CreatedAt = now,
        });

        // Khách trả lời nghĩa là việc lại về phía đội hỗ trợ, kể cả ca đã giải quyết mà khách thấy chưa xong.
        if (ticket.Status is SupportTicketStatus.WaitingCustomer or SupportTicketStatus.Resolved)
        {
            ticket.Status = ticket.AssigneeUserId is null ? SupportTicketStatus.New : SupportTicketStatus.InProgress;
            ticket.ResolvedAt = null;
        }

        ticket.UpdatedAt = now;
        await _support.SaveChangesAsync(ct);
        return await DetailAsync(ticket, ct);
    }

    public async Task<SupportAttachmentDto> AddAttachmentAsync(Guid userId, Guid ticketId, Guid messageId, byte[] content, CancellationToken ct = default)
    {
        var settings = (await EnabledConfigAsync(ct)).Support;
        var ticket = await RequireTicketAsync(userId, ticketId, ct);
        SupportTicketRules.EnsureNotClosed(ticket);

        var message = await _support.GetMessageAsync(ticketId, messageId, ct);
        if (message is null || message.Kind != SupportMessageKind.Customer || message.AuthorUserId != userId)
        {
            throw new NotFoundException("SupportTicketMessage", messageId);
        }

        // Không có khóa chủ thì từ chối hẳn, không lưu bản rõ.
        if (!_protector.Available)
        {
            throw new BusinessRuleException(SupportErrorCodes.AttachmentsUnavailable, "Hiện chưa gửi được ảnh. Vui lòng mô tả bằng chữ.");
        }

        if (content.Length == 0 || content.Length > SupportTicketRules.MaxAttachmentBytes)
        {
            throw new BusinessRuleException(SupportErrorCodes.AttachmentInvalid, $"Ảnh tối đa {SupportTicketRules.MaxAttachmentBytes / (1024 * 1024)}MB.");
        }

        var contentType = SupportTicketRules.DetectImageType(content)
            ?? throw new BusinessRuleException(SupportErrorCodes.AttachmentInvalid, "Chỉ nhận ảnh JPEG, PNG, WebP, HEIC hoặc HEIF.");

        if (await _support.CountAttachmentsAsync(messageId, ct) >= settings.MaxAttachmentsPerMessage)
        {
            throw new BusinessRuleException(SupportErrorCodes.TooManyAttachments, $"Mỗi tin nhắn gửi được tối đa {settings.MaxAttachmentsPerMessage} ảnh.");
        }

        var id = Guid.NewGuid();
        var (ciphertext, nonce) = _protector.Protect(id, content);
        var attachment = new SupportTicketAttachment
        {
            Id = id,
            TicketId = ticketId,
            MessageId = messageId,
            ContentType = contentType,
            SizeBytes = content.Length,
            Data = ciphertext,
            Nonce = nonce,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _support.AddAttachment(attachment);
        ticket.UpdatedAt = attachment.CreatedAt;
        await _support.SaveChangesAsync(ct);

        return new SupportAttachmentDto(id, contentType, content.Length, attachment.CreatedAt);
    }

    public async Task<SupportAttachmentContent> GetAttachmentAsync(Guid userId, Guid ticketId, Guid attachmentId, CancellationToken ct = default)
    {
        await EnabledConfigAsync(ct);
        await RequireTicketAsync(userId, ticketId, ct);
        var attachment = await _support.GetAttachmentAsync(ticketId, attachmentId, ct)
            ?? throw new NotFoundException("SupportTicketAttachment", attachmentId);
        var data = _protector.Unprotect(attachment.Id, attachment.Data, attachment.Nonce)
            ?? throw new NotFoundException("SupportTicketAttachment", attachmentId);
        return new SupportAttachmentContent(data, attachment.ContentType);
    }

    public async Task<SupportTicketDto> CloseAsync(Guid userId, Guid ticketId, CancellationToken ct = default)
    {
        var settings = (await EnabledConfigAsync(ct)).Support;
        var ticket = await RequireTicketAsync(userId, ticketId, ct);
        if (ticket.Status != SupportTicketStatus.Closed)
        {
            await SupportTicketClosing.CloseAsync(_support, ticket, settings, DateTimeOffset.UtcNow, ct);
        }

        return SupportMapper.ToCustomerDto(ticket);
    }

    public async Task<SupportTicketDto> RateAsync(Guid userId, Guid ticketId, int rating, CancellationToken ct = default)
    {
        await EnabledConfigAsync(ct);
        var ticket = await RequireTicketAsync(userId, ticketId, ct);
        if (rating is < 1 or > 5)
        {
            throw new BusinessRuleException(SupportErrorCodes.InvalidInput, "Điểm đánh giá phải từ 1 đến 5.");
        }

        if (ticket.Status is not (SupportTicketStatus.Resolved or SupportTicketStatus.Closed))
        {
            throw new BusinessRuleException(SupportErrorCodes.RatingNotAllowed, "Chỉ đánh giá được khi yêu cầu đã giải quyết.");
        }

        ticket.Rating = rating;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _support.SaveChangesAsync(ct);
        return SupportMapper.ToCustomerDto(ticket);
    }

    private async Task<SupportTicketDetailDto> DetailAsync(SupportTicket ticket, CancellationToken ct)
    {
        var messages = await _support.ListMessagesAsync(ticket.Id, includeInternal: false, ct);
        var attachments = (await _support.ListAttachmentsAsync(ticket.Id, ct)).ToLookup(a => a.MessageId);
        return new SupportTicketDetailDto(
            SupportMapper.ToCustomerDto(ticket),
            messages.Select(m => new SupportMessageDto(
                m.Message.Id,
                m.Message.Kind.ToString(),
                m.Message.Body,
                m.Message.CreatedAt,
                attachments[m.Message.Id].Select(SupportMapper.ToDto).ToList())).ToList());
    }
}

/// <summary>Đóng ca từ bất kỳ đường nào cũng phải đặt hạn xóa ảnh và chấm dứt quyền xem dữ liệu, nên gom về một chỗ.</summary>
internal static class SupportTicketClosing
{
    public static async Task CloseAsync(ISupportRepository support, SupportTicket ticket, SupportSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        ticket.Status = SupportTicketStatus.Closed;
        ticket.ClosedAt = now;
        ticket.UpdatedAt = now;
        await support.SaveChangesAsync(ct);
        await support.SetAttachmentExpiryAsync(ticket.Id, now.AddDays(settings.AttachmentRetentionDays), ct);
        await support.EndGrantsAsync(ticket.Id, now, ct);
    }
}
