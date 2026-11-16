using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups.Funds;

/// <summary>Quy tắc số của quỹ nhóm (Phase 85). Thuần: một chỗ quyết định dấu của từng loại dòng và khi nào quỹ "vừa âm".</summary>
public static class GroupFundMath
{
    /// <summary>Phần một dòng đổi số dư quỹ (BR-FUND-02, D3): góp +, chi −, rút −, điều chỉnh theo dấu của chính nó. Dòng đã xoá là 0.</summary>
    public static long Signed(GroupFundEntry entry)
        => entry.DeletedAt is not null ? 0 : Signed(entry.Kind, entry.AmountCents);

    public static long Signed(GroupFundEntryKind kind, long amountCents) => kind switch
    {
        GroupFundEntryKind.Contribution => amountCents,
        GroupFundEntryKind.Adjustment => amountCents,
        _ => -amountCents,
    };

    /// <summary>Đổi số dư quỹ và tăng đợt âm khi vừa chuyển từ ≥ 0 sang &lt; 0 (D18). Trả true khi vừa âm.</summary>
    public static bool Apply(GroupFund fund, long deltaCents, DateTimeOffset now)
    {
        var before = fund.BalanceCents;
        fund.BalanceCents += deltaCents;
        fund.UpdatedAt = now;
        if (before >= 0 && fund.BalanceCents < 0)
        {
            fund.NegativeEpisode++;
            return true;
        }

        return false;
    }

    /// <summary>Hướng tiền ở ví cá nhân của một dòng có kèm ví (BR-FUND-07): góp và chi là tiền ra, rút là tiền vào. Điều chỉnh không kèm ví.</summary>
    public static GroupFundDirection? DirectionOf(GroupFundEntryKind kind) => kind switch
    {
        GroupFundEntryKind.Contribution or GroupFundEntryKind.Expense => GroupFundDirection.Out,
        GroupFundEntryKind.Withdrawal => GroupFundDirection.In,
        _ => null,
    };

    /// <summary>Có được kèm ví cá nhân không.</summary>
    public static bool AcceptsWallet(GroupFundEntryKind kind) => DirectionOf(kind) is not null;
}
