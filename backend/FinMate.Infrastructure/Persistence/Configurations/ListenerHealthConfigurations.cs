using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class DeviceListenerStatusConfiguration : IEntityTypeConfiguration<DeviceListenerStatus>
{
    public void Configure(EntityTypeBuilder<DeviceListenerStatus> builder)
    {
        builder.ToTable("device_listener_statuses", t =>
            t.HasCheckConstraint("chk_device_listener_statuses_packages", "jsonb_typeof(selected_packages) = 'array'"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.InstallationId).HasColumnName("installation_id").IsRequired();
        builder.Property(s => s.PermissionGranted).HasColumnName("permission_granted").IsRequired();
        builder.Property(s => s.ListenerConnected).HasColumnName("listener_connected").IsRequired();
        builder.Property(s => s.SelectedPackages).HasColumnName("selected_packages").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(s => s.AppVersion).HasColumnName("app_version").HasMaxLength(32);
        builder.Property(s => s.Manufacturer).HasColumnName("manufacturer").HasMaxLength(64);
        builder.Property(s => s.PermissionLostSince).HasColumnName("permission_lost_since");
        builder.Property(s => s.FirstReportedAt).HasColumnName("first_reported_at").IsRequired();
        builder.Property(s => s.LastReportedAt).HasColumnName("last_reported_at").IsRequired();

        builder.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => new { s.UserId, s.InstallationId }).IsUnique().HasDatabaseName("uq_device_listener_statuses_user_installation");
    }
}

public class DeviceListenerDayConfiguration : IEntityTypeConfiguration<DeviceListenerDay>
{
    public void Configure(EntityTypeBuilder<DeviceListenerDay> builder)
    {
        builder.ToTable("device_listener_daily", t =>
            t.HasCheckConstraint("chk_device_listener_daily_report_count", "report_count >= 1"));

        builder.HasKey(d => new { d.StatusId, d.Day });
        builder.Property(d => d.StatusId).HasColumnName("status_id");
        builder.Property(d => d.Day).HasColumnName("day");
        builder.Property(d => d.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(d => d.AnyPermissionDenied).HasColumnName("any_permission_denied").IsRequired();
        builder.Property(d => d.LastPermissionGranted).HasColumnName("last_permission_granted").IsRequired();
        builder.Property(d => d.LastListenerConnected).HasColumnName("last_listener_connected").IsRequired();
        builder.Property(d => d.LastReportedAt).HasColumnName("last_reported_at").IsRequired();
        builder.Property(d => d.ReportCount).HasColumnName("report_count").IsRequired();
        builder.Ignore(d => d.PermissionOk);
        builder.Ignore(d => d.LastStateOk);

        builder.HasOne(d => d.Status).WithMany().HasForeignKey(d => d.StatusId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => new { d.UserId, d.Day }).HasDatabaseName("ix_device_listener_daily_user_day");
        builder.HasIndex(d => d.Day).HasDatabaseName("ix_device_listener_daily_day");
    }
}

public class ListenerUploadGapDailyConfiguration : IEntityTypeConfiguration<ListenerUploadGapDaily>
{
    public void Configure(EntityTypeBuilder<ListenerUploadGapDaily> builder)
    {
        builder.ToTable("listener_upload_gap_daily", t =>
            t.HasCheckConstraint("chk_listener_upload_gap_daily_count", "count >= 0"));

        builder.HasKey(g => new { g.Day, g.AppVersion });
        builder.Property(g => g.Day).HasColumnName("day");
        builder.Property(g => g.AppVersion).HasColumnName("app_version").HasMaxLength(32);
        builder.Property(g => g.Count).HasColumnName("count").IsRequired();
    }
}
