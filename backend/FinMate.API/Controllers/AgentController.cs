using System.Security.Claims;
using FinMate.API.Middleware;
using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
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
public class AgentController(IAgentOrchestrator orchestrator) : ControllerBase
{
    private const int MaxQuestionLength = 500;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public sealed record AskRequest(string Question);

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
