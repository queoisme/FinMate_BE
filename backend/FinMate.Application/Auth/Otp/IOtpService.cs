namespace FinMate.Application.Auth.Otp;

/// <param name="Code">Mã 6 chữ số dạng rõ — chỉ tồn tại đủ lâu để gửi đi, không bao giờ lưu.</param>
public record OtpIssueResult(bool Issued, string? Code);

public interface IOtpService
{
    /// <summary>
    /// Sinh mã mới cho email + mục đích. Trả <c>Issued = false</c> khi vừa xin cách đây chưa
    /// lâu — giới hạn theo EMAIL, vì hạn mức theo IP không chặn được việc nhắm vào một hộp thư
    /// cụ thể, mà mỗi lần gửi vừa tốn tiền vừa là rác trong hộp thư người khác.
    /// </summary>
    Task<OtpIssueResult> IssueAsync(string email, OtpPurpose purpose, CancellationToken ct = default);

    /// <summary>
    /// Kiểm mã. Đúng thì mã bị HUỶ ngay (dùng một lần). Sai quá số lần cho phép thì mã cũng bị
    /// huỷ — không thì 10⁶ khả năng sẽ bị dò cạn bằng cách thử mãi.
    /// </summary>
    Task<OtpVerifyResult> VerifyAsync(
        string email, OtpPurpose purpose, string code, CancellationToken ct = default);
}

public enum OtpVerifyResult
{
    Valid,

    /// <summary>Sai, hết hạn, đã dùng, hoặc chưa từng gửi — gộp làm một có chủ ý.</summary>
    Invalid,

    TooManyAttempts,
}
