using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Entities;

namespace FinMate.Application.Transactions;

/// <summary>
/// Phase 85 (D9): giao dịch gắn quỹ nhóm chỉ đổi qua dòng quỹ của nó. Sửa, chuyển ví, làm bên thua khi gộp, hoàn/đảo, chia sẻ nhóm qua đường giao dịch
/// cá nhân đều trả 422 <c>TRANSACTION_GROUP_FUND_LINKED</c>. Xoá thì được (D7), và làm bên thắng khi gộp trùng thì được (D11).
/// </summary>
public static class GroupFundGuard
{
    public static void EnsureNotLinked(Transaction? transaction)
    {
        if (transaction?.GroupFundEntryId is not null)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.GroupFundLinked, "Giao dịch này ghi lại một dòng quỹ nhóm. Hãy sửa ở dòng quỹ trong nhóm.");
        }
    }
}
