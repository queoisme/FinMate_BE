using FinMate.Application.Common;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Transfers;

/// <summary>
/// Khoản phí của một lần chuyển giữa các ví (Phase 83, BR-TRF-08, D13): khoản chi riêng ở ví nguồn, danh mục hệ thống <c>other</c>, đã xác nhận,
/// mô tả "Phí giao dịch", khoá idempotent = UUID v5(id Transfer, "fee"). Mang số dư ngân hàng báo của chiều tiền ra (Transfer khi đó không mang).
/// </summary>
public static class TransferFee
{
    public const string Description = "Phí giao dịch";
    public const string CategorySlug = "other";
    private const string KeyName = "fee";

    public static Guid ClientRequestIdFor(Guid transferId) => DeterministicGuid.V5(transferId, KeyName);

    public static Transaction Build(Guid userId, Guid transferId, Transaction bankDebit, Guid fromAccountId, long feeCents, Category? other, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ClientRequestId = ClientRequestIdFor(transferId),
            FinancialAccountId = fromAccountId,
            CategoryId = other?.Id,
            Category = other,
            AmountCents = feeCents,
            TransactionType = TransactionType.Debit,
            Source = bankDebit.Source,
            Status = TransactionStatus.Confirmed,
            Description = Description,
            TransactedAt = bankDebit.TransactedAt,
            BalanceAfterCents = bankDebit.BalanceAfterCents,
            CreatedAt = now,
            UpdatedAt = now,
        };
}
