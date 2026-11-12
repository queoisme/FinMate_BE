using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class TransactionAnomalyConfiguration : IEntityTypeConfiguration<TransactionAnomaly>
{
    public void Configure(EntityTypeBuilder<TransactionAnomaly> builder)
    {
        builder.ToTable("transaction_anomalies", t =>
        {
            t.HasCheckConstraint("chk_transaction_anomalies_kind", "kind IN ('category_spike','merchant_spike','duplicate')");
            t.HasCheckConstraint("chk_transaction_anomalies_severity", "severity IN ('normal','high')");
            t.HasCheckConstraint(
                "chk_transaction_anomalies_resolution",
                "resolution IS NULL OR resolution IN ('normal','one_off','wrong_entry','suspicious','refunded','reversed')");
            t.HasCheckConstraint("chk_transaction_anomalies_resolved_at", "(resolution IS NULL) = (resolved_at IS NULL)");
            t.HasCheckConstraint("chk_transaction_anomalies_amounts", "amount_cents > 0 AND typical_cents > 0 AND ratio_percent > 0");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(a => a.TransactionId).HasColumnName("transaction_id").IsRequired();
        builder.Property(a => a.CategoryId).HasColumnName("category_id");
        builder.Property(a => a.CounterpartyKey).HasColumnName("counterparty_key").HasMaxLength(120);
        builder.Property(a => a.Kind)
            .HasColumnName("kind")
            .HasConversion(v => AnomalyKindColumn.ToColumn(v), v => AnomalyKindColumn.FromColumn(v))
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(a => a.Severity)
            .HasColumnName("severity")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<AnomalySeverity>(v, true))
            .HasMaxLength(10)
            .IsRequired();
        builder.Property(a => a.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(a => a.TypicalCents).HasColumnName("typical_cents").IsRequired();
        builder.Property(a => a.RatioPercent).HasColumnName("ratio_percent").IsRequired();
        builder.Property(a => a.Resolution)
            .HasColumnName("resolution")
            .HasConversion(
                v => v == null ? null : AnomalyResolutionColumn.ToColumn(v.Value),
                v => v == null ? null : AnomalyResolutionColumn.FromColumn(v))
            .HasMaxLength(20);
        builder.Property(a => a.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.Transaction).WithMany().HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Cascade);

        // Giao dịch đã xóa mềm thì đánh dấu của nó cũng ẩn theo (khớp query filter của Transaction).
        builder.HasQueryFilter(a => a.Transaction!.DeletedAt == null);

        builder.HasIndex(a => a.TransactionId).IsUnique().HasDatabaseName("uq_transaction_anomalies_transaction");
        builder.HasIndex(a => new { a.UserId, a.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_transaction_anomalies_user_created");
        builder.HasIndex(a => new { a.UserId, a.CategoryId })
            .HasFilter("resolution = 'normal'")
            .HasDatabaseName("ix_transaction_anomalies_user_normal");
    }
}
