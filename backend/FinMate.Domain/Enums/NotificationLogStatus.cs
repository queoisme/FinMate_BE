namespace FinMate.Domain.Enums;

public enum NotificationLogStatus
{
    Pending,
    Processed,
    Failed,
    Ignored,

    /// <summary>Cùng một sự kiện ngân hàng đã có giao dịch: cùng provider, số tiền và số dư sau giao dịch (Phase 82, BR-DUP-05).</summary>
    Duplicate,
}
