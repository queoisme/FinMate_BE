using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups.Funds;

/// <summary>
/// Phase 85, BR-FUND-09 nửa sau, D7, D8: chủ giao dịch xoá giao dịch cá nhân gắn quỹ thì dòng quỹ cũng bị xoá, trong cùng lượt lưu của lệnh xoá. Ví do
/// lệnh xoá hoàn qua <c>TransferLegs</c>. Không cần cờ quỹ: tắt cờ không được làm số của nhóm lệch (D8).
/// </summary>
public interface IGroupFundLinkSync
{
    /// <summary>Gọi TRƯỚC khi lệnh xoá lưu. Chỉ đổi dữ liệu đang theo dõi. Trả cảnh báo ngân sách nhóm để gửi sau khi lưu.</summary>
    Task<IReadOnlyList<GroupBudgetAlert>> OnPersonalDeletedAsync(Guid userId, Transaction personal, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Gửi cảnh báo ngân sách nhóm và, nếu quỹ vừa âm, push BR-FUND-16. Không ném.</summary>
    Task AfterSaveAsync(IReadOnlyList<GroupBudgetAlert> alerts, CancellationToken ct = default);
}

public class GroupFundLinkSync : IGroupFundLinkSync
{
    private readonly IGroupFundRepository _funds;
    private readonly IGroupRepository _groups;
    private readonly IGroupLedger _ledger;
    private readonly IGroupFundNotifier _notifier;
    private readonly List<GroupFund> _wentNegative = [];

    public GroupFundLinkSync(IGroupFundRepository funds, IGroupRepository groups, IGroupLedger ledger, IGroupFundNotifier notifier)
    {
        _funds = funds;
        _groups = groups;
        _ledger = ledger;
        _notifier = notifier;
    }

    public async Task<IReadOnlyList<GroupBudgetAlert>> OnPersonalDeletedAsync(Guid userId, Transaction personal, DateTimeOffset now, CancellationToken ct = default)
    {
        _wentNegative.Clear();   // lệnh xoá có thể được chạy lại sau xung đột: chỉ giữ kết quả của lượt cuối

        if (personal.GroupFundEntryId is not { } entryId || await _funds.GetEntryByIdAsync(entryId, ct) is not { DeletedAt: null } entry)
        {
            return [];
        }

        // D7: người đã rời nhóm (hoặc nhóm đã xoá, quỹ đã lưu trữ) không còn đổi được sổ quỹ. Giao dịch cá nhân vẫn xoá được và ví vẫn được hoàn;
        // dòng quỹ ở lại, số dư quỹ không đổi.
        var fund = await _funds.GetAsync(entry.GroupId, entry.FundId, ct);
        var group = await _groups.GetByIdAsync(entry.GroupId, ct);
        var member = group is null ? null : await _groups.GetActiveMemberAsync(entry.GroupId, userId, ct);
        if (fund is null || fund.IsArchived || member is null || entry.MemberUserId != userId)
        {
            return [];
        }

        var touch = GroupScopeTouch.Of(entry);
        if (GroupFundMath.Apply(fund, -GroupFundMath.Signed(entry), now))
        {
            _wentNegative.Add(fund);
        }

        entry.DeletedAt = now;
        entry.DeletedByUserId = userId;
        entry.UpdatedAt = now;
        return touch is null ? [] : await _ledger.RecomputeAsync([], [entry], [touch], ct);
    }

    public async Task AfterSaveAsync(IReadOnlyList<GroupBudgetAlert> alerts, CancellationToken ct = default)
    {
        await _ledger.SendAlertsAsync(alerts, ct);
        foreach (var fund in _wentNegative)
        {
            await _notifier.FundWentNegativeAsync(fund, ct);
        }

        _wentNegative.Clear();
    }
}
