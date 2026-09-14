using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <param name="Email">Của người dùng đã soft delete — nạp kèm bằng join bỏ query filter.</param>
public record DataDeletionRequestRow(
    DataDeletionRequest Request,
    string Email,
    string DisplayName);

public record DataDeletionRequestListFilter(
    DataDeletionStatus? Status,
    string? Cursor,
    int Limit);

public record DataDeletionRequestListResult(
    IReadOnlyList<DataDeletionRequestRow> Items,
    string? NextCursor);

public interface IDataDeletionRequestRepository
{
    Task AddAsync(DataDeletionRequest request, CancellationToken ct = default);
    Task<List<DataDeletionRequest>> GetDueAsync(DateTimeOffset asOf, CancellationToken ct = default);
    Task MarkProcessedAsync(DataDeletionRequest request, CancellationToken ct = default);

    Task<DataDeletionRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Hàng đợi cho màn hình giám sát. Trả kèm email/tên vì nếu không, danh sách chỉ là một
    /// đống GUID và admin không đối chiếu nổi với người vừa gọi lên hỗ trợ.
    /// </summary>
    Task<DataDeletionRequestListResult> GetPageAsync(
        DataDeletionRequestListFilter filter, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
