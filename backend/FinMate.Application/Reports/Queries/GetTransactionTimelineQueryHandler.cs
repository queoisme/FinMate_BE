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
            .Select(g => new TimelineDayDto(
                g.Key,
                // Giao dịch cho vay/mượn vẫn nằm trong danh sách ngày đó, chỉ không cộng vào tổng chi/thu của ngày. Hoàn/đảo theo NetFlow
                // (Phase 81): hoàn trừ vào chi, không vào thu; cặp đảo vẫn hiện trong danh sách nhưng không vào tổng.
                g.Where(t => t.Category?.Slug != Category.LoanSlug)
                    .Sum(t => NetFlow.Of(t.TransactionType, t.Relation, t.IsReversed, t.AmountCents).SpentCents),
                g.Where(t => t.Category?.Slug != Category.LoanSlug)
                    .Sum(t => NetFlow.Of(t.TransactionType, t.Relation, t.IsReversed, t.AmountCents).IncomeCents),
                g.Select(t => TransactionMapper.ToDto(t)).ToList()))
            .ToList();

        return new TimelineDto(days, result.NextCursor);
    }
}
