using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class PersonalDataExportConfiguration : IEntityTypeConfiguration<PersonalDataExport>
{
    public void Configure(EntityTypeBuilder<PersonalDataExport> builder)
    {
        builder.ToTable("personal_data_exports", t =>
        {
            t.HasCheckConstraint("ck_personal_data_exports_status", GroupColumns.CheckIn<PersonalDataExportStatus>("status"));
            t.HasCheckConstraint("ck_personal_data_exports_ready", "status <> 'ready' OR (ready_at IS NOT NULL AND expires_at IS NOT NULL)");
            t.HasCheckConstraint("ck_personal_data_exports_failed", "(status = 'failed') = (failure_code IS NOT NULL)");
            t.HasCheckConstraint("ck_personal_data_exports_counters", "attempts >= 0 AND download_count >= 0 AND (size_bytes IS NULL OR size_bytes >= 0)");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<PersonalDataExportStatus>()).HasMaxLength(10).IsRequired();
        builder.Property(e => e.FormatVersion).HasColumnName("format_version").IsRequired();
        builder.Property(e => e.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(e => e.StartedAt).HasColumnName("started_at");
        builder.Property(e => e.ReadyAt).HasColumnName("ready_at");
        builder.Property(e => e.FailedAt).HasColumnName("failed_at");
        builder.Property(e => e.FailureCode).HasColumnName("failure_code").HasMaxLength(40);
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        builder.Property(e => e.ExpiredAt).HasColumnName("expired_at");
        builder.Property(e => e.SizeBytes).HasColumnName("size_bytes");
        builder.Property(e => e.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(e => e.LeaseUntil).HasColumnName("lease_until");
        builder.Property(e => e.DownloadTokenHash).HasColumnName("download_token_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(e => e.DownloadTokenExpiresAt).HasColumnName("download_token_expires_at");
        builder.Property(e => e.DownloadCount).HasColumnName("download_count").IsRequired();
        builder.Property(e => e.LastDownloadedAt).HasColumnName("last_downloaded_at");
        builder.Property(e => e.CountsTowardLimit).HasColumnName("counts_toward_limit").IsRequired();

        // Khóa lạc quan: job, job quét và job dọn dẹp cùng đổi trạng thái một dòng.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);

        // BR-EXP-06: mỗi user tối đa một yêu cầu đang chạy, kể cả khi hai request tới cùng lúc.
        builder.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("ux_personal_data_exports_user_active")
            .HasFilter("status IN ('pending','running')");
        builder.HasIndex(e => new { e.UserId, e.RequestedAt }).IsDescending(false, true).HasDatabaseName("ix_personal_data_exports_user_requested");
        builder.HasIndex(e => new { e.Status, e.ExpiresAt }).HasDatabaseName("ix_personal_data_exports_status_expires");
    }
}

public class PersonalDataExportArchiveConfiguration : IEntityTypeConfiguration<PersonalDataExportArchive>
{
    public void Configure(EntityTypeBuilder<PersonalDataExportArchive> builder)
    {
        builder.ToTable("personal_data_export_archives");
        builder.HasKey(a => a.ExportId);
        builder.Property(a => a.ExportId).HasColumnName("export_id");
        builder.Property(a => a.Data).HasColumnName("data").IsRequired();
        builder.Property(a => a.Nonce).HasColumnName("nonce").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        // Xoá yêu cầu (hay xoá cứng user, cascade qua yêu cầu) xoá luôn file.
        builder.HasOne<PersonalDataExport>().WithOne().HasForeignKey<PersonalDataExportArchive>(a => a.ExportId).OnDelete(DeleteBehavior.Cascade);
    }
}
