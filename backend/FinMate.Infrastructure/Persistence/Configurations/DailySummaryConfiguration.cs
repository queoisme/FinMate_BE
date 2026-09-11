using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class DailySummaryConfiguration : IEntityTypeConfiguration<DailySummary>
{
    public void Configure(EntityTypeBuilder<DailySummary> builder)
    {
        builder.ToTable("daily_summaries");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.SummaryDate).HasColumnName("summary_date").HasColumnType("date").IsRequired();

        builder.Property(s => s.TotalSpentCents).HasColumnName("total_spent_cents").HasDefaultValue(0L);
        builder.Property(s => s.TotalIncomeCents).HasColumnName("total_income_cents").HasDefaultValue(0L);
        builder.Property(s => s.TransactionCount).HasColumnName("transaction_count").HasDefaultValue(0);

        builder.Property(s => s.TopCategoryId).HasColumnName("top_category_id");
        builder.Property(s => s.TopCategorySpentCents).HasColumnName("top_category_spent_cents").HasDefaultValue(0L);

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // DailySummaryJob chạy lại cùng một ngày thì upsert, không nhân bản dòng.
        builder.HasIndex(s => new { s.UserId, s.SummaryDate })
            .IsUnique()
            .HasDatabaseName("uq_daily_summaries_user_date");

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.TopCategory)
            .WithMany()
            .HasForeignKey(s => s.TopCategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
