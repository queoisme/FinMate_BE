namespace FinMate.Domain.Entities;

/// <summary>
/// Phiên bản câu chữ hiện hành của hai màn đồng ý. Đổi nội dung màn nào thì tăng số của màn đó: mọi đồng ý ở phiên bản
/// cũ ngừng có hiệu lực (người dùng bị hỏi lại, và server không gọi AI cho tới khi họ đồng ý bản mới).
/// </summary>
public static class ConsentVersions
{
    /// <summary>Điều khoản sử dụng + Chính sách quyền riêng tư.</summary>
    public const int Terms = 1;

    /// <summary>Gửi TÊN MÓN trên hoá đơn và CỤM SẢN PHẨM trong tin nhắn/giọng nói (đã bỏ số tiền) cho dịch vụ AI bên thứ ba (Google Gemini; dự phòng OpenRouter) để phân loại. Bản 2: thêm OpenRouter. Bản 3: thêm chat và giọng nói.</summary>
    public const int Ai = 3;

    /// <summary>Đóng góp chữ hoá đơn đã ẩn dữ liệu cá nhân + danh mục cuối cùng người dùng chọn để cải thiện phân loại.</summary>
    public const int Training = 1;
}
