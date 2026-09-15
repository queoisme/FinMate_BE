namespace FinMate.Application.Auth.GoogleOAuth;

/// <param name="MobileDeepLink">
/// Nơi callback trả người dùng về, ví dụ <c>finmate://auth</c>. TRỐNG = chế độ thử: callback
/// hiện mã bàn giao ra một trang HTML, để kiểm chứng được toàn bộ luồng bằng trình duyệt máy
/// tính mà không cần app đã dựng xong.
/// </param>
public record GoogleOAuthOptions(
    bool Enabled,
    string ClientId,
    string RedirectUri,
    string? MobileDeepLink);
