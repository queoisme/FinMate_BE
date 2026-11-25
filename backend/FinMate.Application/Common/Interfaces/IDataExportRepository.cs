using FinMate.Application.DataExport;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Bộ lọc giao dịch khi xuất. Khoảng thời gian là [<c>Start</c>, <c>End</c>), null = không giới hạn (gói đầy đủ).</summary>
public sealed record TransactionExportFilter(
    DateTimeOffset? Start,
    DateTimeOffset? End,
    Guid? AccountId,
    Guid? CategoryId,
    IReadOnlyCollection<TransactionStatus> Statuses,
    bool DeletedOnly = false);

/// <summary>Đọc dữ liệu để xuất (Phase 91). Mọi truy vấn lọc theo <c>userId</c>; không bao giờ đọc giao dịch của người khác kể cả cùng nhóm.</summary>
public interface IDataExportRepository
{
    Task<int> CountTransactionsAsync(Guid userId, TransactionExportFilter filter, CancellationToken ct = default);

    /// <summary>Theo thứ tự thời điểm rồi id; đọc theo luồng, không nạp hết vào bộ nhớ.</summary>
    IAsyncEnumerable<TransactionExportRow> StreamTransactionsAsync(Guid userId, TransactionExportFilter filter, CancellationToken ct = default);
}
