using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class MissionConfiguration : IEntityTypeConfiguration<Mission>
{
    private static readonly Dictionary<MissionPeriodType, string> PeriodToColumn = new()
    {
        [MissionPeriodType.Daily] = "daily",
        [MissionPeriodType.Weekly] = "weekly",
        [MissionPeriodType.OneTime] = "one_time",
    };

    private static readonly Dictionary<string, MissionPeriodType> PeriodFromColumn =
        PeriodToColumn.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly Dictionary<MissionConditionType, string> ConditionToColumn = new()
    {
        [MissionConditionType.ConfirmTransaction] = "confirm_transaction",
        [MissionConditionType.CreateManualTransaction] = "create_manual_transaction",
        [MissionConditionType.ContributeToGoal] = "contribute_to_goal",
        [MissionConditionType.StayUnderBudget] = "stay_under_budget",
        [MissionConditionType.LoginStreak] = "login_streak",
    };

    private static readonly Dictionary<string, MissionConditionType> ConditionFromColumn =
        ConditionToColumn.ToDictionary(kv => kv.Value, kv => kv.Key);

    public void Configure(EntityTypeBuilder<Mission> builder)
    {
        builder.ToTable("missions", t =>
        {
            t.HasCheckConstraint("chk_missions_period_type", "period_type IN ('daily','weekly','one_time')");
            t.HasCheckConstraint("chk_missions_condition_target", "condition_target > 0");
            t.HasCheckConstraint("chk_missions_exp_reward", "exp_reward >= 0");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(m => m.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(m => m.Description).HasColumnName("description").HasMaxLength(500).IsRequired();

        builder.Property(m => m.PeriodType)
            .HasColumnName("period_type")
            .HasConversion(v => PeriodToColumn[v], v => PeriodFromColumn[v])
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.ConditionType)
            .HasColumnName("condition_type")
            .HasConversion(v => ConditionToColumn[v], v => ConditionFromColumn[v])
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(m => m.ConditionTarget).HasColumnName("condition_target").IsRequired();
        builder.Property(m => m.ExpReward).HasColumnName("exp_reward").IsRequired();
        builder.Property(m => m.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(m => m.Code).IsUnique().HasDatabaseName("uq_missions_code");
    }
}
