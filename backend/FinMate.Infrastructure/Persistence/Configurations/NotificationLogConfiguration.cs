using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notification_logs", t => t.HasCheckConstraint(
            "chk_notification_logs_status", "status IN ('pending','processed','failed','ignored')"));

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(n => n.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(n => n.FinancialAccountId).HasColumnName("financial_account_id");

        builder.Property(n => n.PackageName)
            .HasColumnName("package_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(n => n.NotificationTitle).HasColumnName("notification_title").HasMaxLength(500);
        builder.Property(n => n.NotificationBody).HasColumnName("notification_body");

        builder.Property(n => n.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(n => n.ReceivedAt).HasColumnName("received_at").IsRequired();

        builder.Property(n => n.Status)
            .HasColumnName("status")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<NotificationLogStatus>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(n => n.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        builder.Property(n => n.ProcessedAt).HasColumnName("processed_at");
        builder.Property(n => n.ErrorMessage).HasColumnName("error_message");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(n => new { n.UserId, n.ContentHash })
            .HasDatabaseName("idx_notification_logs_user_content_hash");

        builder.HasIndex(n => new { n.Status, n.RetryCount })
            .HasDatabaseName("idx_notification_logs_status_retry_count");

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.FinancialAccount)
            .WithMany()
            .HasForeignKey(n => n.FinancialAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
