using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Ảnh chụp trạng thái TRƯỚC một lượt gộp hoặc chuyển đổi (Phase 83, phần B), ghi trong cùng DB transaction với thao tác đó: đủ để hoàn tác sau
/// này. Có hạn lưu (<c>snapshots.retentionDays</c>) để không giữ dữ liệu cá nhân lâu hơn cần thiết. Không có khoá ngoại tới <c>transactions</c>:
/// đây là lịch sử, và giao dịch chỉ bị xoá cứng cùng user (khi đó dòng này cũng đi theo user).
/// </summary>
public class TransactionMergeSnapshot
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SurvivorId { get; set; }

    /// <summary>Bản bị gộp; null khi đổi một chiều thành Transfer (không có bản nào bị gộp).</summary>
    public Guid? MergedId { get; set; }

    public MergeSnapshotKind Kind { get; set; }

    /// <summary>Loại chuyển ví khi AI nhận ra; null với gộp bản trùng hoặc ghép chỉ nhờ tên ví. Chỉ để thống kê.</summary>
    public TransferKind? TransferKind { get; set; }

    /// <summary>JSON: các trường đã đổi của từng dòng, tham chiếu đã chuyển, khoản phí đã tạo, thay đổi số dư từng ví.</summary>
    public string Before { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}
