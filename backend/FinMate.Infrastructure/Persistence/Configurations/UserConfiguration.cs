using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t => t.HasCheckConstraint("chk_users_role", "role IN ('user','admin')"));

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash");

        builder.Property(u => u.GoogleId)
            .HasColumnName("google_id")
            .HasMaxLength(255);

        builder.Property(u => u.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<UserRole>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.MonthlyIncomeCents).HasColumnName("monthly_income_cents");

        builder.OwnsOne(u => u.NotificationPrefs, prefs =>
        {
            prefs.Property(p => p.PushEnabled).HasColumnName("push_enabled");
            prefs.Property(p => p.BudgetAlertsEnabled).HasColumnName("budget_alerts_enabled");
            prefs.Property(p => p.MissionRemindersEnabled).HasColumnName("mission_reminders_enabled");
        });
        builder.Navigation(u => u.NotificationPrefs).IsRequired();

        builder.Property(u => u.IsLocked)
            .HasColumnName("is_locked")
            .HasDefaultValue(false);

        builder.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(u => u.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("uq_users_email");

        builder.HasIndex(u => u.GoogleId)
            .IsUnique()
            .HasDatabaseName("uq_users_google_id")
            .HasFilter("google_id IS NOT NULL");

        builder.HasQueryFilter(u => u.DeletedAt == null);

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(r => r.User)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
