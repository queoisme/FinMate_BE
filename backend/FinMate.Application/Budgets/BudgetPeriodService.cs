using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Application.Budgets;

public class BudgetPeriodService : IBudgetPeriodService
{
    private readonly IBudgetRepository _budgetRepository;

    public BudgetPeriodService(IBudgetRepository budgetRepository)
    {
        _budgetRepository = budgetRepository;
    }

    public async Task ApplyDeltaAsync(
        Guid userId,
        Guid? categoryId,
        long spentDeltaCents,
        DateTimeOffset transactedAt,
        CancellationToken ct = default)
    {
        if (spentDeltaCents == 0)
        {
            return;
        }

        var budgets = await _budgetRepository.GetMatchingBudgetsAsync(userId, categoryId, ct);
        if (budgets.Count == 0)
        {
            return;
        }

        var (periodStart, periodEnd) = BudgetCalendar.MonthlyPeriod(transactedAt);
        var now = DateTimeOffset.UtcNow;

        foreach (var budget in budgets)
        {
            var period = await _budgetRepository.GetPeriodAsync(budget.Id, periodStart, ct);

            if (period is null)
            {
                period = new BudgetPeriod
                {
                    Id = Guid.NewGuid(),
                    BudgetId = budget.Id,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    LimitCents = budget.LimitCents,
                    SpentCents = 0,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _budgetRepository.AddPeriod(period);
            }

            // Clamp ở 0: một revert lệch (ví dụ giao dịch được tạo trước khi budget tồn tại
            // rồi bị xóa sau đó) không được đẩy spent_cents xuống âm.
            period.SpentCents = Math.Max(0, period.SpentCents + spentDeltaCents);
            period.UpdatedAt = now;
        }
    }
}
