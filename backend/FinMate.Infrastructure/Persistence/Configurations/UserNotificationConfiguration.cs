using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

/// <summary>Enum ↔ cột TEXT dạng snake_case (CONVENTIONS: enum là TEXT + CHECK, không dùng PostgreSQL ENUM).</summary>
internal static class UserNotificationTypeColumn
{
    private static readonly Dictionary<UserNotificationType, string> ToText = new()
    {
        [UserNotificationType.BudgetThreshold] = "budget_threshold",
        [UserNotificationType.BalanceChange] = "balance_change",
        [UserNotificationType.AnomalyDetected] = "anomaly_detected",
        [UserNotificationType.RecurringBillDue] = "recurring_bill_due",
        [UserNotificationType.ForecastReady] = "forecast_ready",
        [UserNotificationType.GoalMilestone] = "goal_milestone",
    };

    private static readonly Dictionary<string, UserNotificationType> FromText = ToText.ToDictionary(p => p.Value, p => p.Key);

    public static string ToColumn(UserNotificationType value) => ToText[value];

    public static UserNotificationType FromColumn(string value) => FromText[value];
}

public class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("user_notifications", t => t.HasCheckConstraint(
            "chk_user_notifications_type",
            "type IN ('budget_threshold','balance_change','anomaly_detected','recurring_bill_due','forecast_ready','goal_milestone')"));

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(n => n.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(n => n.Type)
            .HasColumnName("type")
            .HasConversion(v => UserNotificationTypeColumn.ToColumn(v), v => UserNotificationTypeColumn.FromColumn(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(n => n.Title).HasColumnName("title").HasMaxLength(120).IsRequired();
        builder.Property(n => n.Body).HasColumnName("body").HasMaxLength(500).IsRequired();
        builder.Property(n => n.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(50);
        builder.Property(n => n.RelatedEntityId).HasColumnName("related_entity_id");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(n => n.ReadAt).HasColumnName("read_at");

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(n => new { n.UserId, n.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_user_notifications_user_created");
        builder.HasIndex(n => new { n.UserId, n.Type }).HasDatabaseName("ix_user_notifications_user_type");
    }
}
