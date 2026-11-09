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

            // Ngưỡng cảnh báo cá nhân: cả hai cùng trống (dùng mặc định hệ thống) hoặc cùng có và đúng 1 ≤ T1 < T2 ≤ 99 (mức 100% là "vượt hạn mức" cố định).
            t.HasCheckConstraint(
                "chk_users_alert_thresholds",
                "(alert_t1_pct IS NULL AND alert_t2_pct IS NULL) OR (alert_t1_pct IS NOT NULL AND alert_t2_pct IS NOT NULL AND alert_t1_pct >= 1 AND alert_t1_pct < alert_t2_pct AND alert_t2_pct <= 99)");

            t.HasCheckConstraint("chk_users_income_profile_override", "income_profile_override IS NULL OR income_profile_override IN ('stable','variable')");
            t.HasCheckConstraint("chk_users_forecast_reserve", "forecast_reserve_cents IS NULL OR forecast_reserve_cents >= 0");
            t.HasCheckConstraint("chk_users_low_balance_threshold", "low_balance_threshold_cents IS NULL OR low_balance_threshold_cents >= 0");

            // Giờ yên lặng: cả hai cùng trống (mặc định hệ thống) hoặc cùng có và là phút hợp lệ trong ngày.
            t.HasCheckConstraint(
                "chk_users_quiet_hours",
                "(quiet_hours_start_minute IS NULL AND quiet_hours_end_minute IS NULL) OR "
                + "(quiet_hours_start_minute BETWEEN 0 AND 1439 AND quiet_hours_end_minute BETWEEN 0 AND 1439)");
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

        // Vai trò nằm ở bảng user_roles (mỗi người đúng một hàng), không còn là cột của users. AutoInclude để MỌI truy vấn nạp User đều mang vai trò,
        // không ai phải nhớ Include (quên Include sẽ biến Admin thành User lúc đọc).
        builder.Ignore(u => u.Role);
        builder.HasOne(u => u.RoleAssignment)
            .WithOne()
            .HasForeignKey<UserRoleAssignment>(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(u => u.RoleAssignment).AutoInclude();

        builder.Property(u => u.MonthlyIncomeCents).HasColumnName("monthly_income_cents");
        builder.Property(u => u.IncomeProfileOverride).HasColumnName("income_profile_override").HasMaxLength(16); // nhóm thu nhập người dùng tự chọn cho AI Financial Agent
        builder.Property(u => u.ForecastReserveCents).HasColumnName("forecast_reserve_cents");
        builder.Property(u => u.LowBalanceThresholdCents).HasColumnName("low_balance_threshold_cents");
        builder.Property(u => u.QuietHoursStartMinute).HasColumnName("quiet_hours_start_minute");
        builder.Property(u => u.QuietHoursEndMinute).HasColumnName("quiet_hours_end_minute");
        builder.Property(u => u.TermsVersion).HasColumnName("terms_version");
        builder.Property(u => u.TermsAcceptedAt).HasColumnName("terms_accepted_at");
        builder.Property(u => u.AiConsentVersion).HasColumnName("ai_consent_version");
        builder.Property(u => u.AiConsentAt).HasColumnName("ai_consent_at");
        builder.Property(u => u.TrainingConsentVersion).HasColumnName("training_consent_version");
        builder.Property(u => u.TrainingConsentAt).HasColumnName("training_consent_at");
        builder.Property(u => u.SurveyAiConsentVersion).HasColumnName("survey_ai_consent_version");
        builder.Property(u => u.SurveyAiConsentAt).HasColumnName("survey_ai_consent_at");
        builder.Ignore(u => u.SurveyAiConsentGranted);
        builder.Ignore(u => u.CommunityConsentGranted);
        builder.Property(u => u.CommunityProvinceCode).HasColumnName("community_province_code").HasMaxLength(40);
        builder.Property(u => u.CommunityAreaCode).HasColumnName("community_area_code").HasMaxLength(40);
        builder.Property(u => u.CommunityPromptsEnabled).HasColumnName("community_prompts_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(u => u.CommunityConsentVersion).HasColumnName("community_consent_version");
        builder.Property(u => u.CommunityConsentAt).HasColumnName("community_consent_at");
        builder.Property(u => u.CommunityMutedUntil).HasColumnName("community_muted_until");
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
            prefs.Property(p => p.ForecastAlertsEnabled).HasColumnName("forecast_alerts_enabled").HasDefaultValue(true);
            prefs.Property(p => p.AlertT1Pct).HasColumnName("alert_t1_pct");
            prefs.Property(p => p.AlertT2Pct).HasColumnName("alert_t2_pct");
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
