using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class ForecastAlertConfiguration : IEntityTypeConfiguration<ForecastAlert>
{
    private const string InaccurateFeedback = "inaccurate";

    public void Configure(EntityTypeBuilder<ForecastAlert> builder)
    {
        builder.ToTable("forecast_alerts", t =>
        {
            t.HasCheckConstraint(
                "chk_forecast_alerts_kind",
                "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly')");
            t.HasCheckConstraint("chk_forecast_alerts_status", "status IN ('pending','sent')");
            t.HasCheckConstraint("chk_forecast_alerts_feedback", "feedback IS NULL OR feedback = 'inaccurate'");
            t.HasCheckConstraint("chk_forecast_alerts_sent_at", "(status = 'sent') = (sent_at IS NOT NULL)");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(a => a.Kind)
            .HasColumnName("kind")
            .HasConversion(v => ForecastAlertKindColumn.ToColumn(v), v => ForecastAlertKindColumn.FromColumn(v))
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(a => a.DedupKey).HasColumnName("dedup_key").HasMaxLength(120).IsRequired();
        builder.Property(a => a.Title).HasColumnName("title").HasMaxLength(120).IsRequired();
        builder.Property(a => a.Body).HasColumnName("body").HasMaxLength(500).IsRequired();
        builder.Property(a => a.Data).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<ForecastAlertStatus>(v, true))
            .HasMaxLength(10)
            .IsRequired();
        builder.Property(a => a.ScheduledFor).HasColumnName("scheduled_for").IsRequired();
        builder.Property(a => a.SentAt).HasColumnName("sent_at");
        builder.Property(a => a.MarkedInaccurate)
            .HasColumnName("feedback")
            .HasConversion(v => v == true ? InaccurateFeedback : null, v => v == InaccurateFeedback ? true : null)
            .HasMaxLength(20);
        builder.Property(a => a.FeedbackAt).HasColumnName("feedback_at");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.UserId, a.Kind, a.DedupKey }).IsUnique().HasDatabaseName("uq_forecast_alerts_user_kind_dedup");
        builder.HasIndex(a => a.ScheduledFor).HasFilter("status = 'pending'").HasDatabaseName("ix_forecast_alerts_pending_due");
        builder.HasIndex(a => new { a.UserId, a.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_forecast_alerts_user_created");
    }
}
