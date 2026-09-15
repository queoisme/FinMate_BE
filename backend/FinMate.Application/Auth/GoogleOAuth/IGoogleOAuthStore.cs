using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.GoogleOAuth;

/// <summary>
/// Hai thứ sống rất ngắn của luồng OAuth qua trình duyệt, cùng cất ở Redis vì TTL chính là
/// mô hình lưu trữ của chúng.
/// </summary>
public interface IGoogleOAuthStore
{
    /// <summary>
    /// Chuỗi ngẫu nhiên gửi kèm sang Google và nhận lại ở callback.
    ///
    /// Thiếu nó thì kẻ tấn công dựng được một lời gọi callback bằng <c>code</c> của CHÚNG,
    /// khiến nạn nhân đăng nhập vào tài khoản Google của kẻ tấn công mà không biết.
    /// </summary>
    Task<string> IssueStateAsync(CancellationToken ct = default);

    /// <summary>True đúng MỘT lần cho mỗi state — dùng rồi là huỷ.</summary>
    Task<bool> ConsumeStateAsync(string state, CancellationToken ct = default);

    /// <summary>
    /// Cất kết quả đăng nhập sau một mã ngắn, để redirect chỉ mang mã chứ không mang token.
    /// </summary>
    Task<string> IssueHandoffAsync(AuthResultDto result, CancellationToken ct = default);

    Task<AuthResultDto?> ConsumeHandoffAsync(string code, CancellationToken ct = default);
}
