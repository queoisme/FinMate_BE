using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class FinanceMethodConfiguration : IEntityTypeConfiguration<FinanceMethod>
{
    private static readonly Dictionary<GoalMode, string> GoalModeToColumn = new()
    {
        [GoalMode.ReserveFirst] = "reserve_first",
        [GoalMode.Envelope] = "envelope",
        [GoalMode.AvailableToSpend] = "available_to_spend",
        [GoalMode.TrackOnly] = "track_only",
    };

    private static readonly Dictionary<string, GoalMode> GoalModeFromColumn =
        GoalModeToColumn.ToDictionary(kv => kv.Value, kv => kv.Key);

    public void Configure(EntityTypeBuilder<FinanceMethod> builder)
    {
        builder.ToTable("finance_methods", t =>
        {
            t.HasCheckConstraint("chk_finance_methods_role", "role IN ('primary','supporting','both')");
            t.HasCheckConstraint(
                "chk_finance_methods_goal_mode",
                "goal_mode IN ('reserve_first','envelope','available_to_spend','track_only')");
            t.HasCheckConstraint("chk_finance_methods_param_defs", "jsonb_typeof(param_defs) = 'array'");
            t.HasCheckConstraint("chk_finance_methods_eligibility", "jsonb_typeof(eligibility) = 'object'");
            t.HasCheckConstraint("chk_finance_methods_starter_steps", "jsonb_typeof(starter_steps) = 'array'");
            t.HasCheckConstraint("chk_finance_methods_version", "version >= 1");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(m => m.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(m => m.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();

        builder.Property(m => m.Role)
            .HasColumnName("role")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<FinanceMethodRole>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.GoalMode)
            .HasColumnName("goal_mode")
            .HasConversion(v => GoalModeToColumn[v], v => GoalModeFromColumn[v])
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(m => m.ParamDefs)
            .HasColumnName("param_defs")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(m => m.Eligibility)
            .HasColumnName("eligibility")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(m => m.WhyText).HasColumnName("why_text").HasMaxLength(1000).IsRequired();

        builder.Property(m => m.StarterSteps)
            .HasColumnName("starter_steps")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(m => m.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        builder.Property(m => m.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(m => m.Version).HasColumnName("version").HasDefaultValue(1);

        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(m => m.Code).IsUnique().HasDatabaseName("uq_finance_methods_code");
        builder.HasIndex(m => new { m.IsActive, m.SortOrder }).HasDatabaseName("idx_finance_methods_active_order");
    }
}
