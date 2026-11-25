using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Groups.Bills;

/// <summary>
/// Số liệu thẻ tổng của màn Nhóm, cộng qua mọi nhóm người dùng đang tham gia.
/// <c>TotalSpentCents</c>: tổng chi của các nhóm trong tháng. <c>MyShareDueCents</c>: tổng phần của CHÍNH MÌNH trong các hóa đơn chia của tháng.
/// <c>MyPayableCents</c>: tổng số mình đang nợ lúc này (cộng dồn, không theo tháng); null khi chưa nhóm nào có hóa đơn hay khoản trả nợ.
/// <c>ClosingDate</c>: ngày chốt sổ gần nhất sắp tới trong các nhóm (giờ Việt Nam); null khi không có nhóm.
/// </summary>
public sealed record MyGroupSummaryDto(
    int Year,
    int Month,
    long TotalSpentCents,
    int ActiveGroupCount,
    long MyShareDueCents,
    long? MyPayableCents,
    DateOnly? ClosingDate);

public interface IGroupSummaryService
{
    Task<MyGroupSummaryDto> GetAsync(Guid userId, int? year, int? month, CancellationToken ct = default);
}

public class GroupSummaryService : IGroupSummaryService
{
    private readonly IGroupRepository _groups;
    private readonly IGroupBillRepository _bills;
    private readonly IGroupReportService _reports;

    public GroupSummaryService(IGroupRepository groups, IGroupBillRepository bills, IGroupReportService reports)
    {
        _groups = groups;
        _bills = bills;
        _reports = reports;
    }

    public async Task<MyGroupSummaryDto> GetAsync(Guid userId, int? year, int? month, CancellationToken ct = default)
    {
        var (currentYear, currentMonth) = VietnamTime.YearMonthOf(DateTimeOffset.UtcNow);
        var y = year ?? currentYear;
        var m = month ?? currentMonth;
        var (start, end) = VietnamTime.MonthRange(y, m);
        var today = VietnamTime.Today();

        long spent = 0, share = 0, payable = 0;
        var usesDebt = false;
        DateOnly? closing = null;
        var memberships = await _groups.ListForUserAsync(userId, ct);

        foreach (var g in memberships)
        {
            spent += (await _reports.GetMonthlyAsync(g.Group.Id, userId, y, m, ct)).TotalSpentCents;
            share += await _bills.SumUserShareAsync(g.Group.Id, userId, start, end, ct);

            var rows = await _bills.LoadDebtRowsAsync(g.Group.Id, ct);
            if (rows.Bills.Count > 0 || rows.Settlements.Count > 0)
            {
                usesDebt = true;
                payable += Math.Max(0, -GroupDebtMath.Net(rows.Bills, rows.Settlements).GetValueOrDefault(userId));
            }

            var next = NextClosing(g.Group.ClosingDay, today);
            closing = closing is null || next < closing ? next : closing;
        }

        return new MyGroupSummaryDto(y, m, spent, memberships.Count, share, usesDebt ? payable : null, closing);
    }

    /// <summary>Ngày chốt sổ gần nhất không trước hôm nay: ngày <paramref name="closingDay"/> của tháng này, hoặc cuối tháng khi null; qua rồi thì sang tháng sau.</summary>
    internal static DateOnly NextClosing(int? closingDay, DateOnly today)
    {
        var thisMonth = Pick(today.Year, today.Month, closingDay);
        if (thisMonth >= today)
        {
            return thisMonth;
        }

        var next = today.AddMonths(1);
        return Pick(next.Year, next.Month, closingDay);
    }

    private static DateOnly Pick(int year, int month, int? closingDay)
        => new(year, month, Math.Min(closingDay ?? DateTime.DaysInMonth(year, month), DateTime.DaysInMonth(year, month)));
}
