using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <param name="RequestedByName">Null khi người yêu cầu đã xóa cứng tài khoản.</param>
public sealed record GroupApprovalView(GroupApproval Approval, string? RequestedByName);

/// <summary>Quyền được kiểm ở service trước khi gọi vào đây.</summary>
public interface IGroupApprovalRepository
{
    void Add(GroupApproval approval);
    Task<GroupApproval?> GetAsync(Guid groupId, Guid approvalId, CancellationToken ct = default);
    Task<GroupApproval?> GetByClientRequestAsync(Guid groupId, Guid requestedByUserId, Guid clientRequestId, CancellationToken ct = default);

    /// <param name="status">Null = mọi trạng thái.</param>
    /// <param name="onlyUserId">Khác null thì chỉ yêu cầu của người đó.</param>
    Task<List<GroupApprovalView>> ListAsync(Guid groupId, GroupApprovalStatus? status, Guid? onlyUserId, int limit, CancellationToken ct = default);

    Task<string?> GetDisplayNameAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
