using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowCancellingDataDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_data_deletion_requests_status",
                table: "data_deletion_requests");

            migrationBuilder.CreateIndex(
                name: "idx_data_deletion_requests_requested_at_id",
                table: "data_deletion_requests",
                columns: new[] { "requested_at", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "chk_data_deletion_requests_status",
                table: "data_deletion_requests",
                sql: "status IN ('pending','processed','cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_data_deletion_requests_requested_at_id",
                table: "data_deletion_requests");

            migrationBuilder.DropCheckConstraint(
                name: "chk_data_deletion_requests_status",
                table: "data_deletion_requests");

            migrationBuilder.AddCheckConstraint(
                name: "chk_data_deletion_requests_status",
                table: "data_deletion_requests",
                sql: "status IN ('pending','processed')");
        }
    }
}
