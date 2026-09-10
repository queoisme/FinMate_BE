using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Period đang chạm ngưỡng cảnh báo, kèm budget sở hữu nó (để biết user và tên category).</summary>
public record BudgetAlertCandidate(BudgetPeriod Period, Budget Budget);

public interface IBudgetRepository
{
    Task<Budget?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<List<Budget>> GetListForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Budget bị ảnh hưởng bởi 1 giao dịch thuộc <paramref name="categoryId"/>: budget riêng
    /// của category đó (nếu có) và budget tổng (category_id NULL) — một giao dịch có thể tiêu
    /// hạn mức của cả hai.
    /// </summary>
    Task<List<Budget>> GetMatchingBudgetsAsync(Guid userId, Guid? categoryId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid userId, Guid? categoryId, BudgetPeriodType periodType, CancellationToken ct = default);
    Task AddAsync(Budget budget, CancellationToken ct = default);
    Task UpdateAsync(Budget budget, CancellationToken ct = default);

    Task<BudgetPeriod?> GetPeriodAsync(Guid budgetId, DateTimeOffset periodStart, CancellationToken ct = default);
    Task<List<BudgetPeriod>> GetPeriodsForUserAsync(Guid userId, DateTimeOffset periodStart, CancellationToken ct = default);

    /// <summary>
    /// Chỉ track entity mới, KHÔNG gọi SaveChangesAsync — để handler gọi ApplyDeltaAsync vẫn
    /// flush toàn bộ thay đổi trong đúng 1 DB transaction ngầm của EF Core.
    /// </summary>
    void AddPeriod(BudgetPeriod period);

    Task<List<BudgetAlertCandidate>> GetPeriodsForAlertAsync(DateTimeOffset now, CancellationToken ct = default);
    Task UpdatePeriodAsync(BudgetPeriod period, CancellationToken ct = default);
}
