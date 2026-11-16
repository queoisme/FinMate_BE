using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/assistant")]
[Authorize]
public class AssistantController : ControllerBase
{
    private readonly IAssistantSuggestionService _suggestions;

    public AssistantController(IAssistantSuggestionService suggestions) => _suggestions = suggestions;

    /// <summary>Câu gợi ý cho khung chat, theo ngữ cảnh của người dùng. Có thể rỗng.</summary>
    [HttpGet("suggestions")]
    public async Task<IActionResult> Suggestions(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<SuggestionDto>>.Ok(
            await _suggestions.ListAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), ct)));
}
