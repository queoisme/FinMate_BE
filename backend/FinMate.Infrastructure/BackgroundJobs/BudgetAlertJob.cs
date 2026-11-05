using FinMate.Application.Groups;
using FinMate.Domain.Entities;
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
    private readonly IGroupAlertNotifier? _groupAlerts;
    private readonly ISystemConfigProvider? _config;

    public BudgetAlertJob(
        IBudgetRepository budgetRepository,
        IBudgetAlertNotifier notifier,
        IAlertThresholdProvider thresholdProvider,
        IGroupAlertNotifier? groupAlerts = null,
        ISystemConfigProvider? config = null)
    {
        _budgetRepository = budgetRepository;
        _notifier = notifier;
        _thresholdProvider = thresholdProvider;
        _groupAlerts = groupAlerts;
        _config = config;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await _budgetRepository.GetPeriodsForAlertAsync(now, ct);

        // Đang tắt thì bỏ qua, không đóng ngưỡng: lượt quét sau khi bật lại sẽ gửi.
        var groupsEnabled = _config is not null && (await _config.GetAsync(ct)).Document.Features.Groups;
        var alerts = new List<BudgetAlert>();
        var thresholdsByUser = new Dictionary<Guid, AlertThresholds>();   // một người có thể có nhiều ngân sách trong cùng lượt quét

        foreach (var (period, budget) in candidates)
        {
            if (budget.UserId is not { } userId)
            {
                if (groupsEnabled)
                {
                    await SweepGroupBudgetAsync(budget, period, now, ct);
                }

                continue;
            }

            var scope = budget.Category?.Name ?? "toàn bộ chi tiêu";
            if (!thresholdsByUser.TryGetValue(userId, out var thresholds))
            {
                thresholds = await _thresholdProvider.GetAsync(userId, ct);
                thresholdsByUser[userId] = thresholds;
            }

            var alert = BudgetAlertEvaluator.Evaluate(period, userId, scope, thresholds);
            if (alert is null || !await _notifier.IsEnabledAsync(userId, ct))
            {
                continue;
            }

            BudgetAlertEvaluator.MarkSent(period, alert, now);

            // Chỉ báo khi cờ đã lưu được. Kỳ vừa bị giao dịch khác đổi thì để lượt sau xét lại, tránh báo trùng với đường tức thì.
            if (await _budgetRepository.UpdatePeriodAsync(period, ct))
            {
                alerts.Add(alert);
            }
        }

        // Gửi sau khi đã lưu cờ, cùng thứ tự với đường tức thì.
        await _notifier.SendAsync(alerts, ct);
    }

    private async Task SweepGroupBudgetAsync(Budget budget, BudgetPeriod period, DateTimeOffset now, CancellationToken ct)
    {
        if (_groupAlerts is null)
        {
            return;
        }

        var alert = await _groupAlerts.EvaluateAndMarkAsync(budget, period, now, ct);
        if (alert is not null && await _budgetRepository.UpdatePeriodAsync(period, ct))
        {
            await _groupAlerts.SendBudgetAlertsAsync([alert], ct);
        }
    }
}
