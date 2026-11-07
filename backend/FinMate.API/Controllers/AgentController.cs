using System.Security.Claims;
using FinMate.API.Middleware;
using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinMate.API.Controllers;

/// <summary>
/// AI Financial Agent: hỏi một câu về tài chính của CHÍNH người dùng (ví dụ "Mua xe 600 triệu sau 2 năm được không?"). <c>userId</c> chỉ lấy từ JWT. Mọi con số tiền trong câu trả lời đều
/// có bằng chứng kèm nguồn và độ tin cậy; không có dữ liệu thì trả <c>insufficient_data</c> chứ không đoán. Mỗi lượt tốn token LLM nên có hạn mức riêng theo người dùng.
/// </summary>
[ApiController]
[Route("api/v1/agent")]
[Authorize]
public class AgentController(IAgentOrchestrator orchestrator, AgentMemoryService memory, RecurringItemService recurring) : ControllerBase
{
    private const int MaxQuestionLength = 500;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public sealed record AskRequest(string Question);

    public sealed record PutMemoryRequest(string Value);

    public sealed record CreateRecurringRequest(string Name, long AmountVnd, string Kind, int DayOfMonth);

    /// <summary>Khoản thu/chi định kỳ ĐÃ XÁC NHẬN của chính người dùng, kèm <c>suggestions</c> là các khoản hệ thống thấy lặp lại trong lịch sử nhưng CHƯA lưu (chỉ là đề xuất; xác nhận bằng POST).</summary>
    [HttpGet("recurring")]
    public async Task<IActionResult> ListRecurring(CancellationToken ct)
        => Ok(ApiResponse<RecurringListDto>.Ok(await recurring.ListAsync(CurrentUserId, DateTimeOffset.UtcNow, ct)));

    /// <summary>Xác nhận một khoản định kỳ. <c>kind</c>: <c>obligation</c> (phải trả) hoặc <c>inflow</c> (thu). 422: <c>invalid_recurring_name</c>, <c>invalid_recurring_amount</c>, <c>invalid_recurring_kind</c>, <c>invalid_recurring_day</c>, <c>recurring_limit_reached</c>.</summary>
    [HttpPost("recurring")]
    public async Task<IActionResult> CreateRecurring([FromBody] CreateRecurringRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<RecurringItemDto>.Ok(await recurring.CreateAsync(CurrentUserId, request.Name, request.AmountVnd, request.Kind, request.DayOfMonth, ct)));

    /// <summary>Bỏ một khoản định kỳ của chính người dùng. 404 nếu không có (kể cả khi id thuộc người khác).</summary>
    [HttpDelete("recurring/{id:guid}")]
    public async Task<IActionResult> DeleteRecurring(Guid id, CancellationToken ct)
    {
        await recurring.DeleteAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>Sở thích dài hạn của CHÍNH người dùng (do họ đặt, xem/sửa/xóa được). Khóa cho phép: <c>preferred_method</c>, <c>eating_out_weekends</c>, <c>notification_quiet_hours</c>, <c>communication_tone</c>.</summary>
    [HttpGet("memory")]
    public async Task<IActionResult> ListMemory(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<AgentMemoryEntry>>.Ok(await memory.ListAsync(CurrentUserId, ct)));

    /// <summary>Đặt (hoặc thay) một sở thích. 422 <c>invalid_memory_key</c> nếu khóa không có trong danh sách; 422 <c>invalid_memory_value</c> nếu rỗng hoặc quá 200 ký tự.</summary>
    [HttpPut("memory/{key}")]
    public async Task<IActionResult> PutMemory(string key, [FromBody] PutMemoryRequest request, CancellationToken ct)
    {
        await memory.PutAsync(CurrentUserId, key, request.Value, ct);
        return NoContent();
    }

    [HttpDelete("memory/{key}")]
    public async Task<IActionResult> DeleteMemoryKey(string key, CancellationToken ct)
        => Ok(ApiResponse<int>.Ok(await memory.DeleteAsync(CurrentUserId, key, ct)));

    /// <summary>Xóa toàn bộ bộ nhớ dài hạn và ngữ cảnh hội thoại của người dùng; agent không dùng chúng nữa. Trả số mục đã xóa.</summary>
    [HttpDelete("memory")]
    public async Task<IActionResult> DeleteAllMemory(CancellationToken ct)
        => Ok(ApiResponse<int>.Ok(await memory.DeleteAsync(CurrentUserId, null, ct)));

    /// <summary>
    /// Trả <c>status</c>: <c>answered</c> (kèm <c>claims</c> + <c>evidence</c>), <c>clarify</c> (hỏi lại <c>message</c>), <c>refused</c> (ngoài phạm vi / đòi tư vấn đầu tư), <c>insufficient_data</c>.
    /// 422 <c>invalid_question</c> nếu rỗng hoặc quá 500 ký tự; 503 khi AI Service không dùng được trước khi có dữ liệu để trả lời; 429 khi vượt hạn mức.
    /// </summary>
    [HttpPost("ask")]
    [EnableRateLimiting(RateLimitingMiddleware.AgentPolicy)]
    public async Task<IActionResult> Ask([FromBody] AskRequest request, CancellationToken ct)
    {
        var question = request.Question?.Trim();
        if (string.IsNullOrEmpty(question) || question.Length > MaxQuestionLength)
        {
            throw new BusinessRuleException("invalid_question", $"Câu hỏi phải có 1–{MaxQuestionLength} ký tự.");
        }

        return Ok(ApiResponse<AgentAnswerDto>.Ok(await orchestrator.AskAsync(CurrentUserId, question, ct)));
    }
}
