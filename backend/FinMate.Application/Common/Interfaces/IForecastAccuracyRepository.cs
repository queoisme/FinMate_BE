using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Bản chụp dự báo (để đo độ chính xác) và phản hồi "dự báo sai". Bản chụp chỉ ghi và điền số thực tế, không bao giờ sửa số dự báo.</summary>
public interface IForecastAccuracyRepository
{
    Task<bool> HasSnapshotsAsync(Guid userId, DateOnly originDate, CancellationToken ct = default);

    Task AddSnapshotsAsync(IReadOnlyList<ForecastSnapshot> snapshots, CancellationToken ct = default);

    /// <summary>Bản chụp chưa có số thực tế mà ngày của mốc đã qua (<c>HorizonDate &lt; before</c>), cũ nhất trước.</summary>
    Task<IReadOnlyList<ForecastSnapshot>> ListAwaitingActualAsync(DateOnly before, int limit, CancellationToken ct = default);

    /// <summary>Số bản chụp còn chờ tới ngày của mốc (<c>HorizonDate &gt;= from</c>, chưa có số thực tế).</summary>
    Task<int> CountPendingAsync(DateOnly from, CancellationToken ct = default);

    /// <summary>Bản chụp đã có số thực tế, <c>OriginDate &gt;= from</c>.</summary>
    Task<IReadOnlyList<ForecastSnapshot>> ListFilledSinceAsync(DateOnly from, CancellationToken ct = default);

    Task<ForecastSnapshot?> GetLatestForUserAsync(Guid userId, CancellationToken ct = default);

    Task<ForecastFeedback?> GetNewFeedbackForSnapshotAsync(Guid userId, Guid snapshotId, CancellationToken ct = default);

    Task AddFeedbackAsync(ForecastFeedback feedback, CancellationToken ct = default);

    /// <summary>Kèm bản chụp, mới nhất trước. <paramref name="status"/> null là mọi trạng thái.</summary>
    Task<(IReadOnlyList<ForecastFeedback> Items, int Total)> ListFeedbackAsync(string? status, int limit, int offset, CancellationToken ct = default);

    Task<ForecastFeedback?> GetFeedbackAsync(Guid id, CancellationToken ct = default);

    /// <summary>Phản hồi tạo từ <paramref name="since"/>, để đếm theo lý do và nguyên nhân.</summary>
    Task<IReadOnlyList<ForecastFeedback>> ListFeedbackSinceAsync(DateTimeOffset since, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
