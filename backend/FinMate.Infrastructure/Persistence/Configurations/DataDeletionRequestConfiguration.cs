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
            t.HasCheckConstraint(
                "chk_data_deletion_requests_status",
                "status IN ('pending','processed','cancelled')"));

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

        // Màn hình giám sát của admin sắp xếp theo lúc gửi yêu cầu, phân trang keyset trên
        // (requested_at, id) — index này là thứ giữ nó không phải quét cả bảng.
        builder.HasIndex(d => new { d.RequestedAt, d.Id })
            .HasDatabaseName("idx_data_deletion_requests_requested_at_id");
    }
}
