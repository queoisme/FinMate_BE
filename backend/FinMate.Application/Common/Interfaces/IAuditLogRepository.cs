using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public record AuditLogListFilter(
    Guid? UserId,
    string? EventType,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    string? Cursor,
    int Limit,
    string? EventTypePrefix = null,
    string? Search = null);   // chữ tự do, khớp tên sự kiện hoặc IP (cố ý KHÔNG tìm trong metadata: nó có thể chứa thông tin nhạy cảm)

public record AuditLogListResult(IReadOnlyList<AuditLog> Items, string? NextCursor);

/// <summary>Người thực hiện một dòng nhật ký, để trang quản trị hiện tên thay vì mã.</summary>
public record AuditActor(string DisplayName, string Email, string Role);

/// <summary>
/// Đường ĐỌC của <c>audit_logs</c>. Ghi vẫn đi qua <see cref="IAuditLogService"/> — tách hai
/// chiều vì bên ghi được gọi từ mọi handler còn bên đọc chỉ admin dùng, và gộp lại sẽ khiến
/// mọi handler mang theo một API truy vấn nó không bao giờ cần.
///
/// Không có phương thức nào sửa hay xoá: nhật ký kiểm toán mà admin sửa được thì không còn là
/// nhật ký kiểm toán.
/// </summary>
/// <summary>Dựng DTO cho một trang nhật ký: thêm câu mô tả và người thực hiện. Dùng chung cho nhật ký quản trị và nhật ký vận hành.</summary>
public static class AuditLogPresenter
{
    public static async Task<IReadOnlyList<Models.AuditLogDto>> ToDtosAsync(this IAuditLogRepository repository, IReadOnlyList<AuditLog> items, CancellationToken ct = default)
    {
        var ids = items.Where(a => a.UserId.HasValue).Select(a => a.UserId!.Value).Distinct().ToList();
        var actors = ids.Count == 0 ? new Dictionary<Guid, AuditActor>() : await repository.GetActorsAsync(ids, ct);
        return items.Select(a =>
        {
            var actor = a.UserId is { } id && actors.TryGetValue(id, out var found) ? found : null;
            return new Models.AuditLogDto(a.Id, a.UserId, a.EventType, a.Metadata, a.IpAddress, a.CreatedAt,
                AuditEventLabels.Describe(a.EventType), actor?.DisplayName, actor?.Email, actor?.Role);
        }).ToList();
    }
}

public interface IAuditLogRepository
{
    Task<AuditLogListResult> GetPageAsync(AuditLogListFilter filter, CancellationToken ct = default);

    /// <summary>Tên, email, vai trò của các người dùng này, kể cả tài khoản đã xóa mềm (nhật ký cũ vẫn phải biết ai đã làm).</summary>
    Task<IReadOnlyDictionary<Guid, AuditActor>> GetActorsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}
