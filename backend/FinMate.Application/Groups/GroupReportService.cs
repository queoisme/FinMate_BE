using FinMate.Application.Reports;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups;

public sealed record GroupCategorySpendDto(Guid? CategoryId, Guid? GroupCategoryId, string CategoryName, long SpentCents);

public sealed record GroupMemberTotalsDto(Guid UserId, string MemberName, long SpentCents, long IncomeCents);

public sealed record GroupMonthlyReportDto(
    int Year,
    int Month,
    long TotalSpentCents,
    long TotalIncomeCents,
    IReadOnlyList<GroupCategorySpendDto> ByCategory,
    IReadOnlyList<GroupMemberTotalsDto> ByMember,
    long RefundedCents = 0);

public interface IGroupReportService
{
    /// <summary>Tổng hiện với mọi thành viên, kể cả ở chế độ <c>Summary</c>: chế độ đó chỉ ẩn chi tiết từng giao dịch.</summary>
    Task<GroupMonthlyReportDto> GetMonthlyAsync(Guid groupId, Guid userId, int year, int month, CancellationToken ct = default);
}

public class GroupReportService : IGroupReportService
{
    private readonly IGroupFinanceRepository _finance;
    private readonly IGroupAccessService _access;

    public GroupReportService(IGroupFinanceRepository finance, IGroupAccessService access)
    {
        _finance = finance;
        _access = access;
    }

    public async Task<GroupMonthlyReportDto> GetMonthlyAsync(Guid groupId, Guid userId, int year, int month, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        var (start, end) = VietnamTime.MonthRange(year, month);
        var rows = await _finance.ListReportRowsAsync(groupId, start, end, ct);
        // Phase 81 (NetFlow): chi ròng = chi − hoàn (khoản hoàn đi cùng danh mục nhóm của khoản gốc), thu không gồm hoàn, cặp đảo không vào đâu.
        var flows = rows.Select(r => (Row: r, Parts: NetFlow.Of(r.Type, r.Relation, r.IsReversed, r.AmountCents))).ToList();
        var spending = flows.Where(f => f.Parts.SpentCents != 0).ToList();

        return new GroupMonthlyReportDto(
            year,
            month,
            Math.Max(0, spending.Sum(f => f.Parts.SpentCents)),
            flows.Sum(f => f.Parts.IncomeCents),
            spending
                .GroupBy(f => (f.Row.GroupCategoryId, CategoryId: f.Row.GroupCategoryId is null && f.Row.CategoryIsSystem ? f.Row.CategoryId : null))
                .Select(g => new GroupCategorySpendDto(g.Key.CategoryId, g.Key.GroupCategoryId, CategoryNameOf(g.First().Row), g.Sum(f => f.Parts.SpentCents)))
                .Where(c => c.SpentCents > 0)
                .OrderByDescending(c => c.SpentCents)
                .ToList(),
            flows
                .GroupBy(f => f.Row.UserId)
                .Select(g => new GroupMemberTotalsDto(
                    g.Key,
                    g.First().Row.IsUserActive ? g.First().Row.MemberName : GroupMemberService.InactiveMemberName,
                    Math.Max(0, g.Sum(f => f.Parts.SpentCents)),
                    g.Sum(f => f.Parts.IncomeCents)))
                .OrderByDescending(m => m.SpentCents)
                .ToList(),
            flows.Sum(f => f.Parts.RefundedCents));
    }

    private static string CategoryNameOf(GroupReportRow row)
        => row.GroupCategoryName ?? (row.CategoryIsSystem ? row.CategoryName : null) ?? GroupTransactionService.UncategorizedName;
}
