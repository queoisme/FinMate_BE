using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Agent;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>
/// CHỈ để bộ đánh giá <c>ai-service/agent_eval</c> chạy agent trên hồ sơ giả lập. Tồn tại khi môi trường là Development VÀ <c>AGENT_EVAL_ENABLED=true</c>, còn lại trả 404 (như chưa có route).
/// Khóa nội bộ là <c>AI_SERVICE_API_KEY</c> trong header <c>X-Eval-Key</c>. Mỗi lượt dùng một <c>userId</c> ngẫu nhiên nên không dính ngữ cảnh/bộ nhớ của lượt khác, và dữ liệu thật không bị đụng tới.
/// </summary>
[ApiController]
[Route("api/v1/internal/agent")]
[AllowAnonymous]
public class AgentEvalController(IConfiguration configuration, IWebHostEnvironment environment, IAgentOrchestrator orchestrator, SwitchableFinancialDataSource data, AgentRunOptions options, AgentTraceContext trace) : ControllerBase
{
    public sealed record CategorySpendDto(string Slug, string Name, long SpentVnd);

    public sealed record SnapshotDto(List<long> MonthlyIncome, List<long> MonthlySpend, long TotalBalance, List<CategorySpendDto>? CategorySpend = null);

    public sealed record FlagsDto(bool Verifier = true, bool Rag = true);

    public sealed record RunRequest(SnapshotDto Snapshot, string Question, FlagsDto? Flags);

    public sealed record StepDto(string Tool, string Status);

    public sealed record UsageDto(int InputTokens, int OutputTokens);

    public sealed record RunResponse(AgentAnswerDto Answer, IReadOnlyList<StepDto> Trace, UsageDto Usage);

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] RunRequest request, CancellationToken ct)
    {
        var key = configuration["AI_SERVICE_API_KEY"] ?? "";
        var enabled = environment.IsDevelopment() && string.Equals(configuration["AGENT_EVAL_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled)
        {
            return NotFound();
        }

        var given = Request.Headers["X-Eval-Key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(key)))
        {
            return Unauthorized();
        }

        var s = request.Snapshot;
        if (s.MonthlyIncome.Count != s.MonthlySpend.Count || string.IsNullOrWhiteSpace(request.Question))
        {
            return UnprocessableEntity(new { code = "invalid_eval_request" });
        }

        data.Use(new InMemoryFinancialDataSource(
            s.MonthlyIncome, s.MonthlySpend, s.TotalBalance,
            categorySpend: s.CategorySpend?.Select(c => new CategorySpendVnd(c.Slug, c.Name, c.SpentVnd)).ToList()));
        options.SkipVerifier = !(request.Flags?.Verifier ?? true);
        options.RagEnabled = request.Flags?.Rag ?? true;

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), request.Question.Trim(), ct);
        return Ok(ApiResponse<RunResponse>.Ok(new RunResponse(
            answer,
            trace.Steps.Select(x => new StepDto(x.Tool, x.Status)).ToList(),
            new UsageDto(trace.InputTokens, trace.OutputTokens))));
    }
}
