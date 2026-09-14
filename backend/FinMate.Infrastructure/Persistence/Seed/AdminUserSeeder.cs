using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FinMate.Infrastructure.Persistence.Seed;

public static class AdminUserSeeder
{
    public static async Task SeedAsync(
        FinMateDbContext context,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        CancellationToken ct = default)
    {
        var email = configuration["ADMIN_SEED_EMAIL"];
        var password = configuration["ADMIN_SEED_PASSWORD"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var existing = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (existing is not null)
        {
            // Cửa thoát hiểm. Tài khoản này là đường cứu cuối cùng khi không còn ai quản trị
            // được, nên mỗi lần khởi động phải bảo đảm nó THỰC SỰ dùng được: chỉ kiểm tra
            // "email đã tồn tại chưa" là để lọt trường hợp nó bị hạ quyền, bị khoá hoặc đang
            // chờ xoá — lúc đó sửa biến môi trường rồi khởi động lại cũng không cứu được gì.
            // (Phát hiện khi verify Phase 13: hạ quyền admin seed xong restart không khôi phục.)
            var repaired = false;

            if (existing.Role != UserRole.Admin)
            {
                existing.Role = UserRole.Admin;
                repaired = true;
            }

            if (existing.IsLocked)
            {
                existing.IsLocked = false;
                repaired = true;
            }

            if (existing.DeletedAt is not null)
            {
                existing.DeletedAt = null;
                repaired = true;
            }

            if (repaired)
            {
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(ct);
            }

            // Mật khẩu KHÔNG đặt lại: admin đổi mật khẩu là chuyện bình thường, ghi đè mỗi lần
            // khởi động sẽ âm thầm trả nó về giá trị trong biến môi trường.
            return;
        }

        var now = DateTimeOffset.UtcNow;
        context.Users.Add(new User
        {
            Email = email,
            PasswordHash = passwordHasher.Hash(password),
            DisplayName = "Admin",
            Role = UserRole.Admin,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await context.SaveChangesAsync(ct);
    }
}
