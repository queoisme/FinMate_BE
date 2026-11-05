using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Support;

public static class SupportErrorCodes
{
    public const string Disabled = "SUPPORT_DISABLED";
    public const string InvalidInput = "SUPPORT_INVALID_INPUT";
    public const string TooManyOpenTickets = "SUPPORT_TOO_MANY_OPEN_TICKETS";
    public const string TicketClosed = "SUPPORT_TICKET_CLOSED";
    public const string InvalidStatusChange = "SUPPORT_INVALID_STATUS_CHANGE";
    public const string AlreadyAssigned = "SUPPORT_TICKET_ALREADY_ASSIGNED";
    public const string OwnTicket = "SUPPORT_OWN_TICKET";
    public const string InvalidAssignee = "SUPPORT_INVALID_ASSIGNEE";
    public const string RatingNotAllowed = "SUPPORT_RATING_NOT_ALLOWED";
    public const string AttachmentsUnavailable = "SUPPORT_ATTACHMENTS_UNAVAILABLE";
    public const string AttachmentInvalid = "SUPPORT_ATTACHMENT_INVALID";
    public const string TooManyAttachments = "SUPPORT_TOO_MANY_ATTACHMENTS";
    public const string InvalidLink = "SUPPORT_INVALID_LINK";
    public const string AccessRequestPending = "SUPPORT_ACCESS_REQUEST_PENDING";
    public const string AccessRequestNotPending = "SUPPORT_ACCESS_REQUEST_NOT_PENDING";
    public const string DraftAccountRequired = "SUPPORT_DRAFT_ACCOUNT_REQUIRED";
}

/// <summary>Quy tắc chung của ticket, không phụ thuộc người gọi là khách hay đội vận hành.</summary>
public static class SupportTicketRules
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 4000;
    public const int MaxAttachmentBytes = 5 * 1024 * 1024;

    /// <summary>Khách không tự chọn mức ưu tiên, nếu không ai cũng chọn cao nhất. Manager đổi lại được.</summary>
    public static SupportTicketPriority DefaultPriority(SupportTicketCategory category) => category switch
    {
        SupportTicketCategory.Security => SupportTicketPriority.P1,
        SupportTicketCategory.Question => SupportTicketPriority.P3,
        _ => SupportTicketPriority.P2,
    };

    public static void ApplyDueDates(SupportTicket ticket, SupportSettings settings)
    {
        var (firstResponse, resolve) = ticket.Priority switch
        {
            SupportTicketPriority.P1 => (settings.P1FirstResponseMinutes, settings.P1ResolveMinutes),
            SupportTicketPriority.P2 => (settings.P2FirstResponseMinutes, settings.P2ResolveMinutes),
            _ => (settings.P3FirstResponseMinutes, settings.P3ResolveMinutes),
        };
        ticket.FirstResponseDueAt = ticket.CreatedAt.AddMinutes(firstResponse);
        ticket.ResolveDueAt = ticket.CreatedAt.AddMinutes(resolve);
    }

    public static string RequireText(string? value, int maxLength, string field)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new BusinessRuleException(SupportErrorCodes.InvalidInput, $"Vui lòng nhập {field}.");
        }

        if (text.Length > maxLength)
        {
            throw new BusinessRuleException(SupportErrorCodes.InvalidInput, $"{field} tối đa {maxLength} ký tự.");
        }

        return text;
    }

    public static void EnsureNotClosed(SupportTicket ticket)
    {
        if (ticket.Status == SupportTicketStatus.Closed)
        {
            throw new BusinessRuleException(SupportErrorCodes.TicketClosed, "Yêu cầu này đã đóng.");
        }
    }

    /// <summary>
    /// Nhận dạng ảnh theo nội dung thật, không tin <c>Content-Type</c> do client khai. Trả null nếu không phải định dạng được phép.
    /// Định dạng này được gửi lại cho nhân viên mở trên trình duyệt, nên một file HTML khai là ảnh phải bị chặn ở đây.
    /// </summary>
    public static string? DetectImageType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (data.Length >= 12 && data.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = data.Slice(8, 4);
            if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("hevc"u8))
            {
                return "image/heic";
            }

            if (brand.SequenceEqual("heif"u8) || brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8))
            {
                return "image/heif";
            }
        }

        return null;
    }
}
