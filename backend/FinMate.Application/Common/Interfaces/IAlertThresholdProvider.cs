using FinMate.Application.Budgets;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Ngưỡng cảnh báo T1/T2 đang có hiệu lực cho một người dùng. Luôn trả về giá trị hợp lệ, không bao giờ null.</summary>
public interface IAlertThresholdProvider
{
    /// <summary>
    /// Lấy từ phương pháp đang dùng (cách chính trước, rồi cách bổ trợ) — người dùng ghi đè nếu có, không thì mặc định của phương pháp.
    /// Không có phương pháp nào định nghĩa T1/T2 thì <see cref="AlertThresholds.Default"/>.
    /// </summary>
    Task<AlertThresholds> GetAsync(Guid userId, CancellationToken ct = default);
}
