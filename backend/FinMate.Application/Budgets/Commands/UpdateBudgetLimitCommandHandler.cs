using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.Application.Budgets.Commands;

public class UpdateBudgetLimitCommandHandler : IUpdateBudgetLimitCommandHandler
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly ICacheService _cache;
    private readonly IValidator<UpdateBudgetLimitCommand> _validator;

    public UpdateBudgetLimitCommandHandler(
        IBudgetRepository budgetRepository,
        ICacheService cache,
        IValidator<UpdateBudgetLimitCommand> validator)
    {
        _budgetRepository = budgetRepository;
        _cache = cache;
        _validator = validator;
    }

    public async Task<BudgetDto> HandleAsync(UpdateBudgetLimitCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var budget = await _budgetRepository.GetByIdAsync(command.BudgetId, command.UserId, ct)
            ?? throw new NotFoundException("Budget", command.BudgetId);

        var now = DateTimeOffset.UtcNow;
        var (periodStart, _) = VietnamTime.MonthRange(now);

        budget.LimitCents = command.LimitCents;
        budget.UpdatedAt = now;

        // Chỉ period đang chạy nhận limit mới — period quá khứ giữ snapshot để lịch sử không đổi.
        var currentPeriod = await _budgetRepository.GetPeriodAsync(budget.Id, periodStart, ct);
        if (currentPeriod is not null)
        {
            currentPeriod.LimitCents = command.LimitCents;
            currentPeriod.UpdatedAt = now;

            // Nâng hạn mức đủ để không còn vượt ngưỡng → cho phép alert bắn lại nếu sau đó
            // user tiêu chạm ngưỡng lần nữa trong cùng chu kỳ.
            if (currentPeriod.SpentCents * 100 < command.LimitCents * 80)
            {
                currentPeriod.Alert80SentAt = null;
            }

            if (currentPeriod.SpentCents < command.LimitCents)
            {
                currentPeriod.Alert100SentAt = null;
            }
        }

        await _budgetRepository.UpdateAsync(budget, ct);

        var (year, month) = VietnamTime.YearMonthOf(now);
        await _cache.RemoveAsync(CacheKeys.BudgetSummary(command.UserId, year, month), ct);

        return BudgetMapper.ToDto(budget);
    }
}
