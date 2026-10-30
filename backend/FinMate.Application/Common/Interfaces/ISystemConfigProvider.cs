using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Cấu hình hệ thống đang chạy, đọc qua bộ nhớ đệm ngắn: middleware bảo trì hỏi MỖI request nên không thể đọc DB mỗi lần.</summary>
public interface ISystemConfigProvider
{
    /// <summary>Bản đang chạy; chưa ai lưu thì là bản mặc định (version 0).</summary>
    Task<SystemConfigSnapshot> GetAsync(CancellationToken ct = default);

    /// <summary>Bỏ bản đệm sau khi lưu hoặc hoàn tác để request kế tiếp thấy ngay thay đổi trên máy này (máy khác thấy khi bản đệm hết hạn).</summary>
    void Invalidate();
}
