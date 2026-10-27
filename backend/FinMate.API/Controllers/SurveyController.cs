using System.Security.Claims;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.FinanceMethods;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Khảo sát đầu vào: xem câu hỏi và câu trả lời của chính mình, gửi, bỏ qua, xóa.</summary>
[ApiController]
[Route("api/v1/survey")]
[Authorize]
public class SurveyController : ControllerBase
{
    private readonly ISurveyService _survey;

    public SurveyController(ISurveyService survey)
    {
        _survey = survey;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<SurveyDto>.Ok(await _survey.GetAsync(CurrentUserId, ct)));

    /// <summary>Thay toàn bộ câu trả lời và đánh dấu đã làm xong khảo sát.</summary>
    [HttpPut("answers")]
    public async Task<IActionResult> Submit([FromBody] SubmitSurveyRequest request, CancellationToken ct)
        => Ok(ApiResponse<SurveyDto>.Ok(await _survey.SubmitAsync(CurrentUserId, request.Answers ?? [], ct)));

    [HttpPost("skip")]
    public async Task<IActionResult> Skip(CancellationToken ct)
        => Ok(ApiResponse<SurveyDto>.Ok(await _survey.SkipAsync(CurrentUserId, ct)));

    /// <summary>Xóa câu trả lời và các gợi ý suy ra từ chúng; khảo sát về lại chưa làm.</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
        => Ok(ApiResponse<SurveyDto>.Ok(await _survey.DeleteAsync(CurrentUserId, ct)));
}

public record SubmitSurveyRequest(IReadOnlyList<SurveyAnswerDto>? Answers);
