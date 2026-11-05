namespace FinMate.API.Authorization;

public static class AuthorizationPolicies
{
    /// <summary>
    /// Khớp <c>ClaimTypes.Role</c> mà <c>TokenService</c> gắn lúc đăng nhập.
    ///
    /// Lưu ý vận hành: vai trò nằm TRONG access token, không tra lại DB mỗi request. Hạ quyền
    /// hay khoá một admin không vô hiệu hoá token họ đang cầm — họ còn vào được tối đa
    /// <c>JWT_ACCESS_TTL_MINUTES</c> (15 phút). Vì vậy lệnh khoá tài khoản còn phải thu hồi
    /// refresh token để chặn gia hạn; xem <c>SetUserLockCommandHandler</c>.
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
}
