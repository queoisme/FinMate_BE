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

    /// <summary>
    /// Vai trò được gán (bảng <c>user_roles</c>, nạp tự động cùng user). Không phải cột của <c>users</c>.
    /// Null nghĩa là chưa có hàng gán; khi đó <see cref="Role"/> trả <see cref="UserRole.User"/> (thiếu dữ liệu thì mất quyền chứ không thừa quyền).
    /// </summary>
    public UserRoleAssignment? RoleAssignment { get; set; }

    /// <summary>Vai trò hiện tại. Gán qua đây hoặc <see cref="AssignRole"/>; EF lưu thành hàng trong <c>user_roles</c> (điền <c>UserId</c> lúc lưu).</summary>
    public UserRole Role
    {
        get => RoleAssignment?.RoleId ?? UserRole.User;
        set => AssignRole(value, null);
    }

    /// <summary>Đổi vai trò và ghi lại ai cấp, lúc nào. Mỗi người luôn đúng một hàng gán.</summary>
    public void AssignRole(UserRole role, Guid? assignedByUserId)
    {
        var now = DateTimeOffset.UtcNow;
        if (RoleAssignment is null)
        {
            RoleAssignment = new UserRoleAssignment { RoleId = role, AssignedAt = now, AssignedByUserId = assignedByUserId };
            return;
        }

        RoleAssignment.RoleId = role;
        RoleAssignment.AssignedAt = now;
        RoleAssignment.AssignedByUserId = assignedByUserId;
    }

    /// <summary>
    /// Thu nhập hằng tháng DỰ KIẾN, do người dùng khai ở onboarding (docx Bước 1.3).
    ///
    /// Khác hẳn tổng giao dịch <c>Credit</c> thực tế trong báo cáo: tháng đầu dùng app thì
    /// tổng đó bằng 0, đúng lúc người dùng cần cảnh báo bội chi nhất. NULL = chưa khai, và
    /// mọi nơi dùng nó phải BỎ QUA phép so sánh thay vì thay bằng 0 — "chưa biết" không phải
    /// là "không có thu nhập".
    /// </summary>
    public long? MonthlyIncomeCents { get; set; }

    /// <summary>Khoản dự phòng user tự đặt (đồng) cho "số tiền có thể chi". Null = dùng mặc định hệ thống (% thu nhập dự kiến).</summary>
    public long? ForecastReserveCents { get; set; }

    /// <summary>Báo khi số dư dự kiến xuống dưới mức này (đồng). Null = tắt cảnh báo số dư thấp.</summary>
    public long? LowBalanceThresholdCents { get; set; }

    /// <summary>Giờ yên lặng theo phút trong ngày (giờ VN, 0–1439), có thể qua nửa đêm. Cả hai null = dùng mặc định hệ thống.</summary>
    public short? QuietHoursStartMinute { get; set; }
    public short? QuietHoursEndMinute { get; set; }

    /// <summary>
    /// Người dùng tự chọn nhóm thu nhập cho agent: <c>"stable"</c> hoặc <c>"variable"</c>. NULL = để hệ thống tự suy từ độ biến động lịch sử thu nhập.
    /// Có tác dụng thắng giá trị tự suy (người dùng biết hoàn cảnh của mình hơn số liệu), nhưng không thay được số liệu: dải dự báo vẫn lấy từ lịch sử thật.
    /// </summary>
    public string? IncomeProfileOverride { get; set; }

    /// <summary>
    /// Lúc người dùng chứng minh họ thật sự sở hữu email này. NULL = chưa xác minh.
    ///
    /// Trước Phase 17 không có trường nào như vậy, nên ai cũng đăng ký được bằng email của
    /// người khác. Google login đặt luôn giá trị này — Google đã xác minh hộ rồi.
    /// </summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    /// <summary>Phiên bản Điều khoản + Chính sách quyền riêng tư mà người dùng đã đồng ý, và lúc đồng ý. NULL = chưa đồng ý.</summary>
    public int? TermsVersion { get; set; }
    public DateTimeOffset? TermsAcceptedAt { get; set; }

    /// <summary>
    /// Phiên bản đồng ý gửi TÊN MÓN / cụm sản phẩm cho AI bên thứ ba, và lúc đồng ý. Được bật khi đồng ý Điều khoản (điều khoản đã nêu việc này) hoặc bằng công tắc
    /// trong Hồ sơ. NULL = chưa bao giờ quyết định; <see cref="ConsentVersions.AiOptedOut"/> = chủ động tắt (đồng ý điều khoản mới không bật lại).
    /// Không có đồng ý đúng phiên bản thì không chia sẻ. Chỉ chính chủ đổi được, qua POST/DELETE /users/me/consents.
    /// </summary>
    public int? AiConsentVersion { get; set; }
    public DateTimeOffset? AiConsentAt { get; set; }

    /// <summary>Đồng ý đóng góp dữ liệu huấn luyện (chữ hoá đơn đã lọc + nhãn cuối). NULL = chưa đồng ý hoặc đã rút lại. Tách biệt với đồng ý AI.</summary>
    public int? TrainingConsentVersion { get; set; }
    public DateTimeOffset? TrainingConsentAt { get; set; }

    /// <summary>Đồng ý gửi signals từ khảo sát đầu vào cho AI bên thứ ba (xem <see cref="ConsentVersions.SurveyAi"/>). NULL = chưa đồng ý hoặc đã rút lại. Tách biệt với đồng ý AI về tên món.</summary>
    public int? SurveyAiConsentVersion { get; set; }
    public DateTimeOffset? SurveyAiConsentAt { get; set; }

    /// <summary>Khu vực người dùng tự chọn, dùng làm mặc định khi đọc và đăng bài về quán. Không dùng GPS.</summary>
    public string? CommunityProvinceCode { get; set; }
    public string? CommunityAreaCode { get; set; }

    /// <summary>Tắt thì app không hỏi chia sẻ sau khi ghi giao dịch.</summary>
    public bool CommunityPromptsEnabled { get; set; } = true;

    /// <summary>Đồng ý đăng nội dung công khai. Chỉ cần khi đăng, xem thì không cần.</summary>
    public int? CommunityConsentVersion { get; set; }
    public DateTimeOffset? CommunityConsentAt { get; set; }

    /// <summary>Bị cấm đăng tới mốc này. Vẫn đọc được.</summary>
    public DateTimeOffset? CommunityMutedUntil { get; set; }

    /// <summary>Chỉ tính đồng ý ở đúng phiên bản hiện hành: câu chữ đã đổi thì phải đồng ý lại.</summary>
    public bool TermsAccepted => TermsVersion == ConsentVersions.Terms;
    public bool AiConsentGranted => AiConsentVersion == ConsentVersions.Ai;
    public bool TrainingConsentGranted => TrainingConsentVersion == ConsentVersions.Training;
    public bool SurveyAiConsentGranted => SurveyAiConsentVersion == ConsentVersions.SurveyAi;
    public bool CommunityConsentGranted => CommunityConsentVersion == ConsentVersions.Community;

    /// <summary>
    /// Khảo sát đầu vào đã làm hay bỏ qua chưa. Tài khoản có TRƯỚC tính năng này được migration đặt
    /// <see cref="OnboardingStatus.Completed"/> để không bắt họ làm lại; chỉ tài khoản mới mặc định <see cref="OnboardingStatus.Pending"/>.
    /// </summary>
    public OnboardingStatus OnboardingStatus { get; set; } = OnboardingStatus.Pending;
    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    public NotificationPreferences NotificationPrefs { get; set; } = new();
    public bool IsLocked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<FinancialAccount> FinancialAccounts { get; set; } = new List<FinancialAccount>();
}
