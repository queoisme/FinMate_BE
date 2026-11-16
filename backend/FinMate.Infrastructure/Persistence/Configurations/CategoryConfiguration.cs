using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.UserId).HasColumnName("user_id");

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Slug)
            .HasColumnName("slug")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.IconName)
            .HasColumnName("icon_name")
            .HasMaxLength(50);

        builder.Property(c => c.Color)
            .HasColumnName("color")
            .HasMaxLength(7);

        builder.Property(c => c.Kind)
            .HasColumnName("kind")
            .HasMaxLength(10)
            .HasDefaultValue(Category.KindExpense)
            .IsRequired();

        builder.Property(c => c.ParentCategoryId).HasColumnName("parent_category_id");

        builder.Property(c => c.SuggestedBudgetCents).HasColumnName("suggested_budget_cents");

        builder.Property(c => c.IsFavorite)
            .HasColumnName("is_favorite")
            .HasDefaultValue(false);

        builder.Property(c => c.IsSystem)
            .HasColumnName("is_system")
            .HasDefaultValue(false);

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(c => c.DeletedAt == null);

        builder.HasIndex(c => c.Slug)
            .IsUnique()
            .HasDatabaseName("uq_categories_system_slug")
            .HasFilter("user_id IS NULL AND deleted_at IS NULL");

        builder.HasIndex(c => new { c.UserId, c.Slug })
            .IsUnique()
            .HasDatabaseName("uq_categories_user_slug")
            .HasFilter("user_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Danh mục cha bị xóa cứng thì con chỉ mất nhóm, không mất cả danh mục của người dùng.
        builder.HasOne(c => c.ParentCategory)
            .WithMany()
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.ParentCategoryId).HasDatabaseName("ix_categories_parent_category_id");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_categories_kind", "kind IN ('expense', 'income')");
            t.HasCheckConstraint("ck_categories_suggested_budget", "suggested_budget_cents IS NULL OR suggested_budget_cents >= 0");
        });
    }
}
