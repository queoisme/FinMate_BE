namespace FinMate.Application.Common.Interfaces;

public interface IBudgetPeriodService
{
    /// <summary>
    /// Cộng dồn (delta dương) hoặc hoàn lại (delta âm) chi tiêu vào budget period chứa
    /// <paramref name="transactedAt"/>, cho mọi budget khớp category — kể cả budget tổng.
    /// Chỉ track thay đổi, KHÔNG gọi SaveChangesAsync: caller kết thúc bằng đúng 1 lần
    /// SaveChangesAsync nên transaction + balance + budget flush atomically.
    /// </summary>
    Task ApplyDeltaAsync(
        Guid userId,
        Guid? categoryId,
        long spentDeltaCents,
        DateTimeOffset transactedAt,
        CancellationToken ct = default);
}
