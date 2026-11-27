using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class OpsNotificationConfiguration : IEntityTypeConfiguration<OpsNotification>
{
    public void Configure(EntityTypeBuilder<OpsNotification> builder)
    {
        builder.ToTable("ops_notifications", t =>
            t.HasCheckConstraint("chk_ops_notifications_type", GroupColumns.CheckIn<OpsNotificationType>("type")));

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(n => n.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(n => n.Type).HasColumnName("type").HasConversion(GroupColumns.Converter<OpsNotificationType>()).HasMaxLength(30).IsRequired();
        builder.Property(n => n.Title).HasColumnName("title").HasMaxLength(120).IsRequired();
        builder.Property(n => n.Body).HasColumnName("body").HasMaxLength(300).IsRequired();
        builder.Property(n => n.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(50);
        builder.Property(n => n.RelatedEntityId).HasColumnName("related_entity_id");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(n => n.ReadAt).HasColumnName("read_at");

        builder.HasIndex(n => new { n.UserId, n.CreatedAt }).HasDatabaseName("ix_ops_notifications_user_created");
        builder.HasIndex(n => new { n.UserId, n.Type, n.RelatedEntityId })
            .HasFilter("read_at IS NULL")
            .HasDatabaseName("ix_ops_notifications_unread");

        builder.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
