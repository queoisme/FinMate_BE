using System.Security.Claims;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.FinanceMethods;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Cách quản lý tài chính CỦA TÔI: đang dùng cách nào, chỉnh tham số (gồm ngưỡng cảnh báo T1/T2), gợi ý mục tiêu và tiền khả dụng.</summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MyFinanceController : ControllerBase
{
    private readonly IUserMethodService _userMethods;
    private readonly IGoalPlanningService _planning;

    public MyFinanceController(IUserMethodService userMethods, IGoalPlanningService planning)
    {
        _userMethods = userMethods;
        _planning = planning;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("methods")]
    public async Task<IActionResult> GetMethods(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<UserMethodDto>>.Ok(await _userMethods.GetMineAsync(CurrentUserId, ct)));

    /// <summary>Chọn một cách làm cách chính hoặc bổ trợ, thay cách đang giữ vai trò đó.</summary>
    [HttpPost("methods")]
    public async Task<IActionResult> Choose([FromBody] ChooseMethodRequest request, CancellationToken ct)
    {
        var chosen = await _userMethods.ChooseAsync(
            CurrentUserId, request.MethodCode, request.Role ?? UserMethodRole.Primary, request.RecommendationId, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<UserMethodDto>.Ok(chosen));
    }

    /// <summary>Gộp thay đổi vào phần ghi đè tham số. Giá trị null xóa ghi đè (về mặc định của phương pháp).</summary>
    [HttpPatch("methods/{id:guid}/params")]
    public async Task<IActionResult> UpdateParams(Guid id, [FromBody] UpdateMethodParamsRequest request, CancellationToken ct)
        => Ok(ApiResponse<UserMethodDto>.Ok(
            await _userMethods.UpdateParamsAsync(CurrentUserId, id, request.Params ?? new Dictionary<string, long?>(), ct)));

    /// <summary>Ngừng dùng cách này. Dữ liệu giao dịch, ngân sách, mục tiêu không bị ảnh hưởng.</summary>
    [HttpDelete("methods/{id:guid}")]
    public async Task<IActionResult> End(Guid id, CancellationToken ct)
    {
        await _userMethods.EndAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>Mẫu mục tiêu gắn với cách đang dùng, đã bỏ những mẫu không hợp (vd đang thiếu hụt dòng tiền thì không gợi ý tiết kiệm).</summary>
    [HttpGet("goal-suggestions")]
    public async Task<IActionResult> GetGoalSuggestions(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GoalSuggestionDto>>.Ok(await _planning.GetSuggestionsAsync(CurrentUserId, ct)));

    /// <summary>Tiền khả dụng của tháng này, tính một chỗ theo cách mục tiêu đi vào dòng tiền của cách chính.</summary>
    [HttpGet("spendable")]
    public async Task<IActionResult> GetSpendable(CancellationToken ct)
        => Ok(ApiResponse<SpendableDto>.Ok(await _planning.GetSpendableAsync(CurrentUserId, ct)));
}

/// <param name="Role"><c>primary</c> (mặc định) hoặc <c>supporting</c>.</param>
/// <param name="RecommendationId">Gợi ý đang trả lời — để ghi nhận người dùng nhận hay đổi.</param>
public record ChooseMethodRequest(string MethodCode, UserMethodRole? Role, Guid? RecommendationId);

public record UpdateMethodParamsRequest(Dictionary<string, long?>? Params);
