using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Budgets.Commands;

public class DeleteBudgetCommandHandler : IDeleteBudgetCommandHandler
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly ICacheService _cache;

    public DeleteBudgetCommandHandler(IBudgetRepository budgetRepository, ICacheService cache)
    {
        _budgetRepository = budgetRepository;
        _cache = cache;
    }

    public async Task HandleAsync(DeleteBudgetCommand command, CancellationToken ct = default)
    {
        var budget = await _budgetRepository.GetByIdAsync(command.BudgetId, command.UserId, ct)
            ?? throw new NotFoundException("Budget", command.BudgetId);

        var now = DateTimeOffset.UtcNow;
        budget.DeletedAt = now;
        budget.UpdatedAt = now;

        // budget_periods giữ nguyên làm lịch sử chi tiêu — chúng không hiển thị ở đâu nữa vì
        // GetBudgetSummary chỉ đọc qua budget chưa xóa.
        await _budgetRepository.UpdateAsync(budget, ct);

        var (year, month) = BudgetCalendar.VietnamYearMonth(now);
        await _cache.RemoveAsync(CacheKeys.BudgetSummary(command.UserId, year, month), ct);
    }
}
