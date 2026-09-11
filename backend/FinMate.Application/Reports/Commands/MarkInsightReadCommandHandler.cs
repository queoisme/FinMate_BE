using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Reports.Commands;

public class MarkInsightReadCommandHandler : IMarkInsightReadCommandHandler
{
    private readonly IReportRepository _reportRepository;

    public MarkInsightReadCommandHandler(IReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    public async Task HandleAsync(MarkInsightReadCommand command, CancellationToken ct = default)
    {
        var insight = await _reportRepository.GetInsightAsync(command.InsightId, command.UserId, ct)
            ?? throw new NotFoundException("SpendingInsight", command.InsightId);

        if (insight.IsRead)
        {
            return;
        }

        insight.IsRead = true;
        await _reportRepository.UpdateInsightAsync(insight, ct);
    }
}
