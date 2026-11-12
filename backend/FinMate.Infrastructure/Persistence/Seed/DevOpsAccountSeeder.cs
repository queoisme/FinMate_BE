using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Tạo sẵn tài khoản Staff / OpsManager cho môi trường DEV để đăng nhập thử web vận hành mà không phải
/// đăng ký rồi nhờ Admin cấp vai trò. Danh sách và mật khẩu nằm trong cấu hình, không nằm trong code:
/// <c>DEV_SEED_OPS_ACCOUNTS=staff@finmate.local=Staff,manager@finmate.local=OpsManager</c> và <c>DEV_SEED_OPS_PASSWORD</c>.
/// Chỉ được gọi khi chạy ở Development (xem Program.cs). Chỉ TẠO tài khoản chưa có: không bao giờ đặt lại mật khẩu,
/// đổi vai trò hay mở khóa, để thay đổi của Admin trên trang Phân quyền không bị khởi động lại ghi đè.
/// Mật khẩu ở đây bỏ qua chính sách mật khẩu của đăng ký, nên tuyệt đối không dùng ngoài dev.
/// </summary>
public static class DevOpsAccountSeeder
{
    public const string AccountsKey = "DEV_SEED_OPS_ACCOUNTS";
    public const string PasswordKey = "DEV_SEED_OPS_PASSWORD";

    public static async Task<int> SeedAsync(
        FinMateDbContext context,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        CancellationToken ct = default)
    {
        var accounts = configuration[AccountsKey];
        var password = configuration[PasswordKey];
        if (string.IsNullOrWhiteSpace(accounts) || string.IsNullOrWhiteSpace(password))
        {
            return 0;
        }

        var created = 0;
        foreach (var (email, role) in Parse(accounts))
        {
            if (await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            context.Users.Add(new User
            {
                Email = email,
                PasswordHash = passwordHasher.Hash(password),
                DisplayName = role == UserRole.OpsManager ? "Manager" : role.ToString(),
                Role = role,
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
            created++;
        }

        if (created > 0)
        {
            await context.SaveChangesAsync(ct);
        }

        return created;
    }

    /// <summary>Sai định dạng thì ném lỗi ngay lúc khởi động: cấu hình dev sai không nên âm thầm bỏ qua tài khoản.</summary>
    internal static IEnumerable<(string Email, UserRole Role)> Parse(string accounts)
    {
        foreach (var item in accounts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || !Enum.TryParse<UserRole>(parts[1], ignoreCase: true, out var role) || role is UserRole.User)
            {
                throw new InvalidOperationException($"{AccountsKey}: '{item}' không hợp lệ, cần dạng email=Staff|OpsManager|Admin.");
            }

            yield return (parts[0], role);
        }
    }
}
