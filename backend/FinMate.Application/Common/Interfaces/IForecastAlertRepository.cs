using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Hộp thư cảnh báo dự báo. Mọi hàm đọc theo user đều lọc theo <c>userId</c>.</summary>
public interface IForecastAlertRepository
{
    Task<bool> ExistsAsync(Guid userId, ForecastAlertKind kind, string dedupKey, CancellationToken ct = default);
    Task<bool> AnyCreatedSinceAsync(Guid userId, ForecastAlertKind kind, DateTimeOffset since, CancellationToken ct = default);
    Task<bool> AnyMarkedInaccurateSinceAsync(Guid userId, ForecastAlertKind kind, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Thêm và lưu. False nếu đã có cảnh báo cùng <c>(user, kind, dedup_key)</c> (hai lượt kiểm tra chạy song song).</summary>
    Task<bool> TryAddAsync(ForecastAlert alert, CancellationToken ct = default);

    Task<IReadOnlyList<ForecastAlert>> ListDuePendingAsync(DateTimeOffset now, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<ForecastAlert>> ListForUserAsync(Guid userId, int limit, CancellationToken ct = default);
    Task<ForecastAlert?> GetAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task UpdateAsync(ForecastAlert alert, CancellationToken ct = default);
}
