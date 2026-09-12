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
}
