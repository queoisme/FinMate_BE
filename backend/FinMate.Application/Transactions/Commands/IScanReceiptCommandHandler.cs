using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

public interface IScanReceiptCommandHandler
{
    Task<ScannedReceiptDto> HandleAsync(ScanReceiptCommand command, CancellationToken ct = default);
}
