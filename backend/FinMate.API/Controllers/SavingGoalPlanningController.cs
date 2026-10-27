using System.Security.Claims;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Xem trước tính khả thi của một mục tiêu CHƯA tạo — cùng công thức với tiến độ mục tiêu đã có.</summary>
[ApiController]
[Route("api/v1/saving-goals")]
[Authorize]
public class SavingGoalPlanningController : ControllerBase
{
    private readonly IGoalPlanningService _planning;

    public SavingGoalPlanningController(IGoalPlanningService planning)
    {
        _planning = planning;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Chỉ là cảnh báo kèm gợi ý (kéo dài hạn hoặc giảm số tiền), KHÔNG từ chối: người dùng vẫn được đặt mục tiêu tham vọng.
    /// <c>data</c> là null khi không còn gì phải góp.
    /// </summary>
    [HttpPost("feasibility")]
    public async Task<IActionResult> PreviewFeasibility([FromBody] FeasibilityRequest request, CancellationToken ct)
        => Ok(ApiResponse<GoalFeasibilityDto?>.Ok(
            await _planning.PreviewFeasibilityAsync(CurrentUserId, request.TargetCents, request.SavedCents ?? 0, request.Deadline, ct)));
}

public record FeasibilityRequest(long TargetCents, long? SavedCents, DateTimeOffset Deadline);
