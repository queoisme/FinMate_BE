using FinMate.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FinMate.Tests.Integration.BusinessConfig;

/// <summary>
/// Pentest 2026-11-28 (#12): <c>AddCommunity</c> và <c>AddSubscriptions</c> thêm quyền mặc định nhưng không cập nhật các hàng
/// <c>role_permissions</c> đã có, nên DB cũ thiếu: Staff không kiểm duyệt cộng đồng, OpsManager không quản lý gói đăng ký.
/// </summary>
public class GrantCommunityAndSubscriptionCapabilitiesMigrationTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string MigrationBefore = "20261127154755_RealignAgentContentCapabilities";
    private const string ThisMigration = "20261128100000_GrantCommunityAndSubscriptionCapabilities";
    private const long ModerateCommunity = 1L << 14;
    private const long MuteCommunityAuthors = 1L << 15;
    private const long ManageSubscriptionPlans = 1L << 16;
    private readonly ReceiptScanDbFixture _fixture;

    public GrantCommunityAndSubscriptionCapabilitiesMigrationTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OldRowsGetTheMissingDefaults_ExistingBitsStay_AndDownRemovesOnlyThem()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"bizcfg_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);
        // Giá trị đúng như DB eval trước khi sửa: Staff 9961503, OpsManager 16660479 (đều thiếu bit 14-16).
        const long staffBefore = 9961503;
        const long managerBefore = 16660479 & ~(ModerateCommunity | MuteCommunityAuthors | ManageSubscriptionPlans);
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO role_permissions (role_id, capabilities, updated_at) VALUES (2, {staffBefore}, now()), (3, {managerBefore}, now())");

        await migrator.MigrateAsync(ThisMigration);

        async Task<long> CapabilitiesOf(int roleId) => await db.Database
            .SqlQuery<long>($"""SELECT capabilities AS "Value" FROM role_permissions WHERE role_id = {roleId}""").SingleAsync();
        (await CapabilitiesOf(2)).Should().Be(staffBefore | ModerateCommunity);
        (await CapabilitiesOf(3)).Should().Be(managerBefore | ModerateCommunity | MuteCommunityAuthors | ManageSubscriptionPlans);

        await migrator.MigrateAsync(MigrationBefore);

        (await CapabilitiesOf(2)).Should().Be(staffBefore);
        (await CapabilitiesOf(3)).Should().Be(managerBefore);
    }

    [Fact]
    public async Task RowsThatAlreadyHaveTheBitsAreUnchanged()
    {
        // DB mới được seed theo mặc định hiện tại đã có đủ bit: migration phải idempotent.
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"bizcfg_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);
        const long staff = 9961503 | ModerateCommunity;
        const long manager = 16660479 | ModerateCommunity | MuteCommunityAuthors | ManageSubscriptionPlans;
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO role_permissions (role_id, capabilities, updated_at) VALUES (2, {staff}, now()), (3, {manager}, now())");

        await migrator.MigrateAsync(ThisMigration);

        (await db.Database.SqlQuery<long>($"""SELECT capabilities AS "Value" FROM role_permissions WHERE role_id = 2""").SingleAsync()).Should().Be(staff);
        (await db.Database.SqlQuery<long>($"""SELECT capabilities AS "Value" FROM role_permissions WHERE role_id = 3""").SingleAsync()).Should().Be(manager);
    }
}
