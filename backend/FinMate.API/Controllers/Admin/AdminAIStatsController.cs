using FinMate.Application.Admin.Queries;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

[Route("api/v1/admin/ai-stats")]
public class AdminAIStatsController : AdminControllerBase
{
    private readonly IGetAiStatsQueryHandler _statsHandler;

    public AdminAIStatsController(IGetAiStatsQueryHandler statsHandler)
    {
        _statsHandler = statsHandler;
    }

    /// <summary>
    /// Gộp hai nguồn: backend DB trả lời "AI đang chạy tốt đến đâu trong sản xuất" (gồm tỉ lệ
    /// người dùng sửa lại danh mục — con số mà AI Service không thể tự biết), còn AI Service
    /// trả lời "model và dataset đang ở đâu". Nửa thứ hai hỏng thì response vẫn 200 với
    /// <c>aiService: null</c>.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStats(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var stats = await _statsHandler.HandleAsync(new GetAiStatsQuery(from, to), ct);
        return Ok(ApiResponse<AiStatsDto>.Ok(stats));
    }
}
