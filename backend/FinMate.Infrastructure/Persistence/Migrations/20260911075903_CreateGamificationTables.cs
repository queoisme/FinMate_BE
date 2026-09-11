using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateGamificationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mascot_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    item_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unlock_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unlock_level = table.Column<int>(type: "integer", nullable: true),
                    unlock_mission_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_premium = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mascot_items", x => x.id);
                    table.CheckConstraint("chk_mascot_items_item_type", "item_type IN ('hat','outfit','accessory','background')");
                    table.CheckConstraint("chk_mascot_items_unlock_payload", "(unlock_type = 'default' AND unlock_level IS NULL AND unlock_mission_code IS NULL) OR (unlock_type = 'level' AND unlock_level IS NOT NULL) OR (unlock_type = 'mission' AND unlock_mission_code IS NOT NULL)");
                    table.CheckConstraint("chk_mascot_items_unlock_type", "unlock_type IN ('default','level','mission')");
                });

            migrationBuilder.CreateTable(
                name: "missions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    period_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    condition_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    condition_target = table.Column<int>(type: "integer", nullable: false),
                    exp_reward = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_missions", x => x.id);
                    table.CheckConstraint("chk_missions_condition_target", "condition_target > 0");
                    table.CheckConstraint("chk_missions_exp_reward", "exp_reward >= 0");
                    table.CheckConstraint("chk_missions_period_type", "period_type IN ('daily','weekly','one_time')");
                });

            migrationBuilder.CreateTable(
                name: "user_gamification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    exp_points = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    current_streak_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    longest_streak_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_activity_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_gamification", x => x.id);
                    table.CheckConstraint("chk_user_gamification_exp", "exp_points >= 0");
                    table.CheckConstraint("chk_user_gamification_level", "level >= 1");
                    table.ForeignKey(
                        name: "FK_user_gamification_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_mascot_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mascot_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unlocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_equipped = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_mascot_items", x => x.id);
                    table.CheckConstraint("chk_user_mascot_items_item_type", "item_type IN ('hat','outfit','accessory','background')");
                    table.ForeignKey(
                        name: "FK_user_mascot_items_mascot_items_mascot_item_id",
                        column: x => x.mascot_item_id,
                        principalTable: "mascot_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_missions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exp_awarded = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_missions", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_missions_missions_mission_id",
                        column: x => x.mission_id,
                        principalTable: "missions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_mascot_items_code",
                table: "mascot_items",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_missions_code",
                table: "missions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_user_gamification_last_activity",
                table: "user_gamification",
                column: "last_activity_date");

            migrationBuilder.CreateIndex(
                name: "uq_user_gamification_user_id",
                table: "user_gamification",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_mascot_items_mascot_item_id",
                table: "user_mascot_items",
                column: "mascot_item_id");

            migrationBuilder.CreateIndex(
                name: "uq_user_mascot_items_equipped_per_type",
                table: "user_mascot_items",
                columns: new[] { "user_id", "item_type" },
                unique: true,
                filter: "is_equipped");

            migrationBuilder.CreateIndex(
                name: "uq_user_mascot_items_user_item",
                table: "user_mascot_items",
                columns: new[] { "user_id", "mascot_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_missions_mission_id",
                table: "user_missions",
                column: "mission_id");

            migrationBuilder.CreateIndex(
                name: "idx_user_missions_user_period_end",
                table: "user_missions",
                columns: new[] { "user_id", "period_end" });

            migrationBuilder.CreateIndex(
                name: "uq_user_missions_user_mission_period",
                table: "user_missions",
                columns: new[] { "user_id", "mission_id", "period_start" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_gamification");

            migrationBuilder.DropTable(
                name: "user_mascot_items");

            migrationBuilder.DropTable(
                name: "user_missions");

            migrationBuilder.DropTable(
                name: "mascot_items");

            migrationBuilder.DropTable(
                name: "missions");
        }
    }
}
