using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Forecasting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Người dùng báo "dự báo sai". Người dùng lấy từ JWT; phản hồi gắn với bản chụp dự báo gần nhất của chính họ.</summary>
[ApiController]
[Route("api/v1/forecast/feedback")]
[Authorize]
public class ForecastFeedbackController(IForecastFeedbackService feedback) : ControllerBase
{
    public sealed record SubmitRequest(string Reason, string? Note);

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// <c>reason</c>: <c>balance_wrong</c>, <c>missing_bill</c>, <c>wrong_bill</c>, <c>unusual_spending</c>, <c>other</c>; <c>note</c> tùy chọn, tối đa 300 ký tự.
    /// Lỗi 422 <c>FORECAST_FEEDBACK_INVALID</c> khi sai lý do hoặc lời nhắn quá dài. Gửi lại khi phản hồi trước chưa được xử lý thì trả lại phản hồi đó.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitRequest request, CancellationToken ct)
        => StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<SubmittedForecastFeedbackDto>.Ok(await feedback.SubmitAsync(new SubmitForecastFeedbackCommand(CurrentUserId, request.Reason, request.Note), ct)));
}
