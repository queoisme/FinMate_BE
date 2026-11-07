using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Khoản định kỳ đã xác nhận của người dùng. MỌI truy vấn lọc theo <c>userId</c>: không có cách lấy hay xóa một khoản chỉ bằng id.</summary>
public interface IRecurringItemRepository
{
    Task<List<RecurringItem>> GetListByUserAsync(Guid userId, CancellationToken ct = default);

    Task<int> CountByUserAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(RecurringItem item, CancellationToken ct = default);

    /// <returns>false nếu không có khoản nào của người dùng này với id đó (kể cả khi id thuộc người khác).</returns>
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default);
}
