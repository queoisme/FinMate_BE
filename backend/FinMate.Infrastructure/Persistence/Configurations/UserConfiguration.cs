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
        builder.ToTable("users", t =>
        {
            t.HasCheckConstraint("chk_users_role", "role IN ('user','admin')");
            t.HasCheckConstraint(
                "chk_users_onboarding_status", "onboarding_status IN ('pending','completed','skipped')");

            // Còn "pending" thì chưa có mốc kết thúc; đã làm hay đã bỏ qua thì phải có.
            t.HasCheckConstraint(
                "chk_users_onboarding_completed_at",
                "(onboarding_status = 'pending') = (onboarding_completed_at IS NULL)");

            // Phiên bản và thời điểm đồng ý luôn đi cùng nhau.
            t.HasCheckConstraint(
                "chk_users_survey_ai_consent",
                "(survey_ai_consent_version IS NULL) = (survey_ai_consent_at IS NULL)");
        });

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
        builder.Property(u => u.TermsVersion).HasColumnName("terms_version");
        builder.Property(u => u.TermsAcceptedAt).HasColumnName("terms_accepted_at");
        builder.Property(u => u.AiConsentVersion).HasColumnName("ai_consent_version");
        builder.Property(u => u.AiConsentAt).HasColumnName("ai_consent_at");
        builder.Property(u => u.TrainingConsentVersion).HasColumnName("training_consent_version");
        builder.Property(u => u.TrainingConsentAt).HasColumnName("training_consent_at");
        builder.Property(u => u.SurveyAiConsentVersion).HasColumnName("survey_ai_consent_version");
        builder.Property(u => u.SurveyAiConsentAt).HasColumnName("survey_ai_consent_at");
        builder.Ignore(u => u.SurveyAiConsentGranted);
        builder.Ignore(u => u.TrainingConsentGranted);
        builder.Ignore(u => u.TermsAccepted);
        builder.Ignore(u => u.AiConsentGranted);
        builder.Property(u => u.EmailVerifiedAt).HasColumnName("email_verified_at");

        builder.Property(u => u.OnboardingStatus)
            .HasColumnName("onboarding_status")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<OnboardingStatus>(v, true))
            .HasMaxLength(20)
            .HasDefaultValue(OnboardingStatus.Pending)
            .IsRequired();
        builder.Property(u => u.OnboardingCompletedAt).HasColumnName("onboarding_completed_at");

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
