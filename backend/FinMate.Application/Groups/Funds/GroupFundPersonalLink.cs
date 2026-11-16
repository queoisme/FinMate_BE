using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups.Funds;

/// <summary>
/// Giao dịch cá nhân ghi lại một dòng quỹ ở sổ của người ghi (Phase 85, BR-FUND-07/08): Transfer không ví đích, gắn dòng quỹ, đã xác nhận, không
/// danh mục, không chia sẻ nhóm. Không là chi, không là thu ở bất cứ đâu, nhờ chính loại Transfer. Số dư ví đổi qua <see cref="TransferLegs"/>.
/// </summary>
public static class GroupFundPersonalLink
{
    public static Transaction Build(GroupFundEntry entry, FinancialAccount account, DateTimeOffset now)
    {
        var direction = GroupFundMath.DirectionOf(entry.Kind)
            ?? throw new InvalidOperationException("Loại dòng quỹ này không kèm ví.");
        return new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = account.UserId,
            FinancialAccountId = account.Id,
            AmountCents = entry.AmountCents,
            TransactionType = TransactionType.Transfer,
            Source = TransactionSource.Manual,
            Status = TransactionStatus.Confirmed,
            Description = entry.Note,
            TransactedAt = entry.TransactedAt,
            GroupFundEntryId = entry.Id,
            GroupFundDirection = direction,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Áp (<paramref name="sign"/> = +1) hoặc hoàn (−1) tác động của giao dịch lên ví đã nạp. Ví khác ví của giao dịch thì bỏ qua.</summary>
    public static void ApplyBalance(Transaction transaction, FinancialAccount account, int sign, DateTimeOffset now)
    {
        foreach (var leg in TransferLegs.Of(transaction).Where(l => l.AccountId == account.Id))
        {
            account.BalanceCents += sign * leg.DeltaCents;
            account.UpdatedAt = now;
        }
    }
}
