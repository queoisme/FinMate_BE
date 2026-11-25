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

    public GetTransactionTimelineQueryHandler(ITransactionRepository transactionRepository)
    {
        _transactionRepository = transactionRepository;
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

        var days = result.Items
            .GroupBy(t => VietnamTime.DateOf(t.TransactedAt))
            .OrderByDescending(g => g.Key)
            .Select(PageDay)
            .ToList();
        return new TimelineDto(days, result.NextCursor);
    }

    /// <summary>
    /// Tổng của một ngày tính từ các giao dịch ĐÃ XÁC NHẬN của ngày đó TRÊN TRANG NÀY. Một ngày bị cắt giữa hai trang xuất hiện ở cả hai, mỗi bản
    /// mang phần của mình, nên cộng mọi trang lại ra đúng tổng thật: app (tab Ngày/Tuần/Năm, <c>getPeriodSummary</c>) cộng như vậy, đừng đổi
    /// sang tổng cả ngày (sẽ cộng trùng). Cùng bộ tính với báo cáo tháng (Phase 90, Q16): bỏ cho vay/mượn khỏi chi/thu, kèm dòng tiền; chi của phần
    /// ngày không âm, phần bị chặn là <c>refundSurplusCents</c>, nên bất biến đúng cho từng phần. Danh sách giao dịch vẫn gồm cả nháp.
    /// </summary>
    private static TimelineDayDto PageDay(IGrouping<DateOnly, Transaction> g)
    {
        var points = CashFlowCalculator.ByDay(g.Where(t => t.Status == TransactionStatus.Confirmed).Select(CashFlowRow.From), accountId: null);
        var (spent, income, _, _, cashFlow) = CashFlowCalculator.Period(points);
        return new TimelineDayDto(g.Key, spent, income, g.Select(t => TransactionMapper.ToDto(t)).ToList(), cashFlow);
    }
}
