using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Khoản định kỳ theo người dùng. Mọi hàm lọc theo <c>userId</c>: không có đường nào lấy một dòng chỉ bằng id.</summary>
public interface IRecurringItemRepository
{
    Task<RecurringItem?> GetAsync(Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>Không truyền trạng thái thì trả mọi khoản trừ đã hủy và đã từ chối.</summary>
    Task<IReadOnlyList<RecurringItem>> ListAsync(Guid userId, RecurringStatus? status, CancellationToken ct = default);

    /// <summary>Mọi khoản (mọi trạng thái), có theo dõi thay đổi — dùng cho job phát hiện và cập nhật.</summary>
    Task<IReadOnlyList<RecurringItem>> ListAllTrackedAsync(Guid userId, CancellationToken ct = default);

    Task<RecurringItem?> FindByCounterpartyAsync(Guid userId, RecurringDirection direction, string counterpartyKey, CancellationToken ct = default);
    Task AddAsync(RecurringItem item, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<RecurringItem> items, CancellationToken ct = default);
    Task UpdateAsync(RecurringItem item, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
