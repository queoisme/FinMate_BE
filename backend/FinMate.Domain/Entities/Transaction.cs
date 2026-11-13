using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; }

    /// <summary>
    /// Id do CLIENT sinh cho mỗi lần tạo, để gửi lại không thành giao dịch trùng.
    ///
    /// Sinh ra cho docx mục "Mạng Offline": app xếp giao dịch vào Room khi mất mạng rồi đồng
    /// bộ khi có mạng trở lại, và một hàng đợi như thế chắc chắn sẽ gửi lại — timeout giữa
    /// chừng, app bị kill, hoặc backoff. Khác thông báo ngân hàng (đã có <c>content_hash</c>
    /// dựng từ nội dung), giao dịch nhập tay không có gì để băm: hai ly cà phê 45.000đ cùng
    /// quán trong cùng một phút là hai giao dịch THẬT, nên chỉ client mới biết đâu là gửi lại.
    ///
    /// NULL được: client cũ không gửi thì vẫn tạo bình thường, chỉ là không có bảo vệ.
    /// </summary>
    public Guid? ClientRequestId { get; set; }
    public Guid UserId { get; set; }
    public Guid FinancialAccountId { get; set; }

    /// <summary>
    /// Ví đích của giao dịch <see cref="TransactionType.Transfer"/> (<see cref="FinancialAccountId"/>
    /// là ví nguồn). NULL với debit/credit — CHECK constraint ràng buộc cả 2 chiều.
    /// </summary>
    public Guid? CounterAccountId { get; set; }

    public Guid? CategoryId { get; set; }
    public Guid? NotificationLogId { get; set; }
    public Guid? SavingGoalId { get; set; }

    public long AmountCents { get; set; }
    public TransactionType TransactionType { get; set; }
    public TransactionSource Source { get; set; }
    public TransactionStatus Status { get; set; }

    public string? MerchantName { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset TransactedAt { get; set; }
    public long? BalanceAfterCents { get; set; }

    /// <summary>Không dùng giao dịch này làm dữ liệu thói quen chi tiêu cho dự báo (chi một lần, user tự loại). Số dư vẫn bị ảnh hưởng bình thường.</summary>
    public bool ExcludeFromForecast { get; set; }
    public ForecastExclusionReason? ForecastExclusionReason { get; set; }

    /// <summary>Nhóm mà chủ giao dịch đã chia sẻ khoản này. Ví và số dư vẫn của riêng họ; nhóm chỉ nhìn thấy và tính vào ngân sách nhóm.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Phân loại trong nhóm, tách khỏi <see cref="CategoryId"/> để quản lý nhóm phân loại lại mà không đụng dữ liệu cá nhân.</summary>
    public Guid? GroupCategoryId { get; set; }

    public DateTimeOffset? SharedAt { get; set; }

    /// <summary>
    /// Khoản chi gốc mà khoản này hoàn lại hoặc đảo (Phase 81). Chỉ có khi <see cref="Relation"/> có giá trị; NULL với hoàn "mồ côi".
    /// Cùng user với khoản này, mọi đường liên kết đều tra theo userId.
    /// </summary>
    public Guid? RelatedTransactionId { get; set; }

    /// <summary>Hoàn tiền hay đảo giao dịch. Có giá trị thì giao dịch phải là <see cref="TransactionType.Credit"/> và KHÔNG được tính là thu nhập.</summary>
    public TransactionRelation? Relation { get; set; }

    /// <summary>Khoản chi gốc đã bị một lệnh <see cref="TransactionRelation.Reversal"/> đảo: vẫn hiện trong lịch sử nhưng bị loại khỏi mọi tổng hợp.</summary>
    public bool IsReversed { get; set; }

    /// <summary>
    /// Giao dịch mà khoản này có thể trùng với (Phase 82). Luôn đi cùng <see cref="DuplicateStatus"/>; chỉ lưu ứng viên hạng 1,
    /// danh sách đầy đủ tính khi đọc. Bản mới hơn trỏ tới bản cũ hơn.
    /// </summary>
    public Guid? DuplicateOfTransactionId { get; set; }

    public DuplicateStatus? DuplicateStatus { get; set; }

    /// <summary>Survivor mà khoản này đã được gộp vào. Chỉ có trên dòng đã xoá mềm.</summary>
    public Guid? MergedIntoTransactionId { get; set; }

    /// <summary>
    /// Các kênh đã góp vào survivor sau khi gộp (giá trị của <see cref="TransactionSource"/>, chữ thường), ví dụ
    /// <c>{manual, notification}</c>: có <c>notification</c> nghĩa là ngân hàng đã xác thực khoản này. NULL khi chưa từng gộp.
    /// </summary>
    public string[]? MergedSources { get; set; }

    /// <summary>
    /// Người dùng đã nói "không phải chuyển ví" (Phase 83, BR-TRF-12, S2): chỉ tắt GỢI Ý chuyển ví có dính giao dịch này. Chuyển đổi tay vẫn được,
    /// và chuyển đổi thành công thì cột này được xoá.
    /// </summary>
    public DateTimeOffset? TransferDismissedAt { get; set; }

    /// <summary>Ticket đã sinh ra giao dịch nháp này, khi <see cref="Source"/> là <c>Support</c>.</summary>
    public Guid? SupportTicketId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
    public FinancialAccount? FinancialAccount { get; set; }
    public FinancialAccount? CounterAccount { get; set; }
    public Category? Category { get; set; }
    public NotificationLog? NotificationLog { get; set; }
    public SavingGoal? SavingGoal { get; set; }
    public GroupCategory? GroupCategory { get; set; }
    public Transaction? RelatedTransaction { get; set; }
    public Transaction? DuplicateOf { get; set; }
}
