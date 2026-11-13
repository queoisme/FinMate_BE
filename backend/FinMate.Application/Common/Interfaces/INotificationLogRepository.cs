using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface INotificationLogRepository
{
    /// <summary>
    /// Log đã xử lý (Pending/Processed/Failed) có cùng nội dung, KHÔNG giới hạn thời gian.
    ///
    /// Không cần cửa sổ vì <c>contentHash</c> đã gói sẵn <c>ReceivedAt</c> làm tròn phút: hai
    /// dòng cùng hash chắc chắn là cùng một thông báo tại cùng một phút. Trước đây cửa sổ đo
    /// trên <c>CreatedAt</c> — lúc SERVER ghi — nên một thông báo xếp hàng offline rồi gửi lại
    /// sau 5 phút sẽ lọt qua và tạo giao dịch nháp trùng.
    /// </summary>
    Task<NotificationLog?> GetProcessedByContentHashAsync(
        Guid userId, string contentHash, CancellationToken ct = default);

    /// <summary>
    /// Log <c>Ignored</c> vừa ghi gần đây. Nhánh này CÓ cửa sổ thời gian, cố ý: thông báo bị
    /// bỏ qua vì ví chưa được theo dõi, nên nếu người dùng thêm ví rồi app mới đồng bộ, nó
    /// phải được xử lý lại chứ không bị nuốt vĩnh viễn. Cửa sổ chỉ để chặn một cú bắn dồn.
    /// </summary>
    Task<NotificationLog?> GetRecentIgnoredByContentHashAsync(
        Guid userId, string contentHash, DateTimeOffset since, CancellationToken ct = default);

    Task<List<NotificationLog>> GetFailedForRetryAsync(int maxRetryCount, CancellationToken ct = default);

    /// <summary>
    /// Xóa nội dung gốc (<c>notification_body</c>) của mọi log ghi trước <paramref name="cutoff"/> (NFR-SEC-23), trả số dòng đã xóa.
    /// Chỉ cột nội dung: tiêu đề, <c>content_hash</c> (dedup vẫn chạy), trạng thái, kết quả AI và giao dịch giữ nguyên.
    /// Đo theo <c>CreatedAt</c> (giờ server ghi), không theo <c>ReceivedAt</c> do client gửi lên.
    /// </summary>
    Task<int> PurgeBodiesOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);

    Task AddAsync(NotificationLog log, CancellationToken ct = default);
    Task UpdateAsync(NotificationLog log, CancellationToken ct = default);
    Task AddAiResultAsync(AiResult result, CancellationToken ct = default);

    /// <summary>Phase 81: gợi ý hoàn/đảo mà AI đã trả cho các thông báo này (chỉ của user này). Thông báo không có gợi ý thì không có trong kết quả.</summary>
    Task<IReadOnlyDictionary<Guid, Domain.Enums.TransactionRelation>> GetRelationHintsAsync(
        Guid userId, IReadOnlyCollection<Guid> notificationLogIds, CancellationToken ct = default);
}
