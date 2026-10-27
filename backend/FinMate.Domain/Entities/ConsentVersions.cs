namespace FinMate.Domain.Entities;

/// <summary>
/// Phiên bản câu chữ hiện hành của hai màn đồng ý. Đổi nội dung màn nào thì tăng số của màn đó: mọi đồng ý ở phiên bản
/// cũ ngừng có hiệu lực (người dùng bị hỏi lại, và server không gọi AI cho tới khi họ đồng ý bản mới).
/// </summary>
public static class ConsentVersions
{
    /// <summary>
    /// Điều khoản sử dụng + Chính sách quyền riêng tư. Bản 2: nêu rõ việc gửi tên món / cụm sản phẩm cho dịch vụ AI bên thứ ba, nên đồng ý bản này
    /// cũng bật phân loại bằng AI (người dùng không bị hỏi thêm; có thể tắt trong Hồ sơ). Đồng ý đóng góp dữ liệu huấn luyện thì KHÔNG đi kèm.
    /// Bản 3: thêm đọc thông báo từ app ngân hàng/ví và đăng nhập Google, để một lần đồng ý bao trùm toàn bộ.
    /// </summary>
    public const int Terms = 3;

    /// <summary>
    /// Giá trị của <c>AiConsentVersion</c> khi người dùng CHỦ ĐỘNG tắt AI trong Hồ sơ. Khác với <c>null</c> (chưa bao giờ quyết định): đồng ý điều khoản mới
    /// không được bật lại AI cho người đã tắt.
    /// </summary>
    public const int AiOptedOut = 0;

    /// <summary>Gửi TÊN MÓN trên hoá đơn và CỤM SẢN PHẨM trong tin nhắn/giọng nói (đã bỏ số tiền) cho dịch vụ AI bên thứ ba (Google Gemini; dự phòng OpenRouter) để phân loại. Bản 2: thêm OpenRouter. Bản 3: thêm chat và giọng nói.</summary>
    public const int Ai = 3;

    /// <summary>Đóng góp chữ hoá đơn đã ẩn dữ liệu cá nhân + danh mục cuối cùng người dùng chọn để cải thiện phân loại.</summary>
    public const int Training = 1;

    /// <summary>
    /// Gửi các signal suy ra từ câu trả lời khảo sát đầu vào (không kèm định danh) cho dịch vụ AI bên thứ ba để chọn cách quản lý tài chính phù hợp.
    /// Đồng ý RIÊNG, tách khỏi <see cref="Ai"/> (vốn chỉ nói về tên món): không đồng ý thì gợi ý chạy hoàn toàn bằng rule trong backend, không gọi AI.
    /// Bản 1 chỉ nêu Google Gemini. Bản 2 (2026-10-27) nêu thêm OpenRouter làm dự phòng khi Gemini lỗi — chỉ model miễn phí của bên cam kết không lưu/huấn luyện trên dữ liệu gửi đi.
    /// Người đã đồng ý bản 1 không còn <see cref="User.SurveyAiConsentGranted"/> và phải đồng ý lại bản 2; trong lúc đó gợi ý của họ chạy bằng rule.
    /// </summary>
    public const int SurveyAi = 2;
}
