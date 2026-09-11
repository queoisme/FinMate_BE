using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public class GetSpendingInsightsQueryHandler : IGetSpendingInsightsQueryHandler
{
    private readonly IReportRepository _reportRepository;

    public GetSpendingInsightsQueryHandler(IReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    public async Task<IReadOnlyList<SpendingInsightDto>> HandleAsync(
        GetSpendingInsightsQuery query,
        CancellationToken ct = default)
    {
        var insights = await _reportRepository.GetInsightsAsync(query.UserId, query.UnreadOnly, query.Limit, ct);
        return insights.Select(InsightMapper.ToDto).ToList();
    }
}
