using FinMate.Application.Forecasting;

namespace FinMate.Application.Common.Interfaces;

public interface IForecastDataRepository
{
    /// <summary>
    /// Giao dịch đã xác nhận của một user trong <c>[from, to]</c> (giờ VN), không gồm chuyển khoản nội bộ và cho vay/mượn. Khoảng luôn bị chặn
    /// (vài tháng của MỘT user) nên kéo về bộ nhớ là đủ rẻ, cùng cách với <c>ReportRepository.GetDailySpendAsync</c>.
    /// </summary>
    Task<IReadOnlyList<ForecastTransaction>> GetTransactionsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
