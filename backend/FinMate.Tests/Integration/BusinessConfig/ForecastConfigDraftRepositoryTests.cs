using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FinMate.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration.BusinessConfig;

/// <summary>Phase 95 §6.1: bản nháp nằm trong <c>system_config_versions</c> mà không chiếm số phiên bản, không lọt vào lịch sử hay runtime, và DB tự chặn tự duyệt.</summary>
public class ForecastConfigDraftRepositoryTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string App = "app";
    private const string Scope = "forecast";
    private readonly ReceiptScanDbFixture _fixture;

    public ForecastConfigDraftRepositoryTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<FinMateDbContext> FreshAsync()
    {
        var db = _fixture.NewContext(_fixture.ConnectionStringFor($"drafts_{Guid.NewGuid():N}"));
        await db.Database.MigrateAsync();
        return db;
    }

    private static SystemConfigVersion Draft(Guid author, int baseVersion, SystemConfigStatus status = SystemConfigStatus.Draft) => new()
    {
        Id = Guid.NewGuid(), Name = App, Scope = Scope, Status = status, BaseVersion = baseVersion, Content = """{"forecast":{},"limits":{}}""",
        Note = "đổi cửa sổ", CreatedBy = author, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<int> ActivateAdminAsync(SystemConfigRepository repo, int expected)
    {
        var next = new SystemConfigVersion { Id = Guid.NewGuid(), Name = App, Status = SystemConfigStatus.Active, Content = "{}", Note = "admin", CreatedBy = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        (await repo.TryActivateNextAsync(next, expected)).Should().BeTrue();
        return next.Version!.Value;
    }

    [Fact]
    public async Task DraftsHaveNoVersion_AndStayOutOfHistoryAndRuntime()
    {
        await using var db = await FreshAsync();
        var repo = new SystemConfigRepository(db);
        var v1 = await ActivateAdminAsync(repo, 0);
        await repo.AddDraftAsync(Draft(Guid.NewGuid(), v1));
        await repo.AddDraftAsync(Draft(Guid.NewGuid(), v1, SystemConfigStatus.PendingApproval));

        (await repo.GetActiveAsync(App))!.Version.Should().Be(v1);
        var (history, total) = await repo.ListAsync(App, 50, 0);
        total.Should().Be(1);
        history.Should().OnlyContain(v => v.Version != null);
        (await repo.ListDraftsAsync(App, Scope, null, 0, 50)).Total.Should().Be(2);
    }

    [Fact]
    public async Task OnlyOnePendingPerScope()
    {
        await using var db = await FreshAsync();
        var repo = new SystemConfigRepository(db);
        await repo.AddDraftAsync(Draft(Guid.NewGuid(), 0, SystemConfigStatus.PendingApproval));
        var second = Draft(Guid.NewGuid(), 0);
        await repo.AddDraftAsync(second);

        var tracked = await repo.GetDraftAsync(App, second.Id);
        tracked!.Status = SystemConfigStatus.PendingApproval;

        (await repo.TrySaveDraftChangesAsync()).Should().BeFalse("chỉ mục một-bản-chờ-duyệt chặn bản thứ hai");
    }

    [Fact]
    public async Task TheDatabaseRefusesSelfApproval()
    {
        await using var db = await FreshAsync();
        var author = Guid.NewGuid();
        var draft = Draft(author, 0);
        draft.DecidedBy = author;
        db.SystemConfigVersions.Add(draft);

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("chk_system_config_versions_two_people");
    }

    [Fact]
    public async Task ActivatingADraft_TurnsThatRowIntoTheNextVersion_AndArchivesTheOldOne()
    {
        await using var db = await FreshAsync();
        var repo = new SystemConfigRepository(db);
        var v1 = await ActivateAdminAsync(repo, 0);
        var draft = Draft(Guid.NewGuid(), v1, SystemConfigStatus.PendingApproval);
        await repo.AddDraftAsync(draft);
        draft.DecidedBy = Guid.NewGuid();
        draft.DecidedAt = DateTimeOffset.UtcNow;

        (await repo.TryActivateDraftAsync(draft, """{"forecast":{"historyWindowDays":120}}""")).Should().BeTrue();

        var active = await repo.GetActiveAsync(App);
        active!.Id.Should().Be(draft.Id);
        active.Version.Should().Be(v1 + 1);
        active.Content.Should().Contain("120");
        (await repo.GetVersionAsync(App, v1))!.Status.Should().Be(SystemConfigStatus.Archived);
    }

    [Fact]
    public async Task ADraftBuiltOnAnOlderVersionIsNotActivated()
    {
        await using var db = await FreshAsync();
        var repo = new SystemConfigRepository(db);
        var v1 = await ActivateAdminAsync(repo, 0);
        var draft = Draft(Guid.NewGuid(), v1, SystemConfigStatus.PendingApproval);
        await repo.AddDraftAsync(draft);
        var v2 = await ActivateAdminAsync(repo, v1);
        draft.DecidedBy = Guid.NewGuid();

        (await repo.TryActivateDraftAsync(draft, "{}")).Should().BeFalse("bản gốc không còn là bản đang chạy: không tự gộp");

        db.ChangeTracker.Clear();
        (await repo.GetActiveAsync(App))!.Version.Should().Be(v2);
        (await db.SystemConfigVersions.AsNoTracking().SingleAsync(v => v.Id == draft.Id)).Status.Should().Be(SystemConfigStatus.PendingApproval);
    }

    [Fact]
    public async Task MigrationGoesDownCleanly_DroppingDraftsButKeepingHistory()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"draftsdown_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        var repo = new SystemConfigRepository(db);
        await ActivateAdminAsync(repo, 0);
        await repo.AddDraftAsync(Draft(Guid.NewGuid(), 1));

        await migrator.MigrateAsync("20261126190000_AddCatalogAndForecastConfigCapabilities");

        (await db.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM system_config_versions""").SingleAsync()).Should().Be(1);
    }
}
