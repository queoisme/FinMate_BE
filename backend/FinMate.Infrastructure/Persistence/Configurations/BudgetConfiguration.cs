using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budgets", t =>
        {
            t.HasCheckConstraint("chk_budgets_period_type", "period_type IN ('monthly')");
            t.HasCheckConstraint("chk_budgets_limit_cents", "limit_cents > 0");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(b => b.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(b => b.CategoryId).HasColumnName("category_id");
        builder.Property(b => b.LimitCents).HasColumnName("limit_cents").IsRequired();

        builder.Property(b => b.PeriodType)
            .HasColumnName("period_type")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<BudgetPeriodType>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(b => b.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(b => b.DeletedAt == null);

        // Postgres coi mỗi NULL là distinct nên 1 unique index trên (user_id, category_id,
        // period_type) sẽ không chặn được nhiều budget tổng. Tách 2 partial index — cùng
        // pattern đã dùng cho system/user category ở CategoryConfiguration.
        builder.HasIndex(b => new { b.UserId, b.CategoryId, b.PeriodType })
            .IsUnique()
            .HasDatabaseName("uq_budgets_user_category")
            .HasFilter("category_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(b => new { b.UserId, b.PeriodType })
            .IsUnique()
            .HasDatabaseName("uq_budgets_user_total")
            .HasFilter("category_id IS NULL AND deleted_at IS NULL");

        builder.HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Category)
            .WithMany()
            .HasForeignKey(b => b.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Periods)
            .WithOne(p => p.Budget)
            .HasForeignKey(p => p.BudgetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
