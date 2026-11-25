using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Yêu cầu tải dữ liệu cá nhân (Phase 91). Mọi phương thức theo user đều lọc <c>userId</c>.</summary>
public interface IPersonalDataExportRepository
{
    Task<bool> HasActiveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Thời điểm yêu cầu của các yêu cầu tính vào hạn mức (D14) từ <paramref name="since"/>, cũ nhất trước.</summary>
    Task<IReadOnlyList<DateTimeOffset>> CountedRequestTimesSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Lưu yêu cầu mới. False khi user đã có một yêu cầu đang chạy (chỉ mục duy nhất một phần chặn, kể cả khi hai request tới cùng lúc).</summary>
    Task<bool> TryAddAsync(PersonalDataExport export, CancellationToken ct = default);

    Task<PersonalDataExport?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Mới nhất trước, trong cửa sổ lịch sử.</summary>
    Task<IReadOnlyList<PersonalDataExport>> ListAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    // Dùng bởi job (Phase 91, BR-EXP-09). Mọi chuyển trạng thái là UPDATE có điều kiện, nên job, job quét và job dọn dẹp không giẫm lên nhau.

    /// <summary>Nhận việc: <c>Pending</c>, hoặc <c>Running</c> đã quá hạn thuê, và chưa hết lượt thử → <c>Running</c>, tăng <c>Attempts</c>. Null nếu không nhận được.</summary>
    Task<PersonalDataExport?> TryLeaseAsync(Guid id, DateTimeOffset now, DateTimeOffset leaseUntil, int maxAttempts, CancellationToken ct = default);

    /// <summary>Lưu file đã mã hoá và chuyển <c>Running → Ready</c> trong một transaction. False nếu yêu cầu không còn ở <c>Running</c>.</summary>
    Task<bool> MarkReadyAsync(Guid id, byte[] ciphertext, byte[] nonce, long sizeBytes, DateTimeOffset readyAt, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary><c>Pending</c>/<c>Running</c> → <c>Failed</c>. False nếu đã ở trạng thái khác.</summary>
    Task<bool> MarkFailedAsync(Guid id, string failureCode, bool countsTowardLimit, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Trả việc về <c>Pending</c> sau một lỗi tạm thời, để job quét chạy lại.</summary>
    Task ReleaseAsync(Guid id, CancellationToken ct = default);

    /// <summary>Việc cần chạy lại: <c>Pending</c> tạo trước <paramref name="pendingBefore"/> (enqueue bị mất) hoặc <c>Running</c> quá hạn thuê (job chết).</summary>
    Task<IReadOnlyList<PersonalDataExport>> FindStaleAsync(DateTimeOffset pendingBefore, DateTimeOffset now, CancellationToken ct = default);

    // Link tải (BR-EXP-10).

    /// <summary>Đặt token tải mới (thay token cũ) cho yêu cầu <c>Ready</c> của user. False nếu không còn <c>Ready</c>.</summary>
    Task<bool> SetDownloadTokenAsync(Guid userId, Guid id, string tokenHash, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>Đổi token lấy một lượt tải: đúng token, chưa hết hạn, còn <c>Ready</c> → xoá token, tăng số lượt tải. Một UPDATE, nên hai lượt dùng cùng lúc chỉ một qua.</summary>
    Task<bool> TryConsumeDownloadTokenAsync(Guid userId, Guid id, string tokenHash, DateTimeOffset now, CancellationToken ct = default);

    Task<PersonalDataExportArchive?> GetArchiveAsync(Guid id, CancellationToken ct = default);

    // Dọn dẹp (BR-EXP-09).

    /// <summary>Yêu cầu <c>Ready</c> đã quá hạn giữ file (tối đa <paramref name="limit"/> dòng mỗi lượt).</summary>
    Task<IReadOnlyList<PersonalDataExport>> FindDueForExpiryAsync(DateTimeOffset now, int limit, CancellationToken ct = default);

    /// <summary><c>Ready → Expired</c>, huỷ token và xoá file trong một transaction. False nếu không còn <c>Ready</c>.</summary>
    Task<bool> ExpireAsync(Guid id, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Xoá dòng lịch sử (đã xong) cũ hơn <paramref name="before"/>; file đi theo khoá ngoại. Trả số dòng đã xoá.</summary>
    Task<int> PurgeHistoryAsync(DateTimeOffset before, CancellationToken ct = default);

    /// <summary>D15: số yêu cầu còn <c>Pending</c>/<c>Running</c> từ trước <paramref name="before"/> (có nguy cơ trễ hạn luật 10 ngày).</summary>
    Task<int> CountUnfinishedSinceBeforeAsync(DateTimeOffset before, CancellationToken ct = default);
}
