namespace FinMate.Application.Transactions.Commands;

public record ParseNaturalLanguageCommand(Guid UserId, string Text);
