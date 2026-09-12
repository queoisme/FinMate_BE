using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;

namespace FinMate.Infrastructure.ExternalServices;

public class AuditLogService : IAuditLogService
{
    // camelCase để metadata khớp với mọi JSON khác của API (CONVENTIONS.md §1.3) — nó được
    // trả nguyên văn qua GET /api/v1/admin/audit-logs nên hai kiểu đặt tên trong cùng một
    // response là thứ người đọc log phải tự nhớ mà không có lý do gì.
    private static readonly JsonSerializerOptions MetadataOptions =
        new(JsonSerializerDefaults.Web);

    private readonly FinMateDbContext _context;

    public AuditLogService(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(
        string eventType,
        Guid? userId,
        string? ipAddress = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            EventType = eventType,
            UserId = userId,
            IpAddress = ipAddress,
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata, MetadataOptions),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _context.SaveChangesAsync(ct);
    }
}
