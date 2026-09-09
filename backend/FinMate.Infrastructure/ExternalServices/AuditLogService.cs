using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;

namespace FinMate.Infrastructure.ExternalServices;

public class AuditLogService : IAuditLogService
{
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
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _context.SaveChangesAsync(ct);
    }
}
