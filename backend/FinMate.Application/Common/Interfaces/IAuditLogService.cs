namespace FinMate.Application.Common.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(
        string eventType,
        Guid? userId,
        string? ipAddress = null,
        object? metadata = null,
        CancellationToken ct = default);
}
