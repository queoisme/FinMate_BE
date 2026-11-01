using FinMate.Application.Budgets;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Ngưỡng hiệu lực đến từ đâu: cách quản lý đang dùng, ngưỡng cá nhân của người dùng, hay mặc định hệ thống.</summary>
public enum AlertThresholdSource
{
    Method,
    Personal,
    System,
}

/// <param name="MethodCode">Mã cách quản lý (chỉ khi <paramref name="Source"/> là <see cref="AlertThresholdSource.Method"/>).</param>
/// <param name="MethodName">Tên hiển thị của cách đó.</param>
public sealed record AlertThresholdResolution(AlertThresholds Thresholds, AlertThresholdSource Source, string? MethodCode = null, string? MethodName = null);

/// <summary>Ngưỡng cảnh báo T1/T2 đang có hiệu lực cho một người dùng. Luôn trả về giá trị hợp lệ, không bao giờ null.</summary>
public interface IAlertThresholdProvider
{
    /// <summary>
    /// Thứ tự ưu tiên: (1) cách quản lý đang dùng định nghĩa T1/T2 (cách chính trước, rồi bổ trợ; người dùng ghi đè nếu có, không thì mặc định của cách đó),
    /// (2) ngưỡng cá nhân của người dùng, (3) mặc định hệ thống do admin đặt. Ngưỡng cá nhân CHỈ dùng khi không có cách nào định nghĩa T1/T2.
    /// </summary>
    Task<AlertThresholdResolution> ResolveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Chỉ phần ngưỡng của <see cref="ResolveAsync"/>.</summary>
    Task<AlertThresholds> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Mặc định hệ thống (admin đặt ở cấu hình hệ thống); cấu hình lỗi hoặc không hợp lệ thì <see cref="AlertThresholds.Default"/>.</summary>
    Task<AlertThresholds> GetSystemDefaultAsync(CancellationToken ct = default);
}
