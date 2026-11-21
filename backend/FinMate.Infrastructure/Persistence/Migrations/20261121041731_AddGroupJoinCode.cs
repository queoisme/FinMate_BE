using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupJoinCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "join_code",
                table: "groups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "join_code_enabled",
                table: "groups",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "uq_groups_join_code",
                table: "groups",
                column: "join_code",
                unique: true,
                filter: "join_code IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_groups_join_code",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "join_code",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "join_code_enabled",
                table: "groups");
        }
    }
}
