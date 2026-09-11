using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public interface IGetTransactionTimelineQueryHandler
{
    Task<TimelineDto> HandleAsync(GetTransactionTimelineQuery query, CancellationToken ct = default);
}
