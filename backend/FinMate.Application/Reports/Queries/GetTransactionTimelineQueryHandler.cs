using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Reports.Queries;

public class GetTransactionTimelineQueryHandler : IGetTransactionTimelineQueryHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IReportRepository? _reports;   // null (test cũ): tổng ngày cộng từ trang như trước, không có dòng tiền

    public GetTransactionTimelineQueryHandler(ITransactionRepository transactionRepository, IReportRepository? reports = null)
    {
        _transactionRepository = transactionRepository;
        _reports = reports;
    }

    public async Task<TimelineDto> HandleAsync(GetTransactionTimelineQuery query, CancellationToken ct = default)
    {
        // Tái dùng nguyên cursor pagination của Phase 4 thay vì viết lần hai — timeline chỉ
        // khác list ở chỗ gom kết quả theo ngày.
        var result = await _transactionRepository.GetListAsync(
            new TransactionListFilter(
                query.UserId,
                FinancialAccountId: null,
                CategoryId: null,
                TransactionType: null,
                Status: null,
                query.FromDate,
                query.ToDate,
                query.Cursor,
                query.Limit),
            ct);

        var groups = result.Items.GroupBy(t => VietnamTime.DateOf(t.TransactedAt)).OrderByDescending(g => g.Key).ToList();
        if (_reports is null || groups.Count == 0)
        {
            return new TimelineDto(groups.Select(PageTotals).ToList(), result.NextCursor);
        }

        // Phase 90 (Q16): tổng của ngày lấy từ TẤT CẢ giao dịch đã xác nhận của ngày đó (không cộng từ trang: một ngày bị cắt ngang giữa hai
        // trang từng có tổng thiếu), cùng một quy tắc với báo cáo tháng; kèm dòng tiền. Chi của ngày không âm, phần bị chặn là refundSurplusCents.
        var points = (await _reports.GetDailySpendAsync(query.UserId, groups[^1].Key, groups[0].Key, ct, includeCashFlowOnlyDays: true)).ToDictionary(p => p.Date);
        var days = groups.Select(g =>
        {
            var (spent, income, _, _, cashFlow) = CashFlowCalculator.Period(points.TryGetValue(g.Key, out var p) ? [p] : []);
            return new TimelineDayDto(g.Key, spent, income, g.Select(t => TransactionMapper.ToDto(t)).ToList(), cashFlow);
        }).ToList();
        return new TimelineDto(days, result.NextCursor);
    }

    /// <summary>Đường cũ, khi không có repository báo cáo: cộng từ giao dịch trên trang, bỏ cho vay/mượn, hoàn/đảo theo NetFlow.</summary>
    private static TimelineDayDto PageTotals(IGrouping<DateOnly, Transaction> g)
        => new(
            g.Key,
            g.Where(t => !LoanExclusion.IsLoan(t.Category)).Sum(t => NetFlow.Of(t.TransactionType, t.Relation, t.IsReversed, t.AmountCents).SpentCents),
            g.Where(t => !LoanExclusion.IsLoan(t.Category)).Sum(t => NetFlow.Of(t.TransactionType, t.Relation, t.IsReversed, t.AmountCents).IncomeCents),
            g.Select(t => TransactionMapper.ToDto(t)).ToList());
}
