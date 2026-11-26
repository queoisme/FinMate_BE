using FinMate.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FinMate.Tests.Integration.BusinessConfig;

/// <summary>Phase 95: hàng Staff/OpsManager đã có trong <c>role_permissions</c> nhận đúng các bit mới; Down gỡ đúng các bit đó, không đụng bit cũ.</summary>
public class AddCatalogAndForecastConfigCapabilitiesMigrationTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string MigrationBefore = "20261126150000_AddViewProductKpiCapability";
    private const string ThisMigration = "20261126190000_AddCatalogAndForecastConfigCapabilities";
    private const long MaintainCatalog = 1L << 19;
    private const long DraftForecastConfig = 1L << 20;
    private const long ApproveForecastConfig = 1L << 21;
    private const long ApproveAgentContent = 1L << 22;
    private readonly ReceiptScanDbFixture _fixture;

    public AddCatalogAndForecastConfigCapabilitiesMigrationTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StaffGetsCatalogAndDraft_ManagerGetsAllFour_AndDownRemovesOnlyThem()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"bizcfg_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO role_permissions (role_id, capabilities, updated_at) VALUES (2, 31, now()), (3, 14335, now())");

        await migrator.MigrateAsync(ThisMigration);

        async Task<long> CapabilitiesOf(int roleId) => await db.Database
            .SqlQuery<long>($"""SELECT capabilities AS "Value" FROM role_permissions WHERE role_id = {roleId}""").SingleAsync();
        (await CapabilitiesOf(2)).Should().Be(31 | MaintainCatalog | DraftForecastConfig);
        (await CapabilitiesOf(3)).Should().Be(14335 | MaintainCatalog | DraftForecastConfig | ApproveForecastConfig | ApproveAgentContent);

        await migrator.MigrateAsync(MigrationBefore);

        (await CapabilitiesOf(2)).Should().Be(31);
        (await CapabilitiesOf(3)).Should().Be(14335);
    }
}
