namespace FinMate.Application.Reports.Commands;

public interface IMarkInsightReadCommandHandler
{
    Task HandleAsync(MarkInsightReadCommand command, CancellationToken ct = default);
}
