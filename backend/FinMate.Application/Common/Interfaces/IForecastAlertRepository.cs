using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public sealed record ForecastAlertPage(IReadOnlyList<ForecastAlert> Items, string? NextCursor);

/// <summary>Hộp thư cảnh báo dự báo. Mọi hàm đọc theo user đều lọc theo <c>userId</c>.</summary>
public interface IForecastAlertRepository
{
    Task<bool> ExistsAsync(Guid userId, ForecastAlertKind kind, string dedupKey, CancellationToken ct = default);
    Task<bool> AnyCreatedSinceAsync(Guid userId, ForecastAlertKind kind, DateTimeOffset since, CancellationToken ct = default);
    Task<bool> AnyWithKeyPrefixSinceAsync(Guid userId, ForecastAlertKind kind, string dedupKeyPrefix, DateTimeOffset since, CancellationToken ct = default);
    /// <summary>Lúc tạo của mọi cảnh báo có <c>dedup_key</c> bắt đầu bằng <paramref name="dedupKeyPrefix"/>, cũ trước. Dùng đếm lần nhắc trong một đợt (Phase 84, D17).</summary>
    Task<IReadOnlyList<DateTimeOffset>> ListCreatedWithKeyPrefixAsync(Guid userId, ForecastAlertKind kind, string dedupKeyPrefix, CancellationToken ct = default);

    Task<bool> AnyMarkedInaccurateSinceAsync(Guid userId, ForecastAlertKind kind, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Thêm và lưu. False nếu đã có cảnh báo cùng <c>(user, kind, dedup_key)</c> (hai lượt kiểm tra chạy song song).</summary>
    Task<bool> TryAddAsync(ForecastAlert alert, CancellationToken ct = default);

    Task<IReadOnlyList<ForecastAlert>> ListDuePendingAsync(DateTimeOffset now, int limit, CancellationToken ct = default);
    Task<ForecastAlertPage> ListForUserAsync(Guid userId, string? cursor, int limit, CancellationToken ct = default);
    Task<ForecastAlert?> GetAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task UpdateAsync(ForecastAlert alert, CancellationToken ct = default);
}
