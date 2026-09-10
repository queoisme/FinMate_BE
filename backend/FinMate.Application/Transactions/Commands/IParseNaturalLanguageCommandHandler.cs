using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

public interface IParseNaturalLanguageCommandHandler
{
    Task<ParsedTransactionDto> HandleAsync(ParseNaturalLanguageCommand command, CancellationToken ct = default);
}
