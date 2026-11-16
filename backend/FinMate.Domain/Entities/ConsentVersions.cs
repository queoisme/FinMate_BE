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
    /// Bản 4: thêm chia sẻ với nhóm: giao dịch người dùng chủ động chia sẻ (số tiền, nơi chi, ghi chú, tên họ) hiện với thành viên nhóm và còn đó khi họ rời nhóm. Đồng thời nêu việc hỗ trợ: ảnh gửi vào yêu cầu hỗ trợ được lưu có thời hạn, và nhân viên chỉ xem dữ liệu khi người dùng đồng ý theo từng yêu cầu. Cũng nêu rõ ảnh hóa đơn được gửi nguyên bản, chưa che dữ liệu cá nhân, tới Google Cloud Vision để đọc chữ.
    /// </summary>
    public const int Terms = 4;

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
    /// <para>
    /// <b>Từ 2026-11-01 đồng ý này không còn chặn gì:</b> <c>MethodRecommendationService</c> luôn gửi signals ẩn danh cho AI rồi rơi về rule khi AI lỗi,
    /// bất kể <see cref="User.SurveyAiConsentGranted"/>. Giá trị chỉ còn được ghi nhận khi người dùng cấp/rút qua <c>/users/me/consents</c>.
    /// Gỡ hẳn loại đồng ý này hay khôi phục việc kiểm là quyết định của chủ sản phẩm (FR-CONS-06, REQUIREMENTS §13.4).
    /// </para>
    /// </summary>
    public const int SurveyAi = 2;

    /// <summary>Bài, bình luận và ảnh đăng lên cộng đồng là công khai với mọi người dùng. Hỏi riêng khi đăng lần đầu, nên không làm ai phải đồng ý lại Điều khoản.</summary>
    public const int Community = 1;
}
