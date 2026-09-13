using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Application.Budgets;

public class BudgetPeriodService : IBudgetPeriodService
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IBudgetAlertNotifier _alertNotifier;

    public BudgetPeriodService(
        IBudgetRepository budgetRepository, IBudgetAlertNotifier alertNotifier)
    {
        _budgetRepository = budgetRepository;
        _alertNotifier = alertNotifier;
    }

    public async Task<IReadOnlyList<BudgetAlert>> ApplyDeltaAsync(
        Guid userId,
        Guid? categoryId,
        long spentDeltaCents,
        DateTimeOffset transactedAt,
        CancellationToken ct = default)
    {
        if (spentDeltaCents == 0)
        {
            return [];
        }

        var budgets = await _budgetRepository.GetMatchingBudgetsAsync(userId, categoryId, ct);
        if (budgets.Count == 0)
        {
            return [];
        }

        var (periodStart, periodEnd) = VietnamTime.MonthRange(transactedAt);
        var now = DateTimeOffset.UtcNow;
        var alerts = new List<BudgetAlert>();

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

            // Chỉ xét cảnh báo khi chi tiêu TĂNG. Delta âm là hoàn lại (sửa/xoá giao dịch)
            // và nó chỉ có thể kéo phần trăm đi xuống — chạy evaluator ở đó là công vô ích.
            if (spentDeltaCents > 0)
            {
                var scope = budget.Category?.Name ?? "toàn bộ chi tiêu";
                var alert = BudgetAlertEvaluator.Evaluate(period, userId, scope);

                // Hỏi tuỳ chọn thông báo TRƯỚC khi đánh dấu — và chỉ hỏi khi thật sự có mốc
                // vừa chạm, nên giao dịch bình thường không phải trả giá một truy vấn thừa.
                if (alert is not null && await _alertNotifier.IsEnabledAsync(userId, ct))
                {
                    BudgetAlertEvaluator.MarkSent(period, alert, now);
                    alerts.Add(alert);
                }
            }
        }

        return alerts;
    }
}
