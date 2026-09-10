using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;

namespace FinMate.Application.Budgets;

internal static class BudgetMapper
{
    internal static BudgetDto ToDto(Budget budget) => new(
        budget.Id,
        budget.CategoryId,
        budget.Category?.Name,
        budget.Category?.Slug,
        budget.LimitCents,
        budget.PeriodType.ToString().ToLowerInvariant());

    internal static int PercentUsed(long spentCents, long limitCents)
        => limitCents <= 0 ? 0 : (int)Math.Min(int.MaxValue, spentCents * 100 / limitCents);
}
