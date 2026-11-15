using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions;

/// <summary>Một vế số dư: ví nào đổi bao nhiêu (dương là tiền vào ví).</summary>
public readonly record struct WalletLeg(Guid AccountId, long DeltaCents);

/// <summary>Tiền của một giao dịch đi ra, đi vào ví người dùng, hay chỉ chuyển giữa hai ví của họ.</summary>
public enum MoneyDirection
{
    Out,
    In,
    Move,
}

/// <summary>
/// MỘT chỗ duy nhất quyết định một giao dịch đã xác nhận đổi số dư ví nào và bao nhiêu (Phase 85, S6): Debit −, Credit +, Transfer thường
/// nguồn − / đích +, Transfer gắn quỹ nhóm (không ví đích) theo <see cref="Transaction.GroupFundDirection"/>. Đây là nơi duy nhất đọc hướng đó:
/// mã cũ hiểu mọi Transfer là "tiền rời ví nguồn", nên một khoản rút từ quỹ (tiền VÀO ví) đọc theo cách cũ sẽ sai dấu mà không báo lỗi.
/// </summary>
public static class TransferLegs
{
    public static IReadOnlyList<WalletLeg> Of(Transaction transaction)
        => Of(transaction.TransactionType, transaction.FinancialAccountId, transaction.CounterAccountId, transaction.AmountCents, transaction.GroupFundDirection);

    public static IReadOnlyList<WalletLeg> Of(
        TransactionType type, Guid accountId, Guid? counterAccountId, long amountCents, GroupFundDirection? fundDirection = null)
        => type switch
        {
            TransactionType.Credit => [new WalletLeg(accountId, amountCents)],
            TransactionType.Transfer when fundDirection == GroupFundDirection.In => [new WalletLeg(accountId, amountCents)],
            TransactionType.Transfer when counterAccountId is { } counter => [new WalletLeg(accountId, -amountCents), new WalletLeg(counter, amountCents)],
            _ => [new WalletLeg(accountId, -amountCents)],   // Debit, và Transfer gắn quỹ chiều ra
        };

    /// <summary>Hướng tiền so với tập ví của người dùng. Transfer thường là <see cref="MoneyDirection.Move"/>.</summary>
    public static MoneyDirection DirectionOf(Transaction transaction)
        => DirectionOf(transaction.TransactionType, transaction.CounterAccountId, transaction.GroupFundDirection);

    public static MoneyDirection DirectionOf(TransactionType type, Guid? counterAccountId, GroupFundDirection? fundDirection)
        => type switch
        {
            TransactionType.Credit => MoneyDirection.In,
            TransactionType.Transfer when counterAccountId is not null => MoneyDirection.Move,
            TransactionType.Transfer when fundDirection == GroupFundDirection.In => MoneyDirection.In,
            _ => MoneyDirection.Out,
        };
}
