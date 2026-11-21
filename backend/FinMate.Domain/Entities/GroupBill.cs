using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Hóa đơn chia của nhóm. Khác khoản chia sẻ từ sổ cá nhân: không gắn ví nào, FinMate chỉ ghi ai trả và ai nợ ai.
/// Tính vào chi tiêu và ngân sách nhóm theo TOÀN BỘ <see cref="AmountCents"/>, đứng tên người trả. Không sửa, không xóa.
/// Xóa cứng tài khoản thì <see cref="PaidByUserId"/> và <see cref="GroupBillSplit.UserId"/> thành null ("Thành viên cũ"), số tiền giữ nguyên.
/// </summary>
public class GroupBill
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public Guid? PaidByUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public GroupBillSplitMode SplitMode { get; set; }

    /// <summary>Danh mục hệ thống; danh mục nhóm thắng khi cả hai có (cùng luật với khoản chi từ quỹ).</summary>
    public Guid? CategoryId { get; set; }

    public Guid? GroupCategoryId { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<GroupBillSplit> Splits { get; set; } = [];
    public Category? Category { get; set; }
    public GroupCategory? GroupCategory { get; set; }
}

/// <summary>Phần của một người trong hóa đơn. Tổng các phần luôn bằng số tiền hóa đơn; người trả cũng có thể có phần của mình.</summary>
public class GroupBillSplit
{
    public Guid Id { get; set; }
    public Guid BillId { get; set; }
    public Guid? UserId { get; set; }
    public long ShareCents { get; set; }
}

/// <summary>Một lần trả nợ ngoài đời đã được ghi lại: <see cref="FromUserId"/> đã đưa tiền cho <see cref="ToUserId"/>.</summary>
public class GroupSettlement
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid? FromUserId { get; set; }
    public Guid? ToUserId { get; set; }
    public long AmountCents { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Lần nhắc nợ đã gửi tới một người trong một nhóm. Dùng để giới hạn tần suất (không có số tiền).</summary>
public class GroupDebtReminder
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid DebtorUserId { get; set; }
    public Guid? SentByUserId { get; set; }
    public DateTimeOffset SentAt { get; set; }
}
