using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class SpendingInsightConfiguration : IEntityTypeConfiguration<SpendingInsight>
{
    // Enum có nhiều từ nên không dùng được ToLowerInvariant như các enum 1 từ khác —
    // map tường minh sang snake_case, giống AiResultConfiguration ở Phase 4.
    private static readonly Dictionary<InsightType, string> ToColumn = new()
    {
        [InsightType.VsLastMonth] = "vs_last_month",
        [InsightType.RecurringDetected] = "recurring_detected",
        [InsightType.UnusualSpending] = "unusual_spending",
    };

    private static readonly Dictionary<string, InsightType> FromColumn =
        ToColumn.ToDictionary(kv => kv.Value, kv => kv.Key);

    public void Configure(EntityTypeBuilder<SpendingInsight> builder)
    {
        builder.ToTable("spending_insights", t =>
            t.HasCheckConstraint(
                "chk_spending_insights_insight_type",
                "insight_type IN ('vs_last_month','recurring_detected','unusual_spending')"));

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(i => i.InsightType)
            .HasColumnName("insight_type")
            .HasConversion(v => ToColumn[v], v => FromColumn[v])
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(i => i.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(i => i.Body).HasColumnName("body").HasMaxLength(1000).IsRequired();
        builder.Property(i => i.CategoryId).HasColumnName("category_id");
        builder.Property(i => i.AmountCents).HasColumnName("amount_cents");

        builder.Property(i => i.PeriodStart).HasColumnName("period_start").HasColumnType("date").IsRequired();
        builder.Property(i => i.PeriodEnd).HasColumnName("period_end").HasColumnType("date").IsRequired();

        builder.Property(i => i.IsRead).HasColumnName("is_read").HasDefaultValue(false);
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(i => i.DeletedAt == null);

        builder.HasIndex(i => new { i.UserId, i.CreatedAt })
            .HasDatabaseName("idx_spending_insights_user_created_at");

        // InsightGeneratorJob chạy mỗi đêm — chặn sinh trùng cùng loại cho cùng kỳ/category.
        // category_id nullable và Postgres coi mỗi NULL là distinct, nên phải tách 2 partial
        // index; gộp 1 index sẽ không chặn được nhiều insight vs_last_month (category NULL)
        // cho cùng một kỳ. Cùng pattern đã dùng cho budget tổng ở Phase 5.
        builder.HasIndex(i => new { i.UserId, i.InsightType, i.CategoryId, i.PeriodStart })
            .IsUnique()
            .HasDatabaseName("uq_spending_insights_user_type_category_period")
            .HasFilter("category_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(i => new { i.UserId, i.InsightType, i.PeriodStart })
            .IsUnique()
            .HasDatabaseName("uq_spending_insights_user_type_period")
            .HasFilter("category_id IS NULL AND deleted_at IS NULL");

        builder.HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Category)
            .WithMany()
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
