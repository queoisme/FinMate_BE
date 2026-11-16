using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    // No-op: bootstrap migrations 20261112091724_AddUserNotifications,
    // 20261113100000_AddMerchantCategoryOverride and 20261113151102_AddProviderReference already
    // created this schema and are recorded in __EFMigrationsHistory on every real database, but
    // FinMateDbContextModelSnapshot.cs was never regenerated when those were added — so `dotnet ef`
    // kept seeing "pending model changes" (PendingModelChangesWarning crashed startup). This
    // migration exists only so the tool regenerates a snapshot matching both the real schema and
    // the current model; Up/Down are intentionally empty so it never touches the database.
    /// <inheritdoc />
    public partial class SyncSnapshotAfterManualMigrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
