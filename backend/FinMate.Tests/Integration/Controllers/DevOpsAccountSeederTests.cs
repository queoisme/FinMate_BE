using System.Net;
using System.Net.Http.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Tài khoản Staff/OpsManager dựng sẵn cho DEV: đúng vai trò, đăng nhập được với mật khẩu cấu hình, và không bị ghi đè khi khởi động lại.</summary>
[Collection("Integration")]
public class DevOpsAccountSeederTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "123456";
    private readonly AuthApiFactory _factory;

    public DevOpsAccountSeederTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    private static IConfiguration Config(string accounts) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DevOpsAccountSeeder.AccountsKey] = accounts,
            [DevOpsAccountSeeder.PasswordKey] = Password,
        }).Build();

    [Fact]
    public async Task CreatesAccountsWithTheirRolesAndTheyCanSignIn()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var created = await DevOpsAccountSeeder.SeedAsync(db, hasher, Config("dev-staff@finmate.local=Staff, dev-manager@finmate.local=OpsManager"));

        created.Should().Be(2);
        (await db.Users.AsNoTracking().SingleAsync(u => u.Email == "dev-staff@finmate.local")).Role.Should().Be(UserRole.Staff);
        (await db.Users.AsNoTracking().SingleAsync(u => u.Email == "dev-manager@finmate.local")).Role.Should().Be(UserRole.OpsManager);
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("dev-staff@finmate.local", Password));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NeverOverwritesAnExistingAccount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await DevOpsAccountSeeder.SeedAsync(db, hasher, Config("dev-keep@finmate.local=Staff"));
        var user = await db.Users.SingleAsync(u => u.Email == "dev-keep@finmate.local");
        user.Role = UserRole.OpsManager;   // Admin đã đổi vai trò trên trang Phân quyền
        await db.SaveChangesAsync();

        var created = await DevOpsAccountSeeder.SeedAsync(db, hasher, Config("dev-keep@finmate.local=Staff"));

        created.Should().Be(0);
        (await db.Users.AsNoTracking().SingleAsync(u => u.Email == "dev-keep@finmate.local")).Role.Should().Be(UserRole.OpsManager);
    }

    [Theory]
    [InlineData("khong-co-vai-tro")]
    [InlineData("a@b.vn=KhongPhaiVaiTro")]
    [InlineData("a@b.vn=User")]
    public void RejectsMalformedEntriesLoudly(string accounts)
    {
        var act = () => DevOpsAccountSeeder.Parse(accounts).ToList();

        act.Should().Throw<InvalidOperationException>();
    }
}
