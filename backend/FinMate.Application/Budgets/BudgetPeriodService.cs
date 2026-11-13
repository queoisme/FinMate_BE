using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Application.Budgets;

public class BudgetPeriodService : IBudgetPeriodService
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IBudgetAlertNotifier _alertNotifier;
    private readonly IAlertThresholdProvider _thresholdProvider;
    private readonly ITransactionRepository? _transactions;

    public BudgetPeriodService(
        IBudgetRepository budgetRepository,
        IBudgetAlertNotifier alertNotifier,
        IAlertThresholdProvider thresholdProvider,
        ITransactionRepository? transactions = null)
    {
        _budgetRepository = budgetRepository;
        _alertNotifier = alertNotifier;
        _thresholdProvider = thresholdProvider;
        _transactions = transactions;
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
        AlertThresholds? thresholds = null;

        foreach (var budget in budgets)
        {
            var period = await GetOrAddPeriodAsync(budget, periodStart, periodEnd, now, ct);

            // Clamp ở 0: một revert lệch (ví dụ giao dịch được tạo trước khi budget tồn tại
            // rồi bị xóa sau đó) không được đẩy spent_cents xuống âm.
            period.SpentCents = Math.Max(0, period.SpentCents + spentDeltaCents);
            period.UpdatedAt = now;

            // Chỉ xét cảnh báo khi chi tiêu TĂNG. Delta âm là hoàn lại (sửa/xoá giao dịch)
            // và nó chỉ có thể kéo phần trăm đi xuống — chạy evaluator ở đó là công vô ích.
            if (spentDeltaCents > 0)
            {
                // Ngưỡng T1/T2 của người dùng: chỉ tra khi chi tiêu TĂNG (mới cần đánh giá), và đúng một lần cho cả vòng lặp.
                thresholds ??= await _thresholdProvider.GetAsync(userId, ct);
                await EvaluateAsync(userId, budget, period, thresholds, now, alerts, ct);
            }
        }

        return alerts;
    }

    public async Task<IReadOnlyList<BudgetAlert>> RecomputeAsync(
        Guid userId,
        IReadOnlyCollection<Transaction> pending,
        IReadOnlyCollection<(Guid? CategoryId, DateTimeOffset At)> touched,
        CancellationToken ct = default)
    {
        var transactions = _transactions
            ?? throw new InvalidOperationException("BudgetPeriodService cần ITransactionRepository để tính lại kỳ ngân sách.");
        var pendingIds = pending.Select(t => t.Id).Where(id => id != Guid.Empty).ToList();
        var now = DateTimeOffset.UtcNow;
        var alerts = new List<BudgetAlert>();
        var done = new HashSet<(Guid BudgetId, DateTimeOffset Start)>();
        AlertThresholds? thresholds = null;

        foreach (var (categoryId, at) in touched)
        {
            var (periodStart, periodEnd) = VietnamTime.MonthRange(at);
            foreach (var budget in await _budgetRepository.GetMatchingBudgetsAsync(userId, categoryId, ct))
            {
                if (!done.Add((budget.Id, periodStart)))
                {
                    continue;
                }

                var scope = BudgetSpendScope.Personal(userId, budget.CategoryId, periodStart, periodEnd);
                var spent = await transactions.SumNetSpendAsync(scope, pendingIds, ct)
                    + pending.Sum(t => BudgetSpendScope.ContributionOf(t, scope));
                var period = await GetOrAddPeriodAsync(budget, periodStart, periodEnd, now, ct);
                var before = period.SpentCents;
                period.SpentCents = Math.Max(0, spent);
                period.UpdatedAt = now;

                if (period.SpentCents > before)
                {
                    thresholds ??= await _thresholdProvider.GetAsync(userId, ct);
                    await EvaluateAsync(userId, budget, period, thresholds, now, alerts, ct);
                }
            }
        }

        return alerts;
    }

    private async Task<BudgetPeriod> GetOrAddPeriodAsync(Budget budget, DateTimeOffset periodStart, DateTimeOffset periodEnd, DateTimeOffset now, CancellationToken ct)
    {
        var period = await _budgetRepository.GetPeriodAsync(budget.Id, periodStart, ct);
        if (period is not null)
        {
            return period;
        }

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
        return period;
    }

    private async Task EvaluateAsync(
        Guid userId, Budget budget, BudgetPeriod period, AlertThresholds thresholds, DateTimeOffset now, List<BudgetAlert> alerts, CancellationToken ct)
    {
        var scope = budget.Category?.Name ?? "toàn bộ chi tiêu";
        var alert = BudgetAlertEvaluator.Evaluate(period, userId, scope, thresholds);

        // Hỏi tuỳ chọn thông báo TRƯỚC khi đánh dấu — và chỉ hỏi khi thật sự có mốc
        // vừa chạm, nên giao dịch bình thường không phải trả giá một truy vấn thừa.
        if (alert is not null && await _alertNotifier.IsEnabledAsync(userId, ct))
        {
            BudgetAlertEvaluator.MarkSent(period, alert, now);
            alerts.Add(alert);
        }
    }
}
