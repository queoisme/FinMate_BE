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
public class AgentController(IAgentOrchestrator orchestrator, AgentMemoryService memory) : ControllerBase
{
    private const int MaxQuestionLength = 500;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public sealed record AskRequest(string Question);

    public sealed record PutMemoryRequest(string Value);

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
