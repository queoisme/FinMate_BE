using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups;

public sealed record GroupShareTarget(Guid GroupId, Guid? GroupCategoryId);

/// <summary>
/// Cộng và hoàn phần giao dịch đã chia sẻ vào ngân sách nhóm. Theo đúng khuôn của <c>BudgetPeriodService</c>: chỉ đổi dữ liệu đang được theo
/// dõi và KHÔNG lưu, để handler lưu tiền cá nhân và tiền nhóm trong cùng một <c>SaveChanges</c>; cảnh báo trả về và chỉ gửi sau khi lưu.
/// Bất biến: <c>spent_cents</c> của kỳ = tổng khoản chi đã xác nhận, đã chia sẻ, khớp ngân sách. Vẫn áp dụng khi chủ giao dịch đã rời nhóm, và cả khi cờ <c>features.groups</c>
/// đang tắt: tắt cờ chỉ chặn chia sẻ mới và cảnh báo, không được làm số của nhóm lệch khi người dùng sửa hay xóa giao dịch đã chia sẻ.
/// </summary>
public interface IGroupLedger
{
    /// <summary>Nhóm cho giao dịch vừa xác nhận: nhóm người dùng chọn (kiểm quyền, không hợp lệ thì ném), không chọn thì theo quy tắc tự chia sẻ của họ.</summary>
    Task<GroupShareTarget?> ResolveAsync(Guid userId, Guid? groupId, Guid? groupCategoryId, Transaction transaction, Category? category, CancellationToken ct = default);

    void MarkShared(Transaction transaction, GroupShareTarget target, Guid actorUserId, DateTimeOffset now);

    /// <param name="sign">+1 khi tính vào nhóm, -1 khi hoàn. Đọc số tiền, ngày và danh mục từ <paramref name="transaction"/> như nó đang là lúc gọi.</param>
    Task<IReadOnlyList<GroupBudgetAlert>> ApplyAsync(Transaction transaction, Category? category, int sign, CancellationToken ct = default);

    Task SendAlertsAsync(IReadOnlyList<GroupBudgetAlert> alerts, CancellationToken ct = default);
}

public class GroupLedger : IGroupLedger
{
    private readonly IGroupFinanceRepository _finance;
    private readonly IGroupRepository _groups;
    private readonly IBudgetRepository _budgets;
    private readonly IGroupAccessService _access;
    private readonly IGroupAlertNotifier _alerts;
    private readonly ISystemConfigProvider _config;

    public GroupLedger(IGroupFinanceRepository finance, IGroupRepository groups, IBudgetRepository budgets, IGroupAccessService access, IGroupAlertNotifier alerts, ISystemConfigProvider config)
    {
        _config = config;
        _finance = finance;
        _groups = groups;
        _budgets = budgets;
        _access = access;
        _alerts = alerts;
    }

    private async Task<bool> IsEnabledAsync(CancellationToken ct) => (await _config.GetAsync(ct)).Document.Features.Groups;

