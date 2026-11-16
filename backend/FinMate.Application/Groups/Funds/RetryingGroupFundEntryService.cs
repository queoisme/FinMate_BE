using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Groups.Funds;

/// <summary>
/// Chạy lại khi hai người ghi cùng quỹ (hoặc cùng ví, cùng kỳ ngân sách) đồng thời: <c>xmin</c> lệch thì lượt thua đọc lại số mới và tính lại.
/// Gửi lại cùng <c>clientRequestId</c> đồng thời cũng đi qua đây: lượt chạy lại tìm thấy dòng của lượt thắng.
/// </summary>
public sealed class RetryingGroupFundEntryService(GroupFundEntryService inner, IConcurrencyRetry retry) : IGroupFundEntryService
{
    public Task<GroupFundEntryListResult> ListAsync(Guid groupId, Guid userId, GroupFundEntryQuery query, CancellationToken ct = default)
        => inner.ListAsync(groupId, userId, query, ct);

    public Task<GroupFundEntryResult> CreateAsync(CreateGroupFundEntryCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.CreateAsync(command, token), ct);

    public Task<GroupFundEntryResult> UpdateAsync(UpdateGroupFundEntryCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.UpdateAsync(command, token), ct);

    public Task<Common.Models.TransactionDto> RestoreFromFundAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.RestoreFromFundAsync(userId, transactionId, token), ct);

    public Task<GroupFundDto> DeleteAsync(Guid groupId, Guid userId, Guid fundId, Guid entryId, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.DeleteAsync(groupId, userId, fundId, entryId, token), ct);
}
