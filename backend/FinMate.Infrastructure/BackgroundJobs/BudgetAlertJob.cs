using FinMate.Application.Budgets;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Lưới vét cho cảnh báo ngưỡng ngân sách 70% / 90% / 100%.
///
/// Từ Phase 10, đường CHÍNH là tức thì: <c>BudgetPeriodService.ApplyDeltaAsync</c> đánh giá
/// ngưỡng ngay khi giao dịch phát sinh (docx Flow 2 mục 2a, Flow 3 mục 1). Job này vẫn cần
/// vì có hai đường làm phần trăm vượt ngưỡng mà KHÔNG có giao dịch nào:
///
/// - <c>UpdateBudgetLimitCommandHandler</c> hạ hạn mức xuống dưới mức đã chi;
/// - <c>CreateBudgetCommandHandler</c> backfill <c>spent_cents</c> từ giao dịch đã có.
///
/// Logic chọn mốc nằm ở <see cref="BudgetAlertEvaluator"/>, dùng chung với đường tức thì.
/// </summary>
public class BudgetAlertJob
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IBudgetAlertNotifier _notifier;
    private readonly IAlertThresholdProvider _thresholdProvider;

    public BudgetAlertJob(
        IBudgetRepository budgetRepository,
        IBudgetAlertNotifier notifier,
        IAlertThresholdProvider thresholdProvider)
    {
        _budgetRepository = budgetRepository;
        _notifier = notifier;
        _thresholdProvider = thresholdProvider;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await _budgetRepository.GetPeriodsForAlertAsync(now, ct);
        var alerts = new List<BudgetAlert>();
        var thresholdsByUser = new Dictionary<Guid, AlertThresholds>();   // một người có thể có nhiều ngân sách trong cùng lượt quét

        foreach (var (period, budget) in candidates)
        {
            var scope = budget.Category?.Name ?? "toàn bộ chi tiêu";
            if (!thresholdsByUser.TryGetValue(budget.UserId, out var thresholds))
            {
                thresholds = await _thresholdProvider.GetAsync(budget.UserId, ct);
                thresholdsByUser[budget.UserId] = thresholds;
            }

            var alert = BudgetAlertEvaluator.Evaluate(period, budget.UserId, scope, thresholds);
            if (alert is null || !await _notifier.IsEnabledAsync(budget.UserId, ct))
            {
                continue;
            }

            BudgetAlertEvaluator.MarkSent(period, alert, now);
            alerts.Add(alert);
            await _budgetRepository.UpdatePeriodAsync(period, ct);
        }

        // Gửi sau khi đã lưu cờ, cùng thứ tự với đường tức thì.
        await _notifier.SendAsync(alerts, ct);
    }
}
