using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class BudgetPeriodConfiguration : IEntityTypeConfiguration<BudgetPeriod>
{
    public void Configure(EntityTypeBuilder<BudgetPeriod> builder)
    {
        builder.ToTable("budget_periods");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.BudgetId).HasColumnName("budget_id").IsRequired();
        builder.Property(p => p.PeriodStart).HasColumnName("period_start").IsRequired();
        builder.Property(p => p.PeriodEnd).HasColumnName("period_end").IsRequired();
        builder.Property(p => p.LimitCents).HasColumnName("limit_cents").IsRequired();
        builder.Property(p => p.SpentCents).HasColumnName("spent_cents").HasDefaultValue(0L);
        builder.Property(p => p.Alert80SentAt).HasColumnName("alert_80_sent_at");
        builder.Property(p => p.Alert100SentAt).HasColumnName("alert_100_sent_at");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(p => new { p.BudgetId, p.PeriodStart })
            .IsUnique()
            .HasDatabaseName("uq_budget_periods_budget_start");

        // BudgetAlertJob quét period đang hiệu lực theo biên thời gian.
        builder.HasIndex(p => p.PeriodEnd).HasDatabaseName("idx_budget_periods_period_end");
    }
}
