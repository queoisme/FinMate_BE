using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

// Tái dùng POST /api/v1/analyze thay vì thêm route AI Service mới (đã hỏi user, xem plan
// "NLP parse contract" — approved 2026-09-10): coi câu người dùng gõ như notification_body,
// package_name="manual_entry" để AI Service phân biệt nguồn. Không persist gì — chỉ trả về
// để client prefill form CreateManualTransactionCommand.
public class ParseNaturalLanguageCommandHandler : IParseNaturalLanguageCommandHandler
{
    private const string ManualEntryPackageName = "manual_entry";

    private readonly IAIServiceClient _aiServiceClient;
    private readonly IValidator<ParseNaturalLanguageCommand> _validator;

    public ParseNaturalLanguageCommandHandler(IAIServiceClient aiServiceClient, IValidator<ParseNaturalLanguageCommand> validator)
    {
        _aiServiceClient = aiServiceClient;
        _validator = validator;
    }

    public async Task<ParsedTransactionDto> HandleAsync(ParseNaturalLanguageCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var response = await _aiServiceClient.AnalyzeAsync(
            new AnalyzeRequest(Guid.NewGuid(), command.UserId, ManualEntryPackageName, null, command.Text, DateTimeOffset.UtcNow),
            ct);

        return new ParsedTransactionDto(
            response.Extraction?.AmountCents,
            response.Extraction?.TransactionType,
            response.Extraction?.MerchantName,
            response.Extraction?.Description,
            response.Extraction?.TransactedAt,
            response.Categorization?.CategorySlug);
    }
}
