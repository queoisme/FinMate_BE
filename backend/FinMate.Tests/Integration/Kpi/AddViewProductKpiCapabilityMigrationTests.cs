using FinMate.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FinMate.Tests.Integration.Kpi;

/// <summary>Phase 94: hàng OpsManager đã có trong <c>role_permissions</c> được cấp <c>ViewProductKpi</c>; Staff thì không; Down gỡ đúng bit đó.</summary>
public class AddViewProductKpiCapabilityMigrationTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string MigrationBefore = "20261126102753_AddRecurringInstallments";
    private const string ThisMigration = "20261126150000_AddViewProductKpiCapability";
    private const long ViewProductKpi = 1L << 18;
    private readonly ReceiptScanDbFixture _fixture;

    public AddViewProductKpiCapabilityMigrationTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivesExistingOpsManagersTheNewCapability_NotStaff_AndGoesBackDown()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"kpi_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO role_permissions (role_id, capabilities, updated_at) VALUES (2, 31, now()), (3, 14335, now())");

        await migrator.MigrateAsync(ThisMigration);

        async Task<long> CapabilitiesOf(int roleId) => await db.Database
            .SqlQuery<long>($"""SELECT capabilities AS "Value" FROM role_permissions WHERE role_id = {roleId}""").SingleAsync();
        (await CapabilitiesOf(3)).Should().Be(14335 | ViewProductKpi);
        (await CapabilitiesOf(2)).Should().Be(31, "Staff không có quyền xem KPI sản phẩm theo mặc định");

        await migrator.MigrateAsync(MigrationBefore);

        (await CapabilitiesOf(3)).Should().Be(14335);
        (await CapabilitiesOf(2)).Should().Be(31);
    }
}
