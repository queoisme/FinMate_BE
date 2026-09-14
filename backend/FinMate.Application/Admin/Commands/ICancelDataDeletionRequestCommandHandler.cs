using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface ICancelDataDeletionRequestCommandHandler
{
    Task<DataDeletionRequestDto> HandleAsync(
        CancelDataDeletionRequestCommand command, CancellationToken ct = default);
}
