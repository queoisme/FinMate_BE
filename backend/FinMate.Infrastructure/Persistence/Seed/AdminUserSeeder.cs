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

        var exists = await context.Users.AnyAsync(u => u.Email == email, ct);
        if (exists)
        {
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
