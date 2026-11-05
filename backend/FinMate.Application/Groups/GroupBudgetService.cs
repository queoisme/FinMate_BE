using FinMate.Application.Budgets;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Groups;

public sealed record GroupBudgetDto(
    Guid Id,
    Guid? CategoryId,
    Guid? GroupCategoryId,
    string? CategoryName,
    long LimitCents,
    long SpentCents,
    long RemainingCents,
    int PercentUsed,
    bool IsOverLimit);

/// <param name="CategoryId">Danh mục hệ thống. Bỏ trống cả hai là ngân sách tổng của nhóm.</param>
public sealed record CreateGroupBudgetCommand(Guid UserId, Guid GroupId, Guid? CategoryId, Guid? GroupCategoryId, long LimitCents);

public sealed record UpdateGroupBudgetCommand(Guid UserId, Guid GroupId, Guid BudgetId, long LimitCents);

public class CreateGroupBudgetCommandValidator : AbstractValidator<CreateGroupBudgetCommand>
{
    public CreateGroupBudgetCommandValidator()
        => RuleFor(x => x.LimitCents).GreaterThan(0).WithMessage("Hạn mức phải lớn hơn 0.").LessThanOrEqualTo(GroupFinanceLimits.MaxAmountCents).WithMessage("Hạn mức không hợp lệ.");
}

public class UpdateGroupBudgetCommandValidator : AbstractValidator<UpdateGroupBudgetCommand>
{
    public UpdateGroupBudgetCommandValidator()
        => RuleFor(x => x.LimitCents).GreaterThan(0).WithMessage("Hạn mức phải lớn hơn 0.").LessThanOrEqualTo(GroupFinanceLimits.MaxAmountCents).WithMessage("Hạn mức không hợp lệ.");
}

public static class GroupFinanceLimits
{
    public const long MaxAmountCents = 100_000_000_000L;
}

public interface IGroupBudgetService
{
    Task<IReadOnlyList<GroupBudgetDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupBudgetDto> CreateAsync(CreateGroupBudgetCommand command, CancellationToken ct = default);
    Task<GroupBudgetDto> UpdateLimitAsync(UpdateGroupBudgetCommand command, CancellationToken ct = default);
    Task DeleteAsync(Guid groupId, Guid userId, Guid budgetId, CancellationToken ct = default);
}

public class GroupBudgetService : IGroupBudgetService
{
    private readonly IGroupFinanceRepository _finance;
    private readonly IGroupRepository _groups;
    private readonly IBudgetRepository _budgets;
    private readonly ICategoryRepository _categories;
    private readonly IGroupAccessService _access;
    private readonly ISystemConfigProvider _config;
    private readonly IValidator<CreateGroupBudgetCommand> _createValidator;
    private readonly IValidator<UpdateGroupBudgetCommand> _updateValidator;

    public GroupBudgetService(
        IGroupFinanceRepository finance,
        IGroupRepository groups,
        IBudgetRepository budgets,
        ICategoryRepository categories,
        IGroupAccessService access,
        ISystemConfigProvider config,
        IValidator<CreateGroupBudgetCommand> createValidator,
        IValidator<UpdateGroupBudgetCommand> updateValidator)
    {
        _finance = finance;
        _groups = groups;
        _budgets = budgets;
        _categories = categories;
        _access = access;
        _config = config;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<IReadOnlyList<GroupBudgetDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        var (periodStart, _) = VietnamTime.MonthRange(DateTimeOffset.UtcNow);
        var budgets = await _finance.ListBudgetsAsync(groupId, ct);
        var periods = (await _finance.ListPeriodsAsync(groupId, periodStart, ct)).ToDictionary(p => p.BudgetId);
        return budgets.Select(b => ToDto(b, periods.GetValueOrDefault(b.Id))).ToList();
    }

    public async Task<GroupBudgetDto> CreateAsync(CreateGroupBudgetCommand command, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(command, ct);
        await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.ManageBudgets, ct);
        var (category, groupCategory) = await ResolveScopeAsync(command, ct);
        if (await _finance.BudgetExistsAsync(command.GroupId, command.CategoryId, command.GroupCategoryId, ct))
        {
            throw new ConflictException(GroupErrorCodes.BudgetAlreadyExists, "Nhóm đã có hạn mức cho phạm vi này.");
        }

