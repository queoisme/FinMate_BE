using FinMate.Application.Common.Models;

namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Dự báo chi tiêu tháng. Bản hiện tại tính bằng thống kê ngay trong backend
/// (<c>StatisticalSpendingForecaster</c>) — ngoại suy run-rate vốn là phép thống kê, đẩy qua
/// HTTP sang AI Service không làm nó chính xác hơn mà lại làm báo cáo chết khi AI Service
/// chưa có. Đây là chỗ Phase 9 cắm model AI vào mà không phải đổi controller hay DTO.
/// </summary>
public interface ISpendingForecaster
{
    Task<SpendingForecastDto> ForecastCurrentMonthAsync(Guid userId, CancellationToken ct = default);
}
