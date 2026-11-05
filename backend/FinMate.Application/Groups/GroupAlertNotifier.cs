using FinMate.Application.Budgets;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Groups;

public sealed record GroupBudgetAlert(Guid GroupId, Guid BudgetId, long LimitCents, DateTimeOffset PeriodStart, BudgetAlert Alert);

/// <summary>
/// Cảnh báo của nhóm gửi riêng từng thành viên qua hộp thư <c>forecast_alerts</c>, nên chống gửi trùng và lưu trước khi push đã có sẵn.
/// Không bao giờ ném lúc gửi: giao dịch đã lưu rồi, báo lỗi lúc này khiến client gửi lại và ghi trùng tiền. Nội dung không có số tiền.
/// </summary>
public interface IGroupAlertNotifier
{
    /// <summary>Mức vừa chạm mà chưa báo, hoặc null. Đóng cờ trên kỳ cho cả nhóm: ai đang tắt cảnh báo lúc đó sẽ không nhận lại mức này khi bật lại.</summary>
    Task<GroupBudgetAlert?> EvaluateAndMarkAsync(Budget budget, BudgetPeriod period, DateTimeOffset now, CancellationToken ct = default);

    Task SendBudgetAlertsAsync(IReadOnlyList<GroupBudgetAlert> alerts, CancellationToken ct = default);

    /// <param name="reason">Phần cuối của khóa chống trùng, vd <c>completed</c> hoặc <c>overdue</c>.</param>
    Task SendGoalAlertAsync(SavingGoal goal, string title, string body, string reason, CancellationToken ct = default);
}

public class GroupAlertNotifier : IGroupAlertNotifier
{
    private const string DataGroupId = "groupId";
    private const string DataBudgetId = "budgetId";
    private const string DataGoalId = "goalId";
    private const string DataPercent = "percent";
    private const string TotalScope = "toàn bộ chi tiêu";

    private readonly IGroupRepository _groups;
    private readonly IForecastAlertDispatcher _dispatcher;
    private readonly ISystemConfigProvider _config;
    private readonly ILogger<GroupAlertNotifier> _logger;

    public GroupAlertNotifier(IGroupRepository groups, IForecastAlertDispatcher dispatcher, ISystemConfigProvider config, ILogger<GroupAlertNotifier> logger)
    {
        _groups = groups;
        _dispatcher = dispatcher;
        _config = config;
        _logger = logger;
    }

    public async Task<GroupBudgetAlert?> EvaluateAndMarkAsync(Budget budget, BudgetPeriod period, DateTimeOffset now, CancellationToken ct = default)
    {
        var limits = (await _config.GetAsync(ct)).Document.Limits;
        var configured = new AlertThresholds(limits.AlertT1Percent, limits.AlertT2Percent);
        var thresholds = AlertThresholds.IsValid(configured) ? configured : AlertThresholds.Default;

        var group = await _groups.GetByIdAsync(budget.GroupId!.Value, ct);
        if (group is null)
        {
            return null;
        }

        var category = budget.Category?.Name ?? budget.GroupCategory?.Name ?? TotalScope;
        var alert = BudgetAlertEvaluator.Evaluate(period, Guid.Empty, $"{category} của nhóm {group.Name}", thresholds);
        if (alert is null)
        {
            return null;
        }

        BudgetAlertEvaluator.MarkSent(period, alert, now);
        return new GroupBudgetAlert(group.Id, budget.Id, period.LimitCents, period.PeriodStart, alert);
    }

    public async Task SendBudgetAlertsAsync(IReadOnlyList<GroupBudgetAlert> alerts, CancellationToken ct = default)
    {
        foreach (var item in alerts)
        {
            // Hạn mức nằm trong khóa: nâng hạn mức rồi lại chạm ngưỡng là một cảnh báo mới, giống ngân sách cá nhân.
            var dedupKey = $"{item.BudgetId}:{item.PeriodStart:yyyy-MM}:{item.Alert.Level}:{item.LimitCents}";
            var data = new Dictionary<string, string>
            {
                [DataGroupId] = item.GroupId.ToString(),
                [DataBudgetId] = item.BudgetId.ToString(),
                [DataPercent] = item.Alert.Percent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            await SendToMembersAsync(item.GroupId, ForecastAlertKind.GroupBudget, dedupKey, item.Alert.Title, item.Alert.Body, data, ct);
        }
    }

    public Task SendGoalAlertAsync(SavingGoal goal, string title, string body, string reason, CancellationToken ct = default)
    {
        var data = new Dictionary<string, string> { [DataGroupId] = goal.GroupId!.Value.ToString(), [DataGoalId] = goal.Id.ToString() };
        return SendToMembersAsync(goal.GroupId.Value, ForecastAlertKind.GroupGoal, $"{goal.Id}:{reason}", title, body, data, ct);
    }

    private async Task SendToMembersAsync(Guid groupId, ForecastAlertKind kind, string dedupKey, string title, string body, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        try
        {
            var group = await _groups.GetByIdAsync(groupId, ct);
            if (group is null)
            {
                return;
            }

            foreach (var view in await _groups.ListActiveMembersAsync(groupId, ct))
            {
                var member = view.Member;
                var canSee = GroupPermissions.Effective(member.Role, group.TransactionVisibility, member.PermissionsGranted, member.PermissionsRevoked).HasFlag(GroupPermission.ViewOverview);
                if (view.IsUserActive && member.AlertsEnabled && canSee)
                {
                    await _dispatcher.RaiseAsync(new ForecastAlertRequest(member.UserId, kind, dedupKey, title, body, data), ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Group alert could not be sent for {GroupId}: {ExceptionType}", groupId, ex.GetType().Name);
        }
    }
}
