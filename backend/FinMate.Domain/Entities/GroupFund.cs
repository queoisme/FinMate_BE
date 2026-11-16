using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Quỹ chung của nhóm (Phase 85): một sổ quỹ ẢO thuộc về nhóm. FinMate không giữ tiền, chỉ ghi sổ; ví cá nhân không bị chia sẻ.
/// <see cref="BalanceCents"/> là tổng tính sẵn (luôn bằng Σ dòng chưa xoá, có dấu), được khoá lạc quan bằng <c>xmin</c>.
/// </summary>
public class GroupFund
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Note { get; set; }

    /// <summary>Được phép âm: có người ứng trước (BR-FUND-02).</summary>
    public long BalanceCents { get; set; }

    /// <summary>Số lần quỹ chuyển từ ≥ 0 sang âm. Nằm trong khoá chống lặp của push "quỹ đang âm", nên âm lại thì được báo lại (D18).</summary>
    public int NegativeEpisode { get; set; }

    public Guid? CreatedByUserId { get; set; }

    /// <summary>Chỉ lưu trữ được khi số dư bằng 0; đã lưu trữ thì chỉ đọc (BR-FUND-03, D16).</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsArchived => ArchivedAt is not null;
}

/// <summary>
/// Một dòng của sổ quỹ. Thuộc về NHÓM: rời nhóm thì dòng ở lại và vẫn ghi tên người đó (BR-FUND-13); xoá cứng tài khoản thì
/// <see cref="MemberUserId"/> thành null và dòng hiện "Thành viên cũ" (BR-FUND-14/15), số dư quỹ không đổi.
/// </summary>
public class GroupFundEntry
{
    public Guid Id { get; set; }
    public Guid FundId { get; set; }
    public Guid GroupId { get; set; }

    /// <summary>Người ghi, cũng là người gắn với dòng (D4). Null = thành viên cũ đã bị ẩn danh.</summary>
    public Guid? MemberUserId { get; set; }

    public GroupFundEntryKind Kind { get; set; }

    /// <summary>Dương với góp, chi, rút; có dấu (khác 0) với điều chỉnh (D3).</summary>
    public long AmountCents { get; set; }

    public DateTimeOffset TransactedAt { get; set; }

    /// <summary>Chỉ với chi: danh mục hệ thống (D10).</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Chỉ với chi: danh mục nhóm; thắng danh mục hệ thống khi khớp ngân sách.</summary>
    public Guid? GroupCategoryId { get; set; }

    public string? Note { get; set; }
    public string? MerchantName { get; set; }
    public Guid? ClientRequestId { get; set; }

    /// <summary>Chi tạo mới hay chuyển từ một giao dịch có sẵn của người ghi (S4, D24).</summary>
    public GroupFundEntryOrigin Origin { get; set; } = GroupFundEntryOrigin.Created;

    /// <summary>Danh mục của giao dịch gốc khi <see cref="Origin"/> là chuyển đổi: dùng để khôi phục về khoản chi. Không dựa vào snapshot vì snapshot bị dọn.</summary>
    public Guid? OriginCategoryId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }

    public GroupFund? Fund { get; set; }
    public Category? Category { get; set; }
    public GroupCategory? GroupCategory { get; set; }
}
