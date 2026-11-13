using FinMate.Application.Forecasting;

namespace FinMate.Application.Common.Interfaces;

public interface IForecastDataRepository
{
    /// <summary>
    /// Giao dịch đã xác nhận của một user trong <c>[from, to]</c> (giờ VN), không gồm chuyển khoản nội bộ và cho vay/mượn. Khoảng luôn bị chặn
    /// (vài tháng của MỘT user) nên kéo về bộ nhớ là đủ rẻ, cùng cách với <c>ReportRepository.GetDailySpendAsync</c>.
    /// </summary>
    /// <remarks>
    /// Phase 81: đây là dữ liệu THÓI QUEN (<see cref="RefundNetting.ForHabits"/>): khoản hoàn và cặp đảo đã được trừ/loại, khoản chi hoàn một phần
    /// mang số ròng. Mọi bộ tính thói quen, thu nhập, bất thường, định kỳ, xu hướng dùng hàm này. Dựng lại số dư thì dùng
    /// <see cref="GetRawTransactionsAsync"/>.
    /// </remarks>
    Task<IReadOnlyList<ForecastTransaction>> GetTransactionsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Như trên nhưng giữ nguyên mọi dòng: khoản hoàn là tiền vào, cặp đảo triệt tiêu nhau. Chỉ dùng cho phép đối chiếu số dư (BR-RF-07).</summary>
    Task<IReadOnlyList<ForecastTransaction>> GetRawTransactionsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
