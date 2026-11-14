using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Quỹ nhóm và dòng quỹ (Phase 85). Không kiểm quyền: mọi lời gọi phải đi sau <c>IGroupAccessService</c>. Lưu qua <c>IGroupRepository.SaveChangesAsync</c>.</summary>
public interface IGroupFundRepository
{
    /// <summary>Quỹ chưa lưu trữ trước, rồi theo thời điểm tạo.</summary>
    Task<List<GroupFund>> ListAsync(Guid groupId, CancellationToken ct = default);

    Task<GroupFund?> GetAsync(Guid groupId, Guid fundId, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid groupId, CancellationToken ct = default);

    void Add(GroupFund fund);
}
