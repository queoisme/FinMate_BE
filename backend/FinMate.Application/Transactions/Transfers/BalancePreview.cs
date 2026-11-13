using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Transfers;

/// <summary>Số dư trước/sau từng ví cho màn xem trước. Đọc SAU khi rollback, nên số trong DB là số "trước".</summary>
public static class BalancePreview
{
    public static async Task<IReadOnlyList<BalanceChangeDto>> ChangesAsync(
        IFinancialAccountRepository accounts, Guid userId, IReadOnlyDictionary<Guid, long> deltas, CancellationToken ct)
    {
        var changes = new List<BalanceChangeDto>();
        foreach (var (accountId, delta) in deltas.Where(d => d.Value != 0))
        {
            if (await accounts.GetByIdAsync(accountId, userId, ct) is { } account)
            {
                changes.Add(new BalanceChangeDto(account.Id, account.AccountName, account.BalanceCents, account.BalanceCents + delta));
            }
        }

        return changes;
    }
}
