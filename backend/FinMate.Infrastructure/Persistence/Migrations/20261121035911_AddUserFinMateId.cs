using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFinMateId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "finmate_id",
                table: "users",
                type: "character varying(11)",
                maxLength: 11,
                nullable: false,
                defaultValueSql: "'FM-' || upper(substr(md5(random()::text || clock_timestamp()::text), 1, 8))");

            migrationBuilder.CreateIndex(
                name: "uq_users_finmate_id",
                table: "users",
                column: "finmate_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_users_finmate_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "finmate_id",
                table: "users");
        }
    }
}