        var now = DateTimeOffset.UtcNow;
        var (periodStart, periodEnd) = VietnamTime.MonthRange(now);
        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            GroupId = command.GroupId,
            CategoryId = command.CategoryId,
            GroupCategoryId = command.GroupCategoryId,
            CreatedByUserId = command.UserId,
            LimitCents = command.LimitCents,
            PeriodType = BudgetPeriodType.Monthly,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var period = new BudgetPeriod
        {
            Id = Guid.NewGuid(),
            BudgetId = budget.Id,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            LimitCents = command.LimitCents,

            // Ngân sách tạo giữa tháng vẫn phải phản ánh phần đã chi từ đầu kỳ.
            SpentCents = await _finance.SumSharedSpendAsync(command.GroupId, command.CategoryId, command.GroupCategoryId, periodStart, periodEnd, ct),
            CreatedAt = now,
            UpdatedAt = now,
        };

        _finance.AddBudget(budget);
        _budgets.AddPeriod(period);
        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.BudgetChanged, budget.Id, now));
        await _groups.SaveChangesAsync(ct);

        budget.Category = category;
        budget.GroupCategory = groupCategory;
        return ToDto(budget, period);
    }

    public async Task<GroupBudgetDto> UpdateLimitAsync(UpdateGroupBudgetCommand command, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(command, ct);
        await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.ManageBudgets, ct);
        var budget = await _finance.GetBudgetAsync(command.GroupId, command.BudgetId, ct) ?? throw new NotFoundException("Budget", command.BudgetId);

        var now = DateTimeOffset.UtcNow;
        var (periodStart, _) = VietnamTime.MonthRange(now);
        budget.LimitCents = command.LimitCents;
        budget.UpdatedAt = now;

        // Chỉ kỳ đang chạy nhận hạn mức mới: kỳ cũ giữ nguyên để lịch sử không đổi.
        var period = await _budgets.GetPeriodAsync(budget.Id, periodStart, ct);
        if (period is not null)
        {
            period.LimitCents = command.LimitCents;
            period.UpdatedAt = now;
            var limits = (await _config.GetAsync(ct)).Document.Limits;
            BudgetAlertEvaluator.ReopenBelow(period, new AlertThresholds(limits.AlertT1Percent, limits.AlertT2Percent));
        }

        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.BudgetChanged, budget.Id, now));
        await _groups.SaveChangesAsync(ct);
        return ToDto(budget, period);
    }

    public async Task DeleteAsync(Guid groupId, Guid userId, Guid budgetId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ManageBudgets, ct);
        var budget = await _finance.GetBudgetAsync(groupId, budgetId, ct) ?? throw new NotFoundException("Budget", budgetId);
        var now = DateTimeOffset.UtcNow;
        budget.DeletedAt = now;
        budget.UpdatedAt = now;
        _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.BudgetChanged, budgetId, now));
        await _groups.SaveChangesAsync(ct);
    }

    private async Task<(Category? Category, GroupCategory? GroupCategory)> ResolveScopeAsync(CreateGroupBudgetCommand command, CancellationToken ct)
    {
        if (command.CategoryId is not null && command.GroupCategoryId is not null)
        {
            throw new BusinessRuleException(GroupErrorCodes.BudgetInvalidCategory, "Chỉ chọn một trong danh mục hệ thống và danh mục nhóm.");
        }

        if (command.CategoryId is { } categoryId)
        {
            var category = await _categories.GetByIdAsync(categoryId, ct);
            return category is { IsSystem: true }
                ? (category, null)
                : throw new BusinessRuleException(GroupErrorCodes.BudgetInvalidCategory, "Ngân sách nhóm chỉ dùng danh mục hệ thống hoặc danh mục nhóm.");
        }

        if (command.GroupCategoryId is { } groupCategoryId)
        {
            return (null, await _finance.GetCategoryAsync(command.GroupId, groupCategoryId, ct) ?? throw new NotFoundException("GroupCategory", groupCategoryId));
        }

        return (null, null);
    }

    private static GroupBudgetDto ToDto(Budget budget, BudgetPeriod? period)
    {
        var spent = period?.SpentCents ?? 0;
        var limit = period?.LimitCents ?? budget.LimitCents;
        return new GroupBudgetDto(
            budget.Id,
            budget.CategoryId,
            budget.GroupCategoryId,
            budget.Category?.Name ?? budget.GroupCategory?.Name,
            limit,
            spent,
            Math.Max(0, limit - spent),
            BudgetMapper.PercentUsed(spent, limit),
            spent > limit);
    }
}
