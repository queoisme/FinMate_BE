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

/// <summary>Phần dữ liệu nhân viên xin xem. Lưu dạng số trong <c>support_access_grants.scopes</c>.</summary>
[Flags]
public enum SupportAccessScope
{
    None = 0,
    Accounts = 1 << 0,
    Transactions = 1 << 1,
    Budgets = 1 << 2,

    /// <summary>Tạo giao dịch nháp vào ví khách chọn. Khách vẫn phải xác nhận từng giao dịch.</summary>
    Draft = 1 << 3,
}

public enum SupportAccessStatus
{
    Pending,
    Approved,
    Denied,
    Revoked,
}

public enum OpsApprovalType
{
    UnlockUser,
    CancelDeletion,
    TransferGroupOwner,
}

public enum OpsApprovalStatus
{
    Pending,
    Approved,
    Rejected,

    /// <summary>Đã duyệt nhưng không thực hiện được; xem <c>FailureCode</c>.</summary>
    Failed,
}
