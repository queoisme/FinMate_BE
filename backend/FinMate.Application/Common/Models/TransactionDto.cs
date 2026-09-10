namespace FinMate.Application.Common.Models;

public record TransactionDto(
    Guid Id,
    Guid FinancialAccountId,
    Guid? CategoryId,
    string? CategoryName,
    long AmountCents,
    string TransactionType,
    string Source,
    string Status,
    string? MerchantName,
    string? Description,
    DateTimeOffset TransactedAt,
    long? BalanceAfterCents,
    DateTimeOffset CreatedAt);

public record TransactionListDto(IReadOnlyList<TransactionDto> Items, string? NextCursor);

public record ParsedTransactionDto(
    long? AmountCents,
    string? TransactionType,
    string? MerchantName,
    string? Description,
    DateTimeOffset? TransactedAt,
    string? CategorySlug);
