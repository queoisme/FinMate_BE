using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;

namespace FinMate.Infrastructure.ExternalServices;

public class AuditLogService : IAuditLogService
{
    // Khớp đúng cấu hình JSON của API (Program.cs: camelCase + enum dạng chuỗi), vì metadata
    // được trả NGUYÊN VĂN qua GET /api/v1/admin/audit-logs. Thiếu JsonStringEnumConverter thì
    // nhật ký ghi "periodType": 0 — vẫn là JSON hợp lệ, nhưng người đọc log sáu tháng sau phải
    // tra ngược thứ tự khai báo enum để biết 0 là gì, và thứ tự đó có thể đã đổi.
    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

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
