using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

public interface IChatCommandHandler
{
    Task<ChatResultDto> HandleAsync(ChatCommand command, CancellationToken ct = default);
}
