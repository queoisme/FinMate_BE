using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Một phiên bản BẤT BIẾN của cấu hình hệ thống (bảo trì, phiên bản app, tính năng, giao diện...). Mỗi lần lưu hoặc hoàn tác tạo thêm một dòng mới và lưu trữ dòng đang chạy,
/// nên lịch sử là một dãy tuyến tính: không có dòng nào bị sửa hay xóa. Chỉ chứa dữ liệu KHÔNG bí mật; key bên thứ ba có kho mã hóa riêng.
/// </summary>
public class SystemConfigVersion
{
    public Guid Id { get; set; }

    /// <summary>Tên tài liệu cấu hình (hiện chỉ có <c>app</c>); để dành cho tài liệu khác về sau mà không cần bảng mới.</summary>
    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }
    public SystemConfigStatus Status { get; set; }

    /// <summary>Nội dung JSON đã được kiểm tra. Lưu nguyên văn để đọc lại đúng như lúc lưu kể cả khi code thêm trường mới sau này.</summary>
    public string Content { get; set; } = "{}";

    public string? Note { get; set; }

    /// <summary>Admin thực hiện. Không có khóa ngoại: xóa cứng tài khoản không được làm mất lịch sử thay đổi cấu hình.</summary>
    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
