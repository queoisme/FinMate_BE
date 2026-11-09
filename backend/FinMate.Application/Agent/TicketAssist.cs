using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Support;

namespace FinMate.Application.Agent;

/// <param name="Role"><c>user</c> hoặc <c>staff</c>.</param>
public sealed record TicketAssistMessage(string Role, string Text);

public sealed record TicketAssistResult(string Summary, string? SuggestedReply, bool Verified, AgentUsage? Usage);

/// <param name="Available">false = trợ lý không dùng được lúc này (LLM sập): nhân sự vẫn xử lý ticket bình thường.</param>
/// <param name="Verified">false = câu trả lời gợi ý bị bỏ vì chứa số tiền không có trong ticket; <c>Summary</c> khi đó chỉ là tiêu đề.</param>
public sealed record TicketAssistDto(bool Available, string? Summary, string? SuggestedReply, bool Verified);

/// <summary>
/// Trợ lý gợi ý cho nhân sự vận hành khi xử lý ticket (Phase 62): tóm tắt vấn đề và nháp một câu trả lời. CHỈ GỢI Ý: không đổi ticket, không gửi gì cho khách; câu trả lời chỉ ra ngoài khi nhân sự tự gửi qua
/// <c>POST /ops/tickets/{id}/messages</c>. Quyền xem ticket và ghi nhận truy cập do <see cref="IOpsTicketService.GetAsync"/> quyết định (đúng như khi nhân sự mở ticket). Ghi chú nội bộ KHÔNG bao giờ gửi cho mô hình;
/// ai-service không nhận mã người dùng, chỉ tiêu đề + nội dung + các tin nhắn công khai (đã cắt theo giới hạn của hợp đồng).
/// </summary>
public sealed class TicketAssistService(IOpsTicketService tickets, IAgentAiClient ai)
{
    private const int MaxSubject = 120;
    private const int MaxText = 2000;
    private const int MaxHistory = 10;

    public async Task<TicketAssistDto> AssistAsync(Guid actorId, Guid ticketId, CancellationToken ct = default)
    {
        var detail = await tickets.GetAsync(actorId, ticketId, ct);
        var messages = detail.Messages.Where(m => m.Kind != "InternalNote" && !string.IsNullOrWhiteSpace(m.Body)).OrderBy(m => m.CreatedAt).ToList();
        var subject = Clip(detail.Ticket.Subject, MaxSubject);
        var firstCustomer = messages.FirstOrDefault(m => m.Kind == "Customer");
        var body = Clip(firstCustomer?.Body ?? subject, MaxText);
        var history = messages
            .Where(m => m != firstCustomer)
            .TakeLast(MaxHistory)
            .Select(m => new TicketAssistMessage(m.Kind == "Customer" ? "user" : "staff", Clip(m.Body, MaxText)))
            .ToList();

        try
        {
            var result = await ai.TicketAssistAsync(subject, body, history, ct);
            return result is null ? new TicketAssistDto(false, null, null, false) : new TicketAssistDto(true, result.Summary, result.SuggestedReply, result.Verified);
        }
        catch (AIServiceUnavailableException)
        {
            return new TicketAssistDto(false, null, null, false);
        }
    }

    private static string Clip(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max];
    }
}
