using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Tài khoản seed là cửa thoát hiểm khi không còn ai quản trị được.
///
/// Trước Phase 13 seeder chỉ hỏi "email này có chưa" rồi thôi — nên một admin seed bị hạ quyền
/// thì sửa biến môi trường và khởi động lại cũng không cứu được gì, tức là đường cứu duy nhất
/// chỉ tồn tại trên giấy. Phát hiện khi verify thủ công, không phải do test nào bắt được.
/// </summary>
[Collection("Integration")]
public class AdminSeederRecoveryTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public AdminSeederRecoveryTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    private static IConfiguration SeedConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ADMIN_SEED_EMAIL"] = AuthApiFactory.AdminEmail,
            ["ADMIN_SEED_PASSWORD"] = AuthApiFactory.AdminPassword,
        }).Build();

    /// <summary>Làm hỏng tài khoản seed, chạy lại seeder, trả về trạng thái sau đó.</summary>
    private async Task<User> BreakThenReseedAsync(Action<User> break_)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var admin = await db.Users
            .IgnoreQueryFilters()
            .SingleAsync(u => u.Email == AuthApiFactory.AdminEmail);

        break_(admin);
        await db.SaveChangesAsync();

        await AdminUserSeeder.SeedAsync(db, hasher, SeedConfig());

        return await db.Users
            .IgnoreQueryFilters()
            .SingleAsync(u => u.Email == AuthApiFactory.AdminEmail);
    }

    [Fact]
    public async Task RestoresTheRoleAfterSomeoneDemotedIt()
    {
        var admin = await BreakThenReseedAsync(u => u.Role = UserRole.User);

        admin.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task UnlocksItAfterSomeoneLockedIt()
    {
        var admin = await BreakThenReseedAsync(u => u.IsLocked = true);

        admin.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task BringsItBackFromAPendingDeletion()
    {
        var admin = await BreakThenReseedAsync(u => u.DeletedAt = DateTimeOffset.UtcNow);

        admin.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task NeverResetsThePassword()
    {
        // Đổi mật khẩu admin là chuyện bình thường; ghi đè mỗi lần khởi động sẽ âm thầm trả nó
        // về giá trị trong biến môi trường.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var before = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Email == AuthApiFactory.AdminEmail)
            .Select(u => u.PasswordHash)
            .SingleAsync();

        var admin = await BreakThenReseedAsync(u => u.Role = UserRole.User);

        admin.PasswordHash.Should().Be(before);
    }
}
