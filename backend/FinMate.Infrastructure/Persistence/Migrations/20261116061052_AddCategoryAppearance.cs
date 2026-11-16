using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "categories",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_favorite",
                table: "categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "categories",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "expense");

            migrationBuilder.AddColumn<Guid>(
                name: "parent_category_id",
                table: "categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "suggested_budget_cents",
                table: "categories",
                type: "bigint",
                nullable: true);

            // Danh mục hệ thống "Thu nhập" có sẵn phải là loại thu nhập; các danh mục còn lại giữ mặc định "expense".
            migrationBuilder.Sql("UPDATE categories SET kind = 'income' WHERE slug = 'income' AND user_id IS NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_category_id",
                table: "categories",
                column: "parent_category_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_categories_kind",
                table: "categories",
                sql: "kind IN ('expense', 'income')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_categories_suggested_budget",
                table: "categories",
                sql: "suggested_budget_cents IS NULL OR suggested_budget_cents >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_categories_categories_parent_category_id",
                table: "categories",
                column: "parent_category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_categories_categories_parent_category_id",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "ix_categories_parent_category_id",
                table: "categories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_categories_kind",
                table: "categories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_categories_suggested_budget",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "color",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "is_favorite",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "parent_category_id",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "suggested_budget_cents",
                table: "categories");
        }
    }
}
