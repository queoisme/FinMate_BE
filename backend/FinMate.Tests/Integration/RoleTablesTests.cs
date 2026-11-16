using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.API.Controllers.Admin;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>Vai trò nằm ở bảng <c>roles</c> + <c>user_roles</c> (mỗi người đúng một hàng), không còn cột <c>users.role</c>.</summary>
[Collection("Integration")]
public class RoleTablesTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "Password123!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public RoleTablesTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record TokenData(string AccessToken);
    private record TokenBody(TokenData Data);
    private record IdData(Guid Id);
    private record IdBody(IdData Data);

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword));
        return (await response.Content.ReadFromJsonAsync<TokenBody>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<Guid> RegisterAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, "Role Test"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        var token = (await login.Content.ReadFromJsonAsync<TokenBody>(JsonOptions))!.Data.AccessToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<IdBody>(JsonOptions))!.Data.Id;
    }

    private FinMateDbContext Db() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FinMateDbContext>();

    [Fact]
    public async Task TheFourSystemRolesAreSeededWithTheEnumValuesAsIds()
    {
        var db = Db();

        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync();

        roles.Select(r => (r.Id, r.Code)).Should().Equal(
            (UserRole.User, "user"), (UserRole.Admin, "admin"), (UserRole.Staff, "staff"), (UserRole.OpsManager, "opsmanager"));
        roles.Should().OnlyContain(r => r.IsSystem);
    }

    [Fact]
    public async Task EveryNewUserGetsExactlyOneRoleRow()
    {
        var id = await RegisterAsync();
        var db = Db();

        var rows = await db.UserRoleAssignments.AsNoTracking().Where(a => a.UserId == id).ToListAsync();

        rows.Should().ContainSingle().Which.RoleId.Should().Be(UserRole.User);
    }

    [Fact]
    public async Task ChangingARoleUpdatesTheSameRowAndRecordsWhoGaveIt()
    {
        var admin = await AdminTokenAsync();
        var id = await RegisterAsync();
        var adminId = await Db().Users.AsNoTracking().Where(u => u.Email == AuthApiFactory.AdminEmail).Select(u => u.Id).SingleAsync();

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/admin/users/{id}/role") { Content = JsonContent.Create(new SetUserRoleRequest(UserRole.Staff, "kiểm thử"), options: JsonOptions) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await Db().UserRoleAssignments.AsNoTracking().Where(a => a.UserId == id).ToListAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.RoleId.Should().Be(UserRole.Staff);
        row.AssignedByUserId.Should().Be(adminId);
    }

    [Fact]
    public async Task DeletingAUserRemovesTheirRoleRow()
    {
        var id = await RegisterAsync();
        var db = Db();

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {id}");

        (await db.UserRoleAssignments.AsNoTracking().AnyAsync(a => a.UserId == id)).Should().BeFalse();
    }

    [Fact]
    public async Task ARoleThatDoesNotExistCannotBeAssignedOrGivenPermissions()
    {
        var id = await RegisterAsync();
        var db = Db();

        var assign = async () => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE user_roles SET role_id = 99 WHERE user_id = {id}");
        var permit = async () => await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO role_permissions (role_id, capabilities, updated_at) VALUES (99, 1, now())");

        (await assign.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23503");
        (await permit.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23503");
    }

    [Fact]
    public async Task TheUsersTableNoLongerHasARoleColumn()
    {
        var count = await Db().Database.SqlQuery<long>($"""
            SELECT count(*) AS "Value" FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'users' AND column_name = 'role'
            """).SingleAsync();

        count.Should().Be(0);
    }
}

/// <summary>Migration <c>NormalizeRoles</c> trên dữ liệu có sẵn: vai trò cũ được chép đúng, và Down trả lại cột cũ.</summary>
public class NormalizeRolesMigrationTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string MigrationBefore = "20261105202450_AddRolePermissions";
    private readonly ReceiptScanDbFixture _fixture;

    public NormalizeRolesMigrationTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<Guid> InsertUserAsync(FinMateDbContext db, string role)
    {
        var id = Guid.NewGuid();
        var email = $"{id:N}@finmate.local";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, email, display_name, role, push_enabled, budget_alerts_enabled, created_at, updated_at)
            VALUES ({id}, {email}, 'Migration Test', {role}, true, true, now(), now())
            """);
        return id;
    }

    [Fact]
    public async Task CopiesEveryExistingRole_RewiresRolePermissions_AndGoesBackDown()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"roles_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);

        var user = await InsertUserAsync(db, "user");
        var admin = await InsertUserAsync(db, "admin");
        var staff = await InsertUserAsync(db, "staff");
        var manager = await InsertUserAsync(db, "opsmanager");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO role_permissions (role, capabilities, updated_at) VALUES ('staff', 31, now()), ('opsmanager', 14335, now())");

        await migrator.MigrateAsync();

        async Task<int> RoleIdOf(Guid id) => await db.Database.SqlQuery<int>($"""SELECT role_id AS "Value" FROM user_roles WHERE user_id = {id}""").SingleAsync();
        (await RoleIdOf(user)).Should().Be(0);
        (await RoleIdOf(admin)).Should().Be(1);
        (await RoleIdOf(staff)).Should().Be(2);
        (await RoleIdOf(manager)).Should().Be(3);
        (await db.Database.SqlQuery<long>($"""SELECT capabilities AS "Value" FROM role_permissions WHERE role_id = 2""").SingleAsync()).Should().Be(31);
        (await db.Database.SqlQuery<long>($"""SELECT capabilities AS "Value" FROM role_permissions WHERE role_id = 3""").SingleAsync()).Should().Be(14335);

        await migrator.MigrateAsync(MigrationBefore);

        (await db.Database.SqlQuery<string>($"""SELECT role AS "Value" FROM users WHERE id = {admin}""").SingleAsync()).Should().Be("admin");
        (await db.Database.SqlQuery<string>($"""SELECT role AS "Value" FROM users WHERE id = {manager}""").SingleAsync()).Should().Be("opsmanager");
        (await db.Database.SqlQuery<string>($"""SELECT role AS "Value" FROM role_permissions WHERE capabilities = 31""").SingleAsync()).Should().Be("staff");
    }
}
