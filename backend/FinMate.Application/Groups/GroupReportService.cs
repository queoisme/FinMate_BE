using FinMate.Application.Reports;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups;

public sealed record GroupCategorySpendDto(Guid? CategoryId, Guid? GroupCategoryId, string CategoryName, long SpentCents);

/// <param name="FundSpentCents">Phần trong <paramref name="SpentCents"/> là chi từ quỹ nhóm (Phase 85).</param>
public sealed record GroupMemberTotalsDto(Guid UserId, string MemberName, long SpentCents, long IncomeCents, long FundSpentCents = 0);

/// <summary>Phần chi của người đã xoá cứng tài khoản (dòng quỹ đã ẩn danh, S5). Không có trong <c>byMember</c>, vì không còn biết là của ai.</summary>
public sealed record GroupFormerMembersDto(long TotalSpentCents, long FundSpentCents);

/// <param name="FundSpentCents">Phần trong <paramref name="TotalSpentCents"/> là chi từ quỹ nhóm (Phase 85, BR-FUND-10).</param>
/// <param name="FormerMembers">Null khi không có (S5).</param>
public sealed record GroupMonthlyReportDto(
    int Year,
    int Month,
    long TotalSpentCents,
    long TotalIncomeCents,
    IReadOnlyList<GroupCategorySpendDto> ByCategory,
    IReadOnlyList<GroupMemberTotalsDto> ByMember,
    long RefundedCents = 0,
    long FundSpentCents = 0,
    GroupFormerMembersDto? FormerMembers = null);

public interface IGroupReportService
{
    /// <summary>Tổng hiện với mọi thành viên, kể cả ở chế độ <c>Summary</c>: chế độ đó chỉ ẩn chi tiết từng giao dịch.</summary>
    Task<GroupMonthlyReportDto> GetMonthlyAsync(Guid groupId, Guid userId, int year, int month, CancellationToken ct = default);
}

public class GroupReportService : IGroupReportService
{
    private readonly IGroupFinanceRepository _finance;
    private readonly IGroupAccessService _access;
    private readonly IGroupFundRepository? _funds;
    private readonly IGroupBillRepository? _bills;

    public GroupReportService(IGroupFinanceRepository finance, IGroupAccessService access, IGroupFundRepository? funds = null, IGroupBillRepository? bills = null)
    {
        _bills = bills;
        _finance = finance;
        _access = access;
        _funds = funds;
    }

    /// <summary>Một khoản đã quy về phần chi/thu/hoàn, từ giao dịch chia sẻ hoặc từ quỹ. <c>UserId</c> null là thành viên cũ đã ẩn danh.</summary>
    private sealed record Line(
        Guid? UserId, string? MemberName, bool IsUserActive, Guid? GroupCategoryId, Guid? CategoryId, string CategoryName, FlowParts Parts, bool FromFund);

    public async Task<GroupMonthlyReportDto> GetMonthlyAsync(Guid groupId, Guid userId, int year, int month, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        var (start, end) = VietnamTime.MonthRange(year, month);

        // Phase 81 (NetFlow): chi ròng = chi − hoàn (khoản hoàn đi cùng danh mục nhóm của khoản gốc), thu không gồm hoàn, cặp đảo không vào đâu.
        var lines = (await _finance.ListReportRowsAsync(groupId, start, end, ct)).Select(FromShared).ToList();

        // Phase 85 (BR-FUND-10, D14): chi từ quỹ tính cho nhóm, người đứng tên là người ghi. Góp, rút, điều chỉnh không phải chi hay thu.
        if (_funds is not null)
        {
            lines.AddRange((await _funds.ListExpenseRowsAsync(groupId, start, end, ct)).Select(FromFund));
        }

        // Hóa đơn chia tính cho nhóm theo toàn bộ số tiền, đứng tên người trả (không phải chi từ quỹ).
        if (_bills is not null)
        {
            lines.AddRange((await _bills.ListExpenseRowsAsync(groupId, start, end, ct)).Select(r => FromFund(r) with { FromFund = false }));
        }

        var spending = lines.Where(l => l.Parts.SpentCents != 0).ToList();
        var former = lines.Where(l => l.UserId is null).ToList();
        return new GroupMonthlyReportDto(
            year,
            month,
            Math.Max(0, spending.Sum(l => l.Parts.SpentCents)),
            lines.Sum(l => l.Parts.IncomeCents),
            ByCategory(spending),
            ByMember(lines),
            lines.Sum(l => l.Parts.RefundedCents),
            lines.Where(l => l.FromFund).Sum(l => l.Parts.SpentCents),
            former.Count == 0 ? null : new GroupFormerMembersDto(former.Sum(l => l.Parts.SpentCents), former.Where(l => l.FromFund).Sum(l => l.Parts.SpentCents)));
    }

    private static List<GroupCategorySpendDto> ByCategory(IEnumerable<Line> spending)
        => spending
            .GroupBy(l => (l.GroupCategoryId, l.CategoryId))
            .Select(g => new GroupCategorySpendDto(g.Key.CategoryId, g.Key.GroupCategoryId, g.First().CategoryName, g.Sum(l => l.Parts.SpentCents)))
            .Where(c => c.SpentCents > 0)
            .OrderByDescending(c => c.SpentCents)
            .ToList();

    /// <summary>S5: chỉ người thật; phần của người đã ẩn danh nằm ở <c>formerMembers</c>.</summary>
    private static List<GroupMemberTotalsDto> ByMember(IEnumerable<Line> lines)
        => lines
            .Where(l => l.UserId is not null)
            .GroupBy(l => l.UserId!.Value)
            .Select(g => new GroupMemberTotalsDto(
                g.Key,
                g.First().IsUserActive ? g.First().MemberName! : GroupMemberService.InactiveMemberName,
                Math.Max(0, g.Sum(l => l.Parts.SpentCents)),
                g.Sum(l => l.Parts.IncomeCents),
                g.Where(l => l.FromFund).Sum(l => l.Parts.SpentCents)))
            .OrderByDescending(m => m.SpentCents)
            .ToList();

    private static Line FromShared(GroupReportRow r)
        => new(
            r.UserId, r.MemberName, r.IsUserActive, r.GroupCategoryId, r.GroupCategoryId is null && r.CategoryIsSystem ? r.CategoryId : null,
            r.GroupCategoryName ?? (r.CategoryIsSystem ? r.CategoryName : null) ?? GroupTransactionService.UncategorizedName,
            NetFlow.Of(r.Type, r.Relation, r.IsReversed, r.AmountCents), FromFund: false);

    // Danh mục của khoản chi từ quỹ luôn là danh mục hệ thống hoặc danh mục nhóm (D10); danh mục nhóm thắng như giao dịch chia sẻ.
    private static Line FromFund(GroupFundExpenseRow r)
        => new(
            r.MemberUserId, r.MemberName, r.IsMemberActive, r.GroupCategoryId, r.GroupCategoryId is null ? r.CategoryId : null,
            r.GroupCategoryName ?? r.CategoryName ?? GroupTransactionService.UncategorizedName,
            new FlowParts(r.AmountCents, 0, 0, true), FromFund: true);
}
