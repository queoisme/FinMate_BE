using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public record AuditLogListFilter(
    Guid? UserId,
    string? EventType,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    string? Cursor,
    int Limit);

public record AuditLogListResult(IReadOnlyList<AuditLog> Items, string? NextCursor);

/// <summary>
/// Đường ĐỌC của <c>audit_logs</c>. Ghi vẫn đi qua <see cref="IAuditLogService"/> — tách hai
/// chiều vì bên ghi được gọi từ mọi handler còn bên đọc chỉ admin dùng, và gộp lại sẽ khiến
/// mọi handler mang theo một API truy vấn nó không bao giờ cần.
///
/// Không có phương thức nào sửa hay xoá: nhật ký kiểm toán mà admin sửa được thì không còn là
/// nhật ký kiểm toán.
/// </summary>
public interface IAuditLogRepository
{
    Task<AuditLogListResult> GetPageAsync(AuditLogListFilter filter, CancellationToken ct = default);
}
