using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups;
using FinMate.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Giao dịch đã chia sẻ, danh mục, quy tắc tự chia sẻ, ngân sách và mục tiêu của một nhóm.</summary>
[ApiController]
[Route("api/v1/groups/{groupId:guid}")]
[Authorize]
public class GroupFinanceController : ControllerBase
{
    private const int DefaultTransactionLimit = 100;
    private const int DefaultContributionLimit = 50;

    private readonly IGroupTransactionService _transactions;
    private readonly IGroupCategoryService _categories;
    private readonly IGroupBudgetService _budgets;
    private readonly IGroupGoalService _goals;

    public GroupFinanceController(IGroupTransactionService transactions, IGroupCategoryService categories, IGroupBudgetService budgets, IGroupGoalService goals)
    {
        _transactions = transactions;
        _categories = categories;
        _budgets = budgets;
        _goals = goals;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Giao dịch đã chia sẻ trong một tháng (giờ Việt Nam), mặc định tháng này. Không có quyền xem chi tiết thì chỉ thấy khoản của chính mình.</summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions(Guid groupId, [FromQuery] int? year, [FromQuery] int? month, [FromQuery] int limit = DefaultTransactionLimit, CancellationToken ct = default)
    {
        var (currentYear, currentMonth) = VietnamTime.YearMonthOf(DateTimeOffset.UtcNow);
        return Ok(ApiResponse<IReadOnlyList<GroupTransactionDto>>.Ok(
            await _transactions.ListAsync(groupId, CurrentUserId, year ?? currentYear, month ?? currentMonth, limit, ct)));
    }

    [HttpPatch("transactions/{transactionId:guid}")]
    public async Task<IActionResult> Reclassify(Guid groupId, Guid transactionId, [FromBody] ReclassifyGroupTransactionRequest request, CancellationToken ct)
    {
        await _transactions.ReclassifyAsync(groupId, CurrentUserId, transactionId, request.GroupCategoryId, ct);
        return NoContent();
    }

    [HttpDelete("transactions/{transactionId:guid}")]
    public async Task<IActionResult> RemoveTransaction(Guid groupId, Guid transactionId, CancellationToken ct)
    {
        await _transactions.RemoveAsync(groupId, CurrentUserId, transactionId, ct);
        return NoContent();
    }

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(Guid groupId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GroupCategoryDto>>.Ok(await _categories.ListAsync(groupId, CurrentUserId, ct)));

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory(Guid groupId, [FromBody] CreateGroupCategoryRequest request, CancellationToken ct)
    {
        var created = await _categories.CreateAsync(new CreateGroupCategoryCommand(CurrentUserId, groupId, request.Name ?? string.Empty, request.IconName), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<GroupCategoryDto>.Ok(created));
    }

    [HttpDelete("categories/{categoryId:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid groupId, Guid categoryId, CancellationToken ct)
    {
        await _categories.DeleteAsync(groupId, CurrentUserId, categoryId, ct);
        return NoContent();
    }

    [HttpGet("share-rules")]
    public async Task<IActionResult> ShareRules(Guid groupId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GroupShareRuleDto>>.Ok(await _categories.ListRulesAsync(groupId, CurrentUserId, ct)));

    [HttpPost("share-rules")]
    public async Task<IActionResult> CreateShareRule(Guid groupId, [FromBody] CreateGroupShareRuleRequest request, CancellationToken ct)
    {
        var created = await _categories.CreateRuleAsync(groupId, CurrentUserId, request.FinancialAccountId, request.CategoryId, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<GroupShareRuleDto>.Ok(created));
    }

    [HttpDelete("share-rules/{ruleId:guid}")]
    public async Task<IActionResult> DeleteShareRule(Guid groupId, Guid ruleId, CancellationToken ct)
    {
        await _categories.DeleteRuleAsync(groupId, CurrentUserId, ruleId, ct);
        return NoContent();
    }

    [HttpGet("budgets")]
    public async Task<IActionResult> Budgets(Guid groupId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GroupBudgetDto>>.Ok(await _budgets.ListAsync(groupId, CurrentUserId, ct)));

    [HttpPost("budgets")]
    public async Task<IActionResult> CreateBudget(Guid groupId, [FromBody] CreateGroupBudgetRequest request, CancellationToken ct)
    {
        var created = await _budgets.CreateAsync(new CreateGroupBudgetCommand(CurrentUserId, groupId, request.CategoryId, request.GroupCategoryId, request.LimitCents), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<GroupBudgetDto>.Ok(created));
    }

    [HttpPatch("budgets/{budgetId:guid}")]
    public async Task<IActionResult> UpdateBudget(Guid groupId, Guid budgetId, [FromBody] UpdateGroupBudgetRequest request, CancellationToken ct)
        => Ok(ApiResponse<GroupBudgetDto>.Ok(await _budgets.UpdateLimitAsync(new UpdateGroupBudgetCommand(CurrentUserId, groupId, budgetId, request.LimitCents), ct)));

    [HttpDelete("budgets/{budgetId:guid}")]
    public async Task<IActionResult> DeleteBudget(Guid groupId, Guid budgetId, CancellationToken ct)
    {
        await _budgets.DeleteAsync(groupId, CurrentUserId, budgetId, ct);
        return NoContent();
    }

    [HttpGet("goals")]
    public async Task<IActionResult> Goals(Guid groupId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<SavingGoalDto>>.Ok(await _goals.ListAsync(groupId, CurrentUserId, ct)));

    [HttpPost("goals")]
    public async Task<IActionResult> CreateGoal(Guid groupId, [FromBody] CreateGroupGoalRequest request, CancellationToken ct)
    {
        var created = await _goals.CreateAsync(new CreateGroupGoalCommand(CurrentUserId, groupId, request.Name ?? string.Empty, request.TargetCents, request.Deadline), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<SavingGoalDto>.Ok(created));
    }

    [HttpPatch("goals/{goalId:guid}")]
    public async Task<IActionResult> UpdateGoal(Guid groupId, Guid goalId, [FromBody] UpdateGroupGoalRequest request, CancellationToken ct)
        => Ok(ApiResponse<SavingGoalDto>.Ok(await _goals.UpdateAsync(new UpdateGroupGoalCommand(CurrentUserId, groupId, goalId, request.Name, request.TargetCents, request.Deadline), ct)));

    [HttpPost("goals/{goalId:guid}/cancel")]
    public async Task<IActionResult> CancelGoal(Guid groupId, Guid goalId, CancellationToken ct)
    {
        await _goals.CancelAsync(groupId, CurrentUserId, goalId, ct);
        return NoContent();
    }

    [HttpPost("goals/{goalId:guid}/contribute")]
    public async Task<IActionResult> Contribute(Guid groupId, Guid goalId, [FromBody] ContributeGroupGoalRequest request, CancellationToken ct)
        => Ok(ApiResponse<SavingGoalDto>.Ok(await _goals.ContributeAsync(new ContributeGroupGoalCommand(CurrentUserId, groupId, goalId, request.AmountCents, request.Note), ct)));

    [HttpGet("goals/{goalId:guid}/contributions")]
    public async Task<IActionResult> Contributions(Guid groupId, Guid goalId, [FromQuery] int limit = DefaultContributionLimit, CancellationToken ct = default)
        => Ok(ApiResponse<IReadOnlyList<GroupContributionDto>>.Ok(await _goals.ListContributionsAsync(groupId, CurrentUserId, goalId, limit, ct)));
}

public record ReclassifyGroupTransactionRequest(Guid? GroupCategoryId);
public record CreateGroupCategoryRequest(string? Name, string? IconName);
public record CreateGroupShareRuleRequest(Guid? FinancialAccountId, Guid? CategoryId);
public record CreateGroupBudgetRequest(Guid? CategoryId, Guid? GroupCategoryId, long LimitCents);
public record UpdateGroupBudgetRequest(long LimitCents);
public record CreateGroupGoalRequest(string? Name, long TargetCents, DateTimeOffset? Deadline);
public record UpdateGroupGoalRequest(string? Name, long? TargetCents, DateTimeOffset? Deadline);
public record ContributeGroupGoalRequest(long AmountCents, string? Note);
