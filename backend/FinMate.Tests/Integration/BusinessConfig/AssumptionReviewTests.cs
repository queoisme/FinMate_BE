using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration.BusinessConfig;

/// <summary>Phase 95 §8: giả định kinh tế không tự duyệt được, quyền duyệt đọc từ DB, và có "từ chối kèm ghi chú". Job CPI vẫn tự duyệt.</summary>
[Collection("Integration")]
public class AssumptionReviewTests : IClassFixture<AuthApiFactory>, IClassFixture<ReceiptScanDbFixture>
{
    private const string Base = "/api/v1/admin/assumptions";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthApiFactory _factory;
    private readonly ReceiptScanDbFixture _db;
    private readonly HttpClient _client;

    public AssumptionReviewTests(AuthApiFactory factory, ReceiptScanDbFixture db)
    {
        _factory = factory;
        _db = db;
        _client = factory.CreateClient();
    }

    private string Token(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"rv{Guid.NewGuid():N}@finmate.local", DisplayName = "Nhân sự", Role = role, CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        db.SaveChanges();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return _client.SendAsync(request);
    }

    private async Task<Guid> DraftAsync(string author)
    {
        var response = await SendAsync(HttpMethod.Post, Base, author, new
        {
            key = $"rv_{Guid.NewGuid():N}"[..20], rateBps = 450, unit = "pct_per_year", sourceName = "GSO", sourceUrl = "https://gso.gov.vn/cpi", asOf = DateTimeOffset.UtcNow,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    [Fact]
    public async Task AManagerCannotApproveOrRejectTheirOwnDraft_AnotherManagerAndAnAdminCan()
    {
        var manager = Token(UserRole.OpsManager);
        var own = await DraftAsync(manager);

        var self = await SendAsync(HttpMethod.Post, $"{Base}/{own}/approve", manager);
        self.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(self)).Should().Be("ASSUMPTION_OWN_DRAFT");
        (await SendAsync(HttpMethod.Post, $"{Base}/{own}/reject", manager, new { note = "tự từ chối" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await SendAsync(HttpMethod.Post, $"{Base}/{own}/approve", Token(UserRole.OpsManager))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"{Base}/{await DraftAsync(manager)}/approve", Token(UserRole.Admin))).StatusCode.Should().Be(HttpStatusCode.OK, "S5: Admin duyệt bản của người khác");
    }

    [Fact]
    public async Task RejectingNeedsANote_AndLeavesTheDraftRejectedWithIt()
    {
        var staff = Token(UserRole.Staff);
        var manager = Token(UserRole.OpsManager);
        var draft = await DraftAsync(staff);

        (await SendAsync(HttpMethod.Post, $"{Base}/{draft}/reject", manager, new { note = " " })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Post, $"{Base}/{draft}/reject", staff, new { note = "nguồn cũ" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "Staff không có quyền duyệt");
        var rejected = await SendAsync(HttpMethod.Post, $"{Base}/{draft}/reject", manager, new { note = "nguồn đã cũ, cập nhật số tháng này" });

        rejected.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("rejected");
        data.GetProperty("decisionNote").GetString().Should().Be("nguồn đã cũ, cập nhật số tháng này");
        (await SendAsync(HttpMethod.Post, $"{Base}/{draft}/approve", manager)).StatusCode.Should().Be(HttpStatusCode.Conflict, "bản đã từ chối không duyệt được");
    }

    [Fact]
    public async Task RemovingTheCapabilityStopsTheSameTokenAtOnce()
    {
        var staff = Token(UserRole.Staff);
        var manager = Token(UserRole.OpsManager);
        var admin = Token(UserRole.Admin);
        var draft = await DraftAsync(staff);
        var defaults = OpsPermissions.Names(OpsPermissions.For(UserRole.OpsManager));
        (await SendAsync(HttpMethod.Put, "/api/v1/admin/role-permissions/OpsManager", admin,
            new { capabilities = defaults.Where(c => c != nameof(OpsCapability.ApproveAgentContent)).ToList(), reason = "kiểm thử" })).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            (await SendAsync(HttpMethod.Post, $"{Base}/{draft}/approve", manager)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "vai trò trong JWT vẫn là OpsManager nhưng quyền đọc từ DB");
        }
        finally
        {
            await SendAsync(HttpMethod.Put, "/api/v1/admin/role-permissions/OpsManager", admin, new { capabilities = defaults, reason = "trả lại" });
        }
    }

    [Fact]
    public async Task TheCpiJobStillApprovesItsOwnDraft_AndTheDatabaseRefusesAnyoneElseDoingSo()
    {
        await using var db = _db.NewContext(_db.ConnectionStringFor($"asm_{Guid.NewGuid():N}"));
        await db.Database.MigrateAsync();
        var access = new Mock<IOpsAccessService>(MockBehavior.Strict);   // hệ thống không đi qua kiểm quyền người
        var service = new AssumptionService(new EconomicAssumptionRepository(db), Mock.Of<IAuditLogService>(), access.Object);
        var system = AiCategorySyncService.SystemActorId;

        var draft = await service.CreateDraftAsync(system, "cpi_yoy", 350, "pct_per_year", "GSO", "https://gso.gov.vn", DateTimeOffset.UtcNow, null);
        (await service.ApproveAsync(draft.Id, system, null)).Status.Should().Be(AssumptionStatus.Approved);

        var person = Guid.NewGuid();
        db.EconomicAssumptions.Add(new EconomicAssumption
        {
            Id = Guid.NewGuid(), Key = "cpi_yoy", RateBps = 360, Unit = "pct_per_year", SourceName = "GSO", SourceUrl = "https://gso.gov.vn", AsOf = DateTimeOffset.UtcNow,
            Version = 9, Status = AssumptionStatus.Approved, CreatedBy = person, ApprovedBy = person, CreatedAt = DateTimeOffset.UtcNow,
        });
        var act = () => db.SaveChangesAsync();
        (await act.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("chk_economic_assumptions_two_people");
    }

    /// <summary>
    /// Dòng tự duyệt có từ trước Phase 95 vẫn phải sửa được (gỡ, đổi trạng thái): luật "hai người" chỉ được kiểm khi người duyệt / từ chối được GÁN,
    /// không phải mọi lần UPDATE. Postgres kiểm cả CHECK NOT VALID ở mọi UPDATE, nên luật này không thể là CHECK.
    /// </summary>
    [Fact]
    public async Task ALegacySelfApprovedRowCanStillBeRetired_ButANewSelfApprovalIsRefused()
    {
        await using var db = _db.NewContext(_db.ConnectionStringFor($"asmlegacy_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261126193325_AddForecastConfigDrafts");
        var legacyId = Guid.NewGuid();
        var person = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO economic_assumptions (id, key, rate_bps, unit, source_name, source_url, as_of, version, status, created_by, approved_by, created_at)
            VALUES ({legacyId}, 'gold_price_growth', 500, 'pct_per_year', 'SJC', 'https://sjc.com.vn', now(), 1, 'approved', {person}, {person}, now())
            """);
        await migrator.MigrateAsync();

        var access = new Mock<IOpsAccessService>();
        access.Setup(a => a.RequireAsync(It.IsAny<Guid>(), It.IsAny<OpsCapability>(), It.IsAny<CancellationToken>())).ReturnsAsync(new User());
        var service = new AssumptionService(new EconomicAssumptionRepository(db), Mock.Of<IAuditLogService>(), access.Object);

        (await service.RetireAsync(legacyId, Guid.NewGuid(), null)).Status.Should().Be(AssumptionStatus.Retired, "dòng cũ tự duyệt vẫn gỡ được");

        var fresh = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO economic_assumptions (id, key, rate_bps, unit, source_name, source_url, as_of, version, status, created_by, created_at)
            VALUES ({fresh}, 'gold_price_growth', 520, 'pct_per_year', 'SJC', 'https://sjc.com.vn', now(), 2, 'draft', {person}, now())
            """);
        var selfApprove = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE economic_assumptions SET status = 'approved', approved_by = {person} WHERE id = {fresh}");
        (await selfApprove.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
    }
}
