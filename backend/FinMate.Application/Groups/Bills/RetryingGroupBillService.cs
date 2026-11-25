using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Groups.Bills;

/// <summary>Chạy lại khi hai hóa đơn cùng chạm một kỳ ngân sách nhóm (<c>xmin</c> lệch): lượt thua đọc lại số mới và tính lại.</summary>
public sealed class RetryingGroupBillService(GroupBillService inner, IConcurrencyRetry retry) : IGroupBillService
{
    public Task<GroupBillDto> CreateAsync(CreateGroupBillCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.CreateAsync(command, token), ct);

    public Task<GroupBillListResult> ListAsync(Guid groupId, Guid userId, string? cursor, int limit, CancellationToken ct = default)
        => inner.ListAsync(groupId, userId, cursor, limit, ct);

    public Task<GroupBillDto> GetAsync(Guid groupId, Guid userId, Guid billId, CancellationToken ct = default)
        => inner.GetAsync(groupId, userId, billId, ct);
}
