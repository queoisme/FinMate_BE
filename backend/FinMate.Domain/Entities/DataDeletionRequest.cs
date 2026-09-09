using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class DataDeletionRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ScheduledHardDeleteAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DataDeletionStatus Status { get; set; } = DataDeletionStatus.Pending;
}
