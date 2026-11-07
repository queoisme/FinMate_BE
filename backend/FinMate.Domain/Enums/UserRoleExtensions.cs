namespace FinMate.Domain.Enums;

public static class UserRoleExtensions
{
    /// <summary>
    /// <paramref name="role"/> có làm được MỌI việc mà <paramref name="other"/> làm được không? Admin làm được tất cả; Manager làm được mọi việc của nhân sự và người dùng; hai nhóm nhân sự
    /// (nội dung, hỗ trợ) là hai việc khác nhau nên không bao hàm nhau. Dùng để quyết định có thu hồi refresh token khi đổi vai trò (đổi sang vai trò KHÔNG bao hàm vai trò cũ là mất quyền).
    /// </summary>
    public static bool Covers(this UserRole role, UserRole other)
        => role == other
           || role == UserRole.Admin
           || (role == UserRole.Manager && other != UserRole.Admin)
           || (other == UserRole.User);
}
