namespace FinMate.Domain.Enums;

public enum SupportTicketCategory
{
    Security,
    Account,
    Transaction,
    Receipt,
    Notification,
    Group,
    Question,
}

public enum SupportTicketPriority
{
    P1,
    P2,
    P3,
}

public enum SupportTicketStatus
{
    New,
    InProgress,
    WaitingCustomer,
    Escalated,
    Resolved,
    Closed,
}

public enum SupportMessageKind
{
    Customer,
    Staff,

    /// <summary>Chỉ đội vận hành thấy. Không bao giờ trả về cho khách.</summary>
    InternalNote,
}
