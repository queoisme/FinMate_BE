using FinMate.Domain.Enums;
using FinMate.Domain.ValueObjects;

namespace FinMate.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    /// <summary>
    /// Thu nhập hằng tháng DỰ KIẾN, do người dùng khai ở onboarding (docx Bước 1.3).
    ///
    /// Khác hẳn tổng giao dịch <c>Credit</c> thực tế trong báo cáo: tháng đầu dùng app thì
    /// tổng đó bằng 0, đúng lúc người dùng cần cảnh báo bội chi nhất. NULL = chưa khai, và
    /// mọi nơi dùng nó phải BỎ QUA phép so sánh thay vì thay bằng 0 — "chưa biết" không phải
    /// là "không có thu nhập".
    /// </summary>
    public long? MonthlyIncomeCents { get; set; }

    /// <summary>
    /// Lúc người dùng chứng minh họ thật sự sở hữu email này. NULL = chưa xác minh.
    ///
    /// Trước Phase 17 không có trường nào như vậy, nên ai cũng đăng ký được bằng email của
    /// người khác. Google login đặt luôn giá trị này — Google đã xác minh hộ rồi.
    /// </summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public NotificationPreferences NotificationPrefs { get; set; } = new();
    public bool IsLocked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<FinancialAccount> FinancialAccounts { get; set; } = new List<FinancialAccount>();
}
