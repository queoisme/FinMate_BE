using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class DataDeletionRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ScheduledHardDeleteAt { get; set; }
    /// <summary>
    /// Lúc yêu cầu THÔI ở trạng thái chờ — <see cref="Status"/> nói theo hướng nào (đã xoá
    /// cứng, hay bị huỷ). Một cột cho hai kết cục loại trừ nhau; hai cột chỉ tổ phải nhớ đọc
    /// cột nào ứng với trạng thái nào.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }
    public DataDeletionStatus Status { get; set; } = DataDeletionStatus.Pending;
}
