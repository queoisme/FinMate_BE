using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.SavingGoals.Commands;
using FinMate.Application.SavingGoals.Queries;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/saving-goals")]
[Authorize]
public class SavingGoalsController : ControllerBase
{
    private readonly ICreateSavingGoalCommandHandler _createHandler;
    private readonly IUpdateSavingGoalCommandHandler _updateHandler;
    private readonly IContributeToGoalCommandHandler _contributeHandler;
    private readonly ICancelSavingGoalCommandHandler _cancelHandler;
    private readonly IGetSavingGoalListQueryHandler _listHandler;
    private readonly IGetGoalProgressQueryHandler _progressHandler;

    public SavingGoalsController(
        ICreateSavingGoalCommandHandler createHandler,
        IUpdateSavingGoalCommandHandler updateHandler,
        IContributeToGoalCommandHandler contributeHandler,
        ICancelSavingGoalCommandHandler cancelHandler,
        IGetSavingGoalListQueryHandler listHandler,
        IGetGoalProgressQueryHandler progressHandler)
    {
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _contributeHandler = contributeHandler;
        _cancelHandler = cancelHandler;
        _listHandler = listHandler;
        _progressHandler = progressHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] SavingGoalStatus? status, CancellationToken ct)
    {
        var goals = await _listHandler.HandleAsync(new GetSavingGoalListQuery(CurrentUserId, status), ct);
        return Ok(ApiResponse<IReadOnlyList<SavingGoalDto>>.Ok(goals));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSavingGoalRequest request, CancellationToken ct)
    {
        var goal = await _createHandler.HandleAsync(
            new CreateSavingGoalCommand(
                CurrentUserId, request.Name, request.TargetCents, request.Deadline, request.GoalTemplateCode,
                request.Icon, request.InitialSavedCents ?? 0, request.ContributionFrequency, request.IsDraft ?? false), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<SavingGoalDto>.Ok(goal));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSavingGoalRequest request, CancellationToken ct)
    {
        var goal = await _updateHandler.HandleAsync(
            new UpdateSavingGoalCommand(CurrentUserId, id, request.Name, request.TargetCents, request.Deadline, request.Icon, request.ContributionFrequency), ct);
        return Ok(ApiResponse<SavingGoalDto>.Ok(goal));
    }

    [HttpGet("{id:guid}/progress")]
    public async Task<IActionResult> GetProgress(Guid id, CancellationToken ct)
    {
        var progress = await _progressHandler.HandleAsync(new GetGoalProgressQuery(CurrentUserId, id), ct);
        return Ok(ApiResponse<GoalProgressDto>.Ok(progress));
    }

    [HttpPost("{id:guid}/contribute")]
    public async Task<IActionResult> Contribute(Guid id, [FromBody] ContributeToGoalRequest request, CancellationToken ct)
    {
        var goal = await _contributeHandler.HandleAsync(
            new ContributeToGoalCommand(CurrentUserId, id, request.AmountCents, request.Note, request.ContributedAt), ct);
        return Ok(ApiResponse<SavingGoalDto>.Ok(goal));
    }

    /// <summary>Bản nháp thành mục tiêu đang hoạt động. Hạn chót (nếu có) phải còn ở tương lai.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, [FromServices] IActivateSavingGoalCommandHandler handler, CancellationToken ct)
        => Ok(ApiResponse<SavingGoalDto>.Ok(await handler.HandleAsync(CurrentUserId, id, ct)));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var goal = await _cancelHandler.HandleAsync(new CancelSavingGoalCommand(CurrentUserId, id), ct);
        return Ok(ApiResponse<SavingGoalDto>.Ok(goal));
    }
}

/// <param name="GoalTemplateCode">Mã mẫu từ <c>GET /me/goal-suggestions</c> nếu mục tiêu được tạo từ gợi ý; bỏ trống khi tự đặt.</param>
/// <param name="Icon">Tên biểu tượng app chọn (tối đa 40 ký tự).</param>
/// <param name="InitialSavedCents">Số đã có sẵn lúc đặt mục tiêu, từ 0 và nhỏ hơn <c>TargetCents</c>. Không phải khoản góp.</param>
/// <param name="ContributionFrequency"><c>weekly</c> | <c>monthly</c> | <c>custom</c>.</param>
/// <param name="IsDraft">true = lưu bản nháp (trạng thái <c>draft</c>), kích hoạt sau bằng <c>POST /saving-goals/{id}/activate</c>.</param>
public record CreateSavingGoalRequest(
    string Name, long TargetCents, DateTimeOffset? Deadline, string? GoalTemplateCode = null,
    string? Icon = null, long? InitialSavedCents = null, GoalContributionFrequency? ContributionFrequency = null, bool? IsDraft = null);

/// <param name="Icon">null = giữ nguyên; chuỗi rỗng = xóa.</param>
public record UpdateSavingGoalRequest(
    string Name, long TargetCents, DateTimeOffset? Deadline, string? Icon = null, GoalContributionFrequency? ContributionFrequency = null);
public record ContributeToGoalRequest(long AmountCents, string? Note, DateTimeOffset? ContributedAt);
