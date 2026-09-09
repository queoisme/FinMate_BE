using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class DataDeletionRequestConfiguration : IEntityTypeConfiguration<DataDeletionRequest>
{
    public void Configure(EntityTypeBuilder<DataDeletionRequest> builder)
    {
        builder.ToTable("data_deletion_requests", t =>
            t.HasCheckConstraint("chk_data_deletion_requests_status", "status IN ('pending','processed')"));

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(d => d.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(d => d.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(d => d.ScheduledHardDeleteAt).HasColumnName("scheduled_hard_delete_at").IsRequired();
        builder.Property(d => d.ProcessedAt).HasColumnName("processed_at");

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<DataDeletionStatus>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(d => d.ScheduledHardDeleteAt)
            .HasDatabaseName("idx_data_deletion_requests_scheduled_hard_delete_at");
    }
}
