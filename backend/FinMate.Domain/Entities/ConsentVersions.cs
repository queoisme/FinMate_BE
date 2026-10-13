namespace FinMate.Domain.Entities;

/// <summary>
/// Phiên bản câu chữ hiện hành của hai màn đồng ý. Đổi nội dung màn nào thì tăng số của màn đó: mọi đồng ý ở phiên bản
/// cũ ngừng có hiệu lực (người dùng bị hỏi lại, và server không gọi AI cho tới khi họ đồng ý bản mới).
/// </summary>
public static class ConsentVersions
{
    /// <summary>Điều khoản sử dụng + Chính sách quyền riêng tư.</summary>
    public const int Terms = 1;

    /// <summary>Gửi TÊN MÓN trên hoá đơn cho dịch vụ AI bên thứ ba (Google Gemini) để phân loại.</summary>
    public const int Ai = 1;
}
