using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups.Funds;

/// <param name="MemberUserId">Null là thành viên cũ đã ẩn danh.</param>
public sealed record GroupFundMemberAmountDto(Guid? MemberUserId, string MemberName, long AmountCents);

public sealed record GroupFundCategoryAmountDto(Guid? CategoryId, Guid? GroupCategoryId, string CategoryName, long AmountCents);

/// <summary>
/// BR-FUND-12: đầu kỳ, góp theo người, chi theo danh mục, rút, cuối kỳ của một tháng (giờ VN). <c>ClosingCents = OpeningCents + ContributedCents −
/// SpentCents − WithdrawnCents + AdjustmentsCents</c>. Dòng đã xoá không tính.
/// </summary>
public sealed record GroupFundSummaryDto(
    Guid FundId,
    int Year,
    int Month,
    long OpeningCents,
    long ContributedCents,
    IReadOnlyList<GroupFundMemberAmountDto> ContributionsByMember,
    long SpentCents,
    IReadOnlyList<GroupFundCategoryAmountDto> ExpensesByCategory,
    long WithdrawnCents,
    IReadOnlyList<GroupFundMemberAmountDto> WithdrawalsByMember,
    long AdjustmentsCents,
    long ClosingCents);

public interface IGroupFundSummaryService
{
    Task<GroupFundSummaryDto> GetAsync(Guid groupId, Guid userId, Guid fundId, int year, int month, CancellationToken ct = default);
}

public class GroupFundSummaryService : IGroupFundSummaryService
{
    private readonly IGroupFundRepository _funds;
    private readonly IGroupAccessService _access;

    public GroupFundSummaryService(IGroupFundRepository funds, IGroupAccessService access)
    {
        _funds = funds;
        _access = access;
    }

    public async Task<GroupFundSummaryDto> GetAsync(Guid groupId, Guid userId, Guid fundId, int year, int month, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        if (month is < 1 or > 12 || year is < 2000 or > 9999)
        {
            throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure(nameof(month), "Tháng hoặc năm không hợp lệ.")]);
        }

        _ = await _funds.GetAsync(groupId, fundId, ct) ?? throw new NotFoundException("GroupFund", fundId);
        var (start, end) = VietnamTime.MonthRange(year, month);
        var opening = await _funds.SumBeforeAsync(fundId, start, ct);
        var lines = await _funds.ListEntriesInRangeAsync(fundId, start, end, ct);

        var contributed = Total(lines, GroupFundEntryKind.Contribution);
        var spent = Total(lines, GroupFundEntryKind.Expense);
        var withdrawn = Total(lines, GroupFundEntryKind.Withdrawal);
        var adjustments = Total(lines, GroupFundEntryKind.Adjustment);
        return new GroupFundSummaryDto(
            fundId, year, month, opening,
            contributed, ByMember(lines, GroupFundEntryKind.Contribution),
            spent, ByCategory(lines),
            withdrawn, ByMember(lines, GroupFundEntryKind.Withdrawal),
            adjustments,
            opening + contributed - spent - withdrawn + adjustments);
    }

    private static long Total(IEnumerable<GroupFundEntryView> lines, GroupFundEntryKind kind)
        => lines.Where(l => l.Entry.Kind == kind).Sum(l => l.Entry.AmountCents);

    private static List<GroupFundMemberAmountDto> ByMember(IEnumerable<GroupFundEntryView> lines, GroupFundEntryKind kind)
        => lines
            .Where(l => l.Entry.Kind == kind)
            .GroupBy(l => l.Entry.MemberUserId)
            .Select(g => new GroupFundMemberAmountDto(
                g.Key, GroupFundEntryService.MemberNameOf(g.First().MemberName, g.First().IsMemberActive), g.Sum(l => l.Entry.AmountCents)))
            .OrderByDescending(m => m.AmountCents)
            .ToList();

    private static List<GroupFundCategoryAmountDto> ByCategory(IEnumerable<GroupFundEntryView> lines)
        => lines
            .Where(l => l.Entry.Kind == GroupFundEntryKind.Expense)
            .GroupBy(l => (l.Entry.GroupCategoryId, CategoryId: l.Entry.GroupCategoryId is null ? l.Entry.CategoryId : null))
            .Select(g => new GroupFundCategoryAmountDto(
                g.Key.CategoryId, g.Key.GroupCategoryId, g.First().CategoryName ?? GroupTransactionService.UncategorizedName, g.Sum(l => l.Entry.AmountCents)))
            .OrderByDescending(c => c.AmountCents)
            .ToList();
}
