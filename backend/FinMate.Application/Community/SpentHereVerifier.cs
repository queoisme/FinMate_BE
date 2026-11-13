using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Community;

public interface ISpentHereVerifier
{
    /// <summary>Không đạt thì trả false chứ không báo lỗi: nhãn là phần thưởng, thiếu nhãn không làm hỏng việc chia sẻ. Không đọc số tiền và không lưu lại mã giao dịch.</summary>
    Task<bool> IsVerifiedAsync(Guid userId, Guid? transactionId, string placeNameKey, int lookbackDays, CancellationToken ct = default);
}

public sealed class SpentHereVerifier : ISpentHereVerifier
{
    private const int MinPartialMatchLength = 4;

    private readonly ITransactionRepository _transactions;

    public SpentHereVerifier(ITransactionRepository transactions)
    {
        _transactions = transactions;
    }

    public async Task<bool> IsVerifiedAsync(Guid userId, Guid? transactionId, string placeNameKey, int lookbackDays, CancellationToken ct = default)
    {
        if (transactionId is not { } id)
        {
            return false;
        }

        var transaction = await _transactions.GetByIdAsync(id, userId, ct);
        return transaction is not null
            && transaction.TransactionType == TransactionType.Debit
            && transaction.Status == TransactionStatus.Confirmed
            && !transaction.IsReversed   // giao dịch lỗi đã bị đảo thì không phải "đã chi ở đây" (Phase 81)
            && transaction.Category?.Slug != Category.LoanSlug
            && transaction.TransactedAt >= DateTimeOffset.UtcNow.AddDays(-lookbackDays)
            && NamesMatch(CounterpartyKey.From(transaction.MerchantName), placeNameKey);
    }

    /// <summary>Ngân hàng hay viết dài hơn tên quán ("HIGHLANDS COFFEE Q7"), nên chấp nhận chứa nhau khi phần chung đủ dài.</summary>
    private static bool NamesMatch(string? merchantKey, string placeKey)
    {
        if (merchantKey is null)
        {
            return false;
        }

        if (merchantKey == placeKey)
        {
            return true;
        }

        var shorter = merchantKey.Length < placeKey.Length ? merchantKey : placeKey;
        var longer = merchantKey.Length < placeKey.Length ? placeKey : merchantKey;
        return shorter.Length >= MinPartialMatchLength && $" {longer} ".Contains($" {shorter} ", StringComparison.Ordinal);
    }
}
