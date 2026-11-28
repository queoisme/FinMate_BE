namespace FinMate.API.Authorization;

public static class AuthorizationPolicies
{
    /// <summary>
    /// Khớp <c>ClaimTypes.Role</c> mà <c>TokenService</c> gắn lúc đăng nhập.
    ///
    /// Lưu ý vận hành: vai trò nằm TRONG access token, không tra lại DB mỗi request. Khoá tài khoản
    /// hay hạ quyền vì vậy phải làm hai việc: thu hồi refresh token (chặn gia hạn) và đánh dấu
    /// <c>IAccessTokenRevocationStore</c> (token cấp trước mốc bị từ chối ngay ở <c>OnTokenValidated</c>);
    /// xem <c>SetUserLockCommandHandler</c>, <c>SetUserRoleCommandHandler</c>. Nếu Redis sập thì phép
    /// kiểm tra cho qua (fail-open) và cửa sổ <c>JWT_ACCESS_TTL_MINUTES</c> (15 phút) quay lại.
    /// Các đường "thu hồi hết phiên" khác (đổi/đặt lại mật khẩu, đăng xuất mọi thiết bị, xoá tài khoản)
    /// chưa đánh dấu access token.
    /// </summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>
    /// Staff, OpsManager hoặc Admin. Cũng đọc role trong token nên chỉ dùng để chặn sớm; thao tác nhạy cảm phải qua
    /// <c>IOpsAccessService</c> để người vừa bị hạ quyền hoặc khoá mất quyền ngay.
    /// </summary>
    public const string StaffOrAbove = "StaffOrAbove";

    /// <summary>OpsManager hoặc Admin. Cùng lưu ý với <see cref="StaffOrAbove"/>.</summary>
    public const string ManagerOrAbove = "ManagerOrAbove";

    /// <summary>Có vai trò nội bộ nhưng chưa cần qua OTP. Chỉ dùng cho chính bước nhập OTP; mọi policy khác đều yêu cầu đã qua bước này.</summary>
    public const string InternalAccount = "InternalAccount";

    /// <summary>Phiên thanh toán của website (<c>X-Billing-Session</c>). Cố ý không nhận JWT, và phiên này không mở được API nào khác.</summary>
    public const string BillingSession = "BillingSession";
}
