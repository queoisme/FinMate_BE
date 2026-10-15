namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Người dùng ĐANG đồng ý gửi tên món / cụm sản phẩm cho AI bên thứ ba hay không (đúng phiên bản câu chữ hiện hành). Đọc từ DB mỗi lần để rút đồng ý có
/// hiệu lực ngay. Tách khỏi repository để các handler "chỉ hiểu câu" (chat phân tích, local-first) vẫn không chạm dữ liệu giao dịch.
/// </summary>
public interface IAiConsentReader
{
    Task<bool> HasAiConsentAsync(Guid userId, CancellationToken ct = default);
}
