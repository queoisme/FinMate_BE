using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class OpsIncidentConfiguration : IEntityTypeConfiguration<OpsIncident>
{
    public void Configure(EntityTypeBuilder<OpsIncident> builder)
    {
        builder.ToTable("ops_incidents", t =>
        {
            t.HasCheckConstraint("chk_ops_incidents_source", GroupColumns.CheckIn<IncidentSource>("source"));
            t.HasCheckConstraint("chk_ops_incidents_severity", GroupColumns.CheckIn<IncidentSeverity>("severity"));
            t.HasCheckConstraint("chk_ops_incidents_status", GroupColumns.CheckIn<IncidentStatus>("status"));
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(i => i.Fingerprint).HasColumnName("fingerprint").HasMaxLength(100).IsRequired();
        builder.Property(i => i.Source).HasColumnName("source").HasConversion(GroupColumns.Converter<IncidentSource>()).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Service).HasColumnName("service").HasMaxLength(100).IsRequired();
        builder.Property(i => i.Severity).HasColumnName("severity").HasConversion(GroupColumns.Converter<IncidentSeverity>()).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<IncidentStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        builder.Property(i => i.TechnicalDetail).HasColumnName("technical_detail").HasMaxLength(1000);
        builder.Property(i => i.OccurrenceCount).HasColumnName("occurrence_count").IsRequired();
        builder.Property(i => i.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
        builder.Property(i => i.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
        builder.Property(i => i.AcknowledgedByUserId).HasColumnName("acknowledged_by_user_id");
        builder.Property(i => i.AcknowledgedAt).HasColumnName("acknowledged_at");
        builder.Property(i => i.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        builder.Property(i => i.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(i => i.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(1000);
        builder.Property(i => i.LastNotifiedAt).HasColumnName("last_notified_at");
        builder.Property(i => i.NotificationCount).HasColumnName("notification_count").IsRequired();
        builder.Property(i => i.ResolvedNotifiedAt).HasColumnName("resolved_notified_at");

        // Nhận xử lý và tự đóng cùng lúc: một bên nhận 409 thay vì ghi đè.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(i => i.Fingerprint)
            .IsUnique()
            .HasFilter("status <> 'resolved'")
            .HasDatabaseName("uq_ops_incidents_one_active");
        builder.HasIndex(i => new { i.Status, i.LastSeenAt }).HasDatabaseName("ix_ops_incidents_status");

        builder.HasOne<User>().WithMany().HasForeignKey(i => i.AcknowledgedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.ResolvedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class OpsServiceStatusConfiguration : IEntityTypeConfiguration<OpsServiceStatus>
{
    public void Configure(EntityTypeBuilder<OpsServiceStatus> builder)
    {
        builder.ToTable("ops_service_status", t =>
            t.HasCheckConstraint("chk_ops_service_status_state", GroupColumns.CheckIn<MonitoredServiceState>("state")));

        builder.HasKey(s => s.Service);
        builder.Property(s => s.Service).HasColumnName("service").HasMaxLength(100);
        builder.Property(s => s.State).HasColumnName("state").HasConversion(GroupColumns.Converter<MonitoredServiceState>()).HasMaxLength(20).IsRequired();
        builder.Property(s => s.ConsecutiveFailures).HasColumnName("consecutive_failures").IsRequired();
        builder.Property(s => s.ConsecutiveSuccesses).HasColumnName("consecutive_successes").IsRequired();
        builder.Property(s => s.LastCheckedAt).HasColumnName("last_checked_at").IsRequired();
        builder.Property(s => s.LastHealthyAt).HasColumnName("last_healthy_at");
        builder.Property(s => s.Detail).HasColumnName("detail").HasMaxLength(300);
    }
}

public class OpsErrorEventConfiguration : IEntityTypeConfiguration<OpsErrorEvent>
{
    public void Configure(EntityTypeBuilder<OpsErrorEvent> builder)
    {
        builder.ToTable("ops_error_events", t =>
            t.HasCheckConstraint("chk_ops_error_events_source", GroupColumns.CheckIn<IncidentSource>("source")));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.Service).HasColumnName("service").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Source).HasColumnName("source").HasConversion(GroupColumns.Converter<IncidentSource>()).HasMaxLength(20).IsRequired();
        builder.Property(e => e.ExceptionType).HasColumnName("exception_type").HasMaxLength(300);
        builder.Property(e => e.SourceContext).HasColumnName("source_context").HasMaxLength(300);
        builder.Property(e => e.MessageTemplate).HasColumnName("message_template").HasMaxLength(500);
        builder.Property(e => e.RequestPath).HasColumnName("request_path").HasMaxLength(300);
        builder.Property(e => e.TraceId).HasColumnName("trace_id").HasMaxLength(100);
        builder.Property(e => e.Fingerprint).HasColumnName("fingerprint").HasMaxLength(100).IsRequired();

        builder.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_ops_error_events_created_at");
        builder.HasIndex(e => new { e.Fingerprint, e.CreatedAt }).HasDatabaseName("ix_ops_error_events_fingerprint");
    }
}
