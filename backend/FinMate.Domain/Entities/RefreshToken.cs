namespace FinMate.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Lúc phiên này qua bước OTP của tài khoản nội bộ. Đổi token vẫn giữ nguyên mốc này, nên thời hạn tính từ lần nhập mã thật.</summary>
    public DateTimeOffset? OpsVerifiedAt { get; set; }

    public User? User { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
