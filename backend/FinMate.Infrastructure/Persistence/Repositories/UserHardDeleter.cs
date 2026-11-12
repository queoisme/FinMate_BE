using FinMate.Application.Budgets;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

/// <summary>
/// Xóa cứng người dùng. Khóa ngoại tự xóa giao dịch đã chia sẻ và tư cách thành viên, nhưng không biết tới số tính sẵn của nhóm, nên phải tính
/// lại trong cùng một DB transaction: không bao giờ để nhóm còn tiền của người đã bị xóa.
/// </summary>
public class UserHardDeleter : IUserHardDeleter
{
    private readonly FinMateDbContext _context;

    public UserHardDeleter(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task HardDeleteAsync(Guid userId, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        var periods = await FindAffectedPeriodsAsync(userId, ct);
        var goalIds = await FindGroupGoalsContributedToAsync(userId, ct);
        var groupIds = await FindGroupsAsync(userId, ct);

        // Bảng này không có khóa ngoại theo người góp; mục tiêu cá nhân đã tự mất theo chủ của nó.
        await _context.GoalContributions.Where(c => c.UserId == userId && goalIds.Contains(c.SavingGoalId)).ExecuteDeleteAsync(ct);

        // IgnoreQueryFilters: the soft-delete filter would otherwise hide the very row we need to remove.
        await _context.Users.IgnoreQueryFilters().Where(u => u.Id == userId).ExecuteDeleteAsync(ct);

        foreach (var period in periods)
        {
            await RecalculateSpentAsync(period, ct);
        }

        foreach (var goalId in goalIds)
        {
            await RecalculateSavedAsync(goalId, ct);
        }

        foreach (var groupId in groupIds)
        {
            await SettleGroupAsync(groupId, ct);
        }

        await transaction.CommitAsync(ct);
    }

    private sealed record AffectedPeriod(Guid PeriodId, Guid GroupId, Guid? CategoryId, Guid? GroupCategoryId, DateTimeOffset Start, DateTimeOffset End);

    private Task<List<AffectedPeriod>> FindAffectedPeriodsAsync(Guid userId, CancellationToken ct)
        => (from p in _context.BudgetPeriods
            join b in _context.Budgets.IgnoreQueryFilters() on p.BudgetId equals b.Id
            where b.GroupId != null
                && _context.Transactions.Any(t => t.UserId == userId && t.GroupId == b.GroupId && t.TransactedAt >= p.PeriodStart && t.TransactedAt < p.PeriodEnd)
            select new AffectedPeriod(p.Id, b.GroupId!.Value, b.CategoryId, b.GroupCategoryId, p.PeriodStart, p.PeriodEnd))
            .ToListAsync(ct);

    private Task<List<Guid>> FindGroupGoalsContributedToAsync(Guid userId, CancellationToken ct)
        => (from c in _context.GoalContributions
            join g in _context.SavingGoals.IgnoreQueryFilters() on c.SavingGoalId equals g.Id
            where c.UserId == userId && g.GroupId != null
            select g.Id)
            .Distinct()
            .ToListAsync(ct);

    private Task<List<Guid>> FindGroupsAsync(Guid userId, CancellationToken ct)
        => _context.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync(ct);

    /// <summary>
    /// Cùng định nghĩa với <c>GroupFinanceRepository.SumSharedSpendAsync</c> (<see cref="BudgetSpendScope"/>: chi − hoàn, bỏ cặp đảo, chặn ở 0);
    /// không cộng trừ dần nên chạy lại vẫn đúng. Cặp gốc–hoàn của người bị xoá đi cùng nhau nên không để lại khoản hoàn lẻ.
    /// </summary>
    private async Task RecalculateSpentAsync(AffectedPeriod period, CancellationToken ct)
    {
        var scope = BudgetSpendScope.Group(period.GroupId, period.CategoryId, period.GroupCategoryId, period.Start, period.End);
        var spent = Math.Max(0, await _context.Transactions.Where(scope).Where(BudgetSpendScope.Counts).Select(BudgetSpendScope.Signed).SumAsync(ct));
        await _context.BudgetPeriods.Where(p => p.Id == period.PeriodId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SpentCents, spent).SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow), ct);
    }

    /// <summary>Trạng thái hoàn thành giữ nguyên: nhóm đã đạt mục tiêu thì đó vẫn là sự thật, chỉ số tiền phản ánh phần còn lại.</summary>
    private async Task RecalculateSavedAsync(Guid goalId, CancellationToken ct)
    {
        var saved = await _context.GoalContributions.Where(c => c.SavingGoalId == goalId).SumAsync(c => (long?)c.AmountCents, ct) ?? 0;
        await _context.SavingGoals.IgnoreQueryFilters().Where(g => g.Id == goalId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.SavedCents, saved).SetProperty(g => g.UpdatedAt, DateTimeOffset.UtcNow), ct);
    }

    /// <summary>Nhóm không còn ai thì xóa mềm. Còn người nhưng không còn chủ (không ai hoạt động lúc người này yêu cầu xóa) thì nâng người có vai trò cao nhất, lâu năm nhất.</summary>
    private async Task SettleGroupAsync(Guid groupId, CancellationToken ct)
    {
        var members = await _context.GroupMembers.Where(m => m.GroupId == groupId && m.LeftAt == null).ToListAsync(ct);
        if (members.Count == 0)
        {
            var now = DateTimeOffset.UtcNow;
            await _context.Groups.Where(g => g.Id == groupId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.DeletedAt, now).SetProperty(g => g.UpdatedAt, now), ct);
            return;
        }

        if (members.All(m => m.Role != GroupRole.Owner))
        {
            var successor = members.OrderBy(m => m.Role).ThenBy(m => m.JoinedAt).First();
            successor.Role = GroupRole.Owner;
            successor.PermissionsGranted = GroupPermission.None;
            successor.PermissionsRevoked = GroupPermission.None;
            successor.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }
}
