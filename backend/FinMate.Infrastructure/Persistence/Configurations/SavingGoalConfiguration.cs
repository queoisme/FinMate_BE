using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class SavingGoalConfiguration : IEntityTypeConfiguration<SavingGoal>
{
    public void Configure(EntityTypeBuilder<SavingGoal> builder)
    {
        builder.ToTable("saving_goals", t =>
        {
            t.HasCheckConstraint("chk_saving_goals_status", "status IN ('draft','active','completed','cancelled')");
            t.HasCheckConstraint("chk_saving_goals_contribution_frequency", "contribution_frequency IS NULL OR contribution_frequency IN ('weekly','monthly','custom')");
            t.HasCheckConstraint("chk_saving_goals_initial_saved_cents", "initial_saved_cents >= 0");
            t.HasCheckConstraint("chk_saving_goals_target_cents", "target_cents > 0");
            t.HasCheckConstraint("chk_saving_goals_owner", "(user_id IS NULL) <> (group_id IS NULL)");
            t.HasCheckConstraint(
                "chk_saving_goals_market",
                "(market_topic IS NULL AND market_baseline_cents IS NULL AND market_baseline_at IS NULL) OR (market_topic IS NOT NULL AND market_baseline_cents > 0 AND market_baseline_at IS NOT NULL AND user_id IS NOT NULL)");
        });

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(g => g.UserId).HasColumnName("user_id");
        builder.Property(g => g.GroupId).HasColumnName("group_id");
        builder.Property(g => g.CreatedByUserId).HasColumnName("created_by_user_id");

        builder.Property(g => g.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(g => g.TargetCents).HasColumnName("target_cents").IsRequired();
        builder.Property(g => g.SavedCents).HasColumnName("saved_cents").HasDefaultValue(0L);
        builder.Property(g => g.InitialSavedCents).HasColumnName("initial_saved_cents").HasDefaultValue(0L);
        builder.Property(g => g.Icon).HasColumnName("icon").HasMaxLength(40);
        builder.Property(g => g.ContributionFrequency)
            .HasColumnName("contribution_frequency")
            .HasConversion(v => v == null ? null : v.Value.ToString().ToLowerInvariant(), v => v == null ? null : Enum.Parse<GoalContributionFrequency>(v, true))
            .HasMaxLength(20);

        builder.Property(g => g.Status)
            .HasColumnName("status")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<SavingGoalStatus>(v, true))
            .HasMaxLength(20)
            .HasDefaultValue(SavingGoalStatus.Active)
            .IsRequired();

        builder.Property(g => g.Deadline).HasColumnName("deadline");
        builder.Property(g => g.CompletedAt).HasColumnName("completed_at");
        builder.Property(g => g.DeadlineNotifiedAt).HasColumnName("deadline_notified_at");

        builder.Property(g => g.GoalTemplateId).HasColumnName("goal_template_id");
        builder.Property(g => g.UserMethodId).HasColumnName("user_method_id");
        builder.Property(g => g.MarketTopic).HasColumnName("market_topic").HasMaxLength(32);
        builder.Property(g => g.MarketBaselineCents).HasColumnName("market_baseline_cents");
        builder.Property(g => g.MarketBaselineAt).HasColumnName("market_baseline_at");

        builder.Property(g => g.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(g => g.DeletedAt).HasColumnName("deleted_at");

        // Khóa lạc quan theo cột hệ thống xmin của PostgreSQL: số tiền ở bảng này được đọc rồi cộng trong tiến trình, thiếu khóa thì hai request đồng thời làm mất một khoản.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasQueryFilter(g => g.DeletedAt == null);

        builder.HasIndex(g => g.UserId).HasDatabaseName("idx_saving_goals_user_id");
        builder.HasIndex(g => g.GroupId).HasFilter("group_id IS NOT NULL").HasDatabaseName("ix_saving_goals_group_id");
        builder.HasOne<Group>().WithMany().HasForeignKey(g => g.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(g => g.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(g => g.User)
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Mẫu và phương pháp chỉ là NGUỒN GỐC của mục tiêu: mất chúng thì mục tiêu vẫn sống, chỉ mất nhãn gốc.
        builder.HasOne<GoalTemplate>()
            .WithMany()
            .HasForeignKey(g => g.GoalTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<UserMethod>()
            .WithMany()
            .HasForeignKey(g => g.UserMethodId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