    public async Task<GroupShareTarget?> ResolveAsync(Guid userId, Guid? groupId, Guid? groupCategoryId, Transaction transaction, Category? category, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct))
        {
            // Chủ động xin chia sẻ thì báo rõ. Không xin thì giao dịch cá nhân vẫn lưu bình thường, chỉ bỏ qua quy tắc tự chia sẻ.
            return groupId is null && groupCategoryId is null
                ? null
                : throw new BusinessRuleException(GroupErrorCodes.Disabled, "Tính năng nhóm hiện chưa được bật.");
        }

        if (groupId is { } requested)
        {
            EnsureShareable(transaction, category);
            await _access.RequireAsync(requested, userId, GroupPermission.ShareTransactions, ct);
            await EnsureCategoryAsync(requested, groupCategoryId, ct);
            return new GroupShareTarget(requested, groupCategoryId);
        }

        if (groupCategoryId is not null)
        {
            throw NotShareable("Chọn danh mục nhóm thì phải chọn cả nhóm.");
        }

        return IsShareable(transaction, category) ? await MatchRuleAsync(userId, transaction, ct) : null;
    }

    public void MarkShared(Transaction transaction, GroupShareTarget target, Guid actorUserId, DateTimeOffset now)
    {
        if (transaction.Id == Guid.Empty)
        {
            transaction.Id = Guid.NewGuid();
        }

        transaction.GroupId = target.GroupId;
        transaction.GroupCategoryId = target.GroupCategoryId;
        transaction.SharedAt = now;
        _groups.AddActivity(GroupService.NewActivity(target.GroupId, actorUserId, GroupActivityType.TransactionShared, transaction.Id, now));
    }

    public async Task<IReadOnlyList<GroupBudgetAlert>> ApplyAsync(Transaction transaction, Category? category, int sign, CancellationToken ct = default)
    {
        var spend = TransactionBudgetDelta.Spend(transaction.TransactionType, transaction.AmountCents, category);
        if (transaction.GroupId is not { } groupId || transaction.Status != TransactionStatus.Confirmed || spend == 0)
        {
            return [];
        }

        var systemCategoryId = category is { IsSystem: true } ? category.Id : (Guid?)null;
        var budgets = await _finance.GetMatchingBudgetsAsync(groupId, systemCategoryId, transaction.GroupCategoryId, ct);
        var (periodStart, periodEnd) = VietnamTime.MonthRange(transaction.TransactedAt);
        var now = DateTimeOffset.UtcNow;
        var alerts = new List<GroupBudgetAlert>();

        // Đang tắt thì không đóng ngưỡng cảnh báo: đóng mà không gửi là mất hẳn ngưỡng đó. BudgetAlertJob quét lại khi cờ bật.
        var alertsEnabled = sign > 0 && await IsEnabledAsync(ct);

        foreach (var budget in budgets)
        {
            var period = await _budgets.GetPeriodAsync(budget.Id, periodStart, ct) ?? AddPeriod(budget, periodStart, periodEnd, now);
            period.SpentCents = Math.Max(0, period.SpentCents + (sign * spend));
            period.UpdatedAt = now;

            if (alertsEnabled && await _alerts.EvaluateAndMarkAsync(budget, period, now, ct) is { } alert)
            {
                alerts.Add(alert);
            }
        }

        return alerts;
    }

    public Task SendAlertsAsync(IReadOnlyList<GroupBudgetAlert> alerts, CancellationToken ct = default)
        => alerts.Count == 0 ? Task.CompletedTask : _alerts.SendBudgetAlertsAsync(alerts, ct);

    internal static bool IsShareable(Transaction transaction, Category? category)
        => transaction.Status == TransactionStatus.Confirmed
            && transaction.TransactionType != TransactionType.Transfer
            && category?.Slug != Category.LoanSlug;

    internal static void EnsureShareable(Transaction transaction, Category? category)
    {
        if (!IsShareable(transaction, category))
        {
            throw NotShareable("Chỉ chia sẻ được khoản thu chi đã xác nhận; không gồm chuyển khoản nội bộ và cho vay/mượn.");
        }
    }

    private async Task<GroupShareTarget?> MatchRuleAsync(Guid userId, Transaction transaction, CancellationToken ct)
    {
        var rules = await _finance.ListActiveRulesForUserAsync(userId, ct);
        foreach (var rule in rules.Where(r => Matches(r, transaction)))
        {
            // Quy tắc còn đó nhưng người dùng đã rời nhóm hoặc bị thu quyền thì bỏ qua: quy tắc không bao giờ làm hỏng việc lưu giao dịch.
            if (await HasShareAccessAsync(rule.GroupId, userId, ct))
            {
                return new GroupShareTarget(rule.GroupId, null);
            }
        }

        return null;
    }

    private async Task<bool> HasShareAccessAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var group = await _groups.GetByIdAsync(groupId, ct);
        var member = group is null ? null : await _groups.GetActiveMemberAsync(groupId, userId, ct);
        return member is not null
            && GroupPermissions.Effective(member.Role, group!.TransactionVisibility, member.PermissionsGranted, member.PermissionsRevoked).HasFlag(GroupPermission.ShareTransactions);
    }

    private static bool Matches(GroupShareRule rule, Transaction transaction)
        => (rule.FinancialAccountId is null || rule.FinancialAccountId == transaction.FinancialAccountId)
            && (rule.CategoryId is null || rule.CategoryId == transaction.CategoryId);

    private async Task EnsureCategoryAsync(Guid groupId, Guid? groupCategoryId, CancellationToken ct)
    {
        if (groupCategoryId is { } id && await _finance.GetCategoryAsync(groupId, id, ct) is null)
        {
            throw new NotFoundException("GroupCategory", id);
        }
    }

    private BudgetPeriod AddPeriod(Budget budget, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        var period = new BudgetPeriod
        {
            Id = Guid.NewGuid(),
            BudgetId = budget.Id,
            PeriodStart = start,
            PeriodEnd = end,
            LimitCents = budget.LimitCents,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _budgets.AddPeriod(period);
        return period;
    }

    private static BusinessRuleException NotShareable(string message) => new(GroupErrorCodes.TransactionNotShareable, message);
}
