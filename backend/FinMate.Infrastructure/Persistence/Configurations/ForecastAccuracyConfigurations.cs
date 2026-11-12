using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class ForecastSnapshotConfiguration : IEntityTypeConfiguration<ForecastSnapshot>
{
    public void Configure(EntityTypeBuilder<ForecastSnapshot> builder)
    {
        builder.ToTable("forecast_snapshots", t =>
        {
            t.HasCheckConstraint("chk_forecast_snapshots_horizon", "horizon_days > 0 AND horizon_date > origin_date");
            t.HasCheckConstraint("chk_forecast_snapshots_confidence", "confidence IN ('low','normal')");
            t.HasCheckConstraint("chk_forecast_snapshots_actual", "(actual_cents IS NULL) = (actual_filled_at IS NULL)");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.OriginDate).HasColumnName("origin_date").IsRequired();
        builder.Property(s => s.HorizonDays).HasColumnName("horizon_days").IsRequired();
        builder.Property(s => s.HorizonDate).HasColumnName("horizon_date").IsRequired();
        builder.Property(s => s.Method).HasColumnName("method").HasMaxLength(40).IsRequired();
        builder.Property(s => s.ConfigVersion).HasColumnName("config_version").IsRequired();
        builder.Property(s => s.StartBalanceCents).HasColumnName("start_balance_cents").IsRequired();
        builder.Property(s => s.PredictedCents).HasColumnName("predicted_cents").IsRequired();
        builder.Property(s => s.ConservativeCents).HasColumnName("conservative_cents").IsRequired();
        builder.Property(s => s.OptimisticCents).HasColumnName("optimistic_cents").IsRequired();
        builder.Property(s => s.RecurringOnlyCents).HasColumnName("recurring_only_cents").IsRequired();
        builder.Property(s => s.Confidence).HasColumnName("confidence").HasMaxLength(10).IsRequired();
        builder.Property(s => s.ActualCents).HasColumnName("actual_cents");
        builder.Property(s => s.ActualFilledAt).HasColumnName("actual_filled_at");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);

        // Mỗi người, mỗi ngày gốc, mỗi mốc đúng một bản chụp: chạy lại job không tạo bản trùng.
        builder.HasIndex(s => new { s.UserId, s.OriginDate, s.HorizonDays }).IsUnique().HasDatabaseName("uq_forecast_snapshots_user_origin_horizon");
        builder.HasIndex(s => s.HorizonDate).HasFilter("actual_cents IS NULL").HasDatabaseName("ix_forecast_snapshots_awaiting_actual");
        builder.HasIndex(s => s.OriginDate).HasFilter("actual_cents IS NOT NULL").HasDatabaseName("ix_forecast_snapshots_filled_origin");
    }
}

public class ForecastFeedbackConfiguration : IEntityTypeConfiguration<ForecastFeedback>
{
    public void Configure(EntityTypeBuilder<ForecastFeedback> builder)
    {
        builder.ToTable("forecast_feedback", t =>
        {
            t.HasCheckConstraint("chk_forecast_feedback_reason", "reason IN ('balance_wrong','missing_bill','wrong_bill','unusual_spending','other')");
            t.HasCheckConstraint("chk_forecast_feedback_status", "status IN ('new','labeled')");
            t.HasCheckConstraint(
                "chk_forecast_feedback_cause", "cause IS NULL OR cause IN ('data_missing','recurring_misread','one_off','model_issue','other')");
            t.HasCheckConstraint("chk_forecast_feedback_labeled", "(status = 'labeled') = (cause IS NOT NULL AND labeled_at IS NOT NULL)");
        });

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(f => f.SnapshotId).HasColumnName("snapshot_id");
        builder.Property(f => f.Reason).HasColumnName("reason").HasMaxLength(30).IsRequired();
        builder.Property(f => f.Note).HasColumnName("note").HasMaxLength(300);
        builder.Property(f => f.Status).HasColumnName("status").HasMaxLength(10).IsRequired();
        builder.Property(f => f.Cause).HasColumnName("cause").HasMaxLength(30);
        builder.Property(f => f.LabeledByUserId).HasColumnName("labeled_by_user_id");
        builder.Property(f => f.LabeledAt).HasColumnName("labeled_at");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(f => f.Snapshot).WithMany().HasForeignKey(f => f.SnapshotId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(f => new { f.Status, f.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_forecast_feedback_status_created");
        builder.HasIndex(f => new { f.UserId, f.SnapshotId }).HasFilter("status = 'new'").HasDatabaseName("ix_forecast_feedback_user_snapshot_new");
    }
}
