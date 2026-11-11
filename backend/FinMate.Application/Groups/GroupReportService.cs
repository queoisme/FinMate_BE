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
    IReadOnlyList<GroupMemberTotalsDto> ByMember);

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
        var spending = rows.Where(r => r.Type == TransactionType.Debit).ToList();

        return new GroupMonthlyReportDto(
            year,
            month,
            spending.Sum(r => r.AmountCents),
            rows.Where(r => r.Type == TransactionType.Credit).Sum(r => r.AmountCents),
            spending
                .GroupBy(r => (r.GroupCategoryId, CategoryId: r.GroupCategoryId is null && r.CategoryIsSystem ? r.CategoryId : null))
                .Select(g => new GroupCategorySpendDto(g.Key.CategoryId, g.Key.GroupCategoryId, CategoryNameOf(g.First()), g.Sum(r => r.AmountCents)))
                .OrderByDescending(c => c.SpentCents)
                .ToList(),
            rows
                .GroupBy(r => r.UserId)
                .Select(g => new GroupMemberTotalsDto(
                    g.Key,
                    g.First().IsUserActive ? g.First().MemberName : GroupMemberService.InactiveMemberName,
                    g.Where(r => r.Type == TransactionType.Debit).Sum(r => r.AmountCents),
                    g.Where(r => r.Type == TransactionType.Credit).Sum(r => r.AmountCents)))
                .OrderByDescending(m => m.SpentCents)
                .ToList());
    }

    private static string CategoryNameOf(GroupReportRow row)
        => row.GroupCategoryName ?? (row.CategoryIsSystem ? row.CategoryName : null) ?? GroupTransactionService.UncategorizedName;
}
