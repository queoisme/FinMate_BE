using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Phase 95: bảng cũng giữ BẢN NHÁP tham số dự báo (<see cref="Scope"/> = <c>forecast</c>): Staff soạn, OpsManager duyệt. Bản nháp chưa có
/// <see cref="Version"/> và chỉ chứa hai nhóm <c>forecast</c> + <c>limits</c>; khi được duyệt, chính dòng đó thành phiên bản đang chạy với nội dung là toàn bộ tài liệu.
/// Runtime chỉ đọc dòng <c>active</c>.
///
/// Một phiên bản BẤT BIẾN của cấu hình hệ thống (bảo trì, phiên bản app, tính năng, giao diện...). Mỗi lần lưu hoặc hoàn tác tạo thêm một dòng mới và lưu trữ dòng đang chạy,
/// nên lịch sử là một dãy tuyến tính: không có dòng nào bị sửa hay xóa. Chỉ chứa dữ liệu KHÔNG bí mật; key bên thứ ba có kho mã hóa riêng.
/// </summary>
public class SystemConfigVersion
{
    public Guid Id { get; set; }

    /// <summary>Tên tài liệu cấu hình (hiện chỉ có <c>app</c>); để dành cho tài liệu khác về sau mà không cần bảng mới.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Số phiên bản; chỉ dòng <c>active</c>/<c>archived</c> có (bản nháp chưa chiếm số và không nằm trong lịch sử).</summary>
    public int? Version { get; set; }
    public SystemConfigStatus Status { get; set; }

    /// <summary>Phạm vi của bản nháp (<c>forecast</c>); null với dòng do Admin lưu trực tiếp.</summary>
    public string? Scope { get; set; }

    /// <summary>Phiên bản đang chạy lúc tạo bản nháp. Duyệt chỉ được khi nó vẫn là phiên bản đang chạy (không tự gộp).</summary>
    public int? BaseVersion { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>Người duyệt hoặc từ chối; khác <see cref="CreatedBy"/> (CHECK trong DB).</summary>
    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Ghi chú duyệt hoặc lý do từ chối. Không đưa vào audit.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>JSON <c>[{path, baseValue, activeValue, draftValue}]</c>: trường mà cả bản đang chạy lẫn bản nháp đều đã đổi so với bản gốc khi "làm lại" (S2).</summary>
    public string? Conflicts { get; set; }

    /// <summary>Lần sửa bản nháp gần nhất; khoá đồng thời của <c>PATCH</c> (D6).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Nội dung JSON đã được kiểm tra. Lưu nguyên văn để đọc lại đúng như lúc lưu kể cả khi code thêm trường mới sau này.</summary>
    public string Content { get; set; } = "{}";

    public string? Note { get; set; }

    /// <summary>Người lưu (đường Admin) hoặc người soạn bản nháp. Không có khóa ngoại: xóa cứng tài khoản không được làm mất lịch sử thay đổi cấu hình.</summary>
    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
