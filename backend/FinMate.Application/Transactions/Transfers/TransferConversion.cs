using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Transfers;

/// <summary>Một chiều sắp chuyển đổi, đủ để tính kế hoạch.</summary>
public sealed record ConversionSide(Guid Id, TransactionType Type, TransactionStatus Status, long AmountCents, Guid AccountId, DateTimeOffset CreatedAt);

/// <summary>
/// Kết quả chuyển đổi: giao dịch nào thành Transfer, giao dịch nào bị gộp, Transfer đi từ đâu tới đâu, khoản phí, và số phải CỘNG vào số dư
/// từng ví (đã trừ phần các chiều đã xác nhận đang tác động).
/// </summary>
public sealed record TransferPlan(
    Guid SurvivorId,
    Guid? MergedId,
    Guid FromAccountId,
    Guid ToAccountId,
    long AmountCents,
    long FeeCents,
    IReadOnlyDictionary<Guid, long> BalanceDeltas);

/// <summary>
/// Kế hoạch đổi một hoặc hai giao dịch thành Transfer (Phase 83). Hàm thuần, giữ bất biến BR-TRF-07: sau chuyển đổi, số dư mỗi ví bằng đúng số dư
/// nếu các thông báo được xác nhận như chi/thu thường, bất kể trước đó mỗi chiều là nháp hay đã xác nhận. Cách tính: hoàn tác phần các chiều đã xác
/// nhận đang tác động, rồi áp Transfer (số tiền = tiền vào) cộng khoản phí (tiền ra − tiền vào) ở ví nguồn.
/// </summary>
public static class TransferConversion
{
    /// <summary>BR-TRF-08: ghép một khoản chi và một khoản thu.</summary>
    public static TransferPlan Pair(ConversionSide debit, ConversionSide credit)
    {
        if (debit.Type != TransactionType.Debit || credit.Type != TransactionType.Credit)
        {
            throw new ArgumentException("Ghép cặp cần đúng một khoản chi và một khoản thu.");
        }

        var survivor = Survivor(debit, credit);
        var fee = debit.AmountCents - credit.AmountCents;
        var target = new Dictionary<Guid, long>
        {
            [debit.AccountId] = -credit.AmountCents - fee,
            [credit.AccountId] = credit.AmountCents,
        };
        return new TransferPlan(
            survivor.Id,
            survivor == debit ? credit.Id : debit.Id,
            debit.AccountId,
            credit.AccountId,
            credit.AmountCents,
            fee,
            Deltas(target, debit, credit));
    }

    /// <summary>BR-TRF-09: một chiều chuyển tới / từ ví người dùng chọn; không có phí.</summary>
    public static TransferPlan OneSided(ConversionSide side, Guid counterAccountId)
    {
        var outgoing = side.Type == TransactionType.Debit;
        var from = outgoing ? side.AccountId : counterAccountId;
        var to = outgoing ? counterAccountId : side.AccountId;
        var target = new Dictionary<Guid, long> { [from] = -side.AmountCents, [to] = side.AmountCents };
        return new TransferPlan(side.Id, null, from, to, side.AmountCents, 0, Deltas(target, side));
    }

    /// <summary>Đúng một bản đã xác nhận → giữ nó; cả hai đã xác nhận → bản tạo trước (BR-DUP-09); cả hai là nháp → chiều tiền ra.</summary>
    private static ConversionSide Survivor(ConversionSide debit, ConversionSide credit)
    {
        var debitConfirmed = debit.Status == TransactionStatus.Confirmed;
        var creditConfirmed = credit.Status == TransactionStatus.Confirmed;
        if (debitConfirmed != creditConfirmed)
        {
            return debitConfirmed ? debit : credit;
        }

        if (!debitConfirmed)
        {
            return debit;
        }

        return debit.CreatedAt != credit.CreatedAt
            ? (debit.CreatedAt < credit.CreatedAt ? debit : credit)
            : (debit.Id.CompareTo(credit.Id) <= 0 ? debit : credit);
    }

    private static Dictionary<Guid, long> Deltas(Dictionary<Guid, long> target, params ConversionSide[] sides)
    {
        var deltas = new Dictionary<Guid, long>(target);
        foreach (var side in sides.Where(s => s.Status == TransactionStatus.Confirmed))
        {
            var applied = side.Type == TransactionType.Credit ? side.AmountCents : -side.AmountCents;
            deltas[side.AccountId] = deltas.GetValueOrDefault(side.AccountId) - applied;
        }

        return deltas;
    }
}
