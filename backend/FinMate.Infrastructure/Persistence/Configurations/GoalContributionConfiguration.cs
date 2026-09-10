using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class GoalContributionConfiguration : IEntityTypeConfiguration<GoalContribution>
{
    public void Configure(EntityTypeBuilder<GoalContribution> builder)
    {
        builder.ToTable("goal_contributions", t =>
            t.HasCheckConstraint("chk_goal_contributions_amount_cents", "amount_cents > 0"));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.SavingGoalId).HasColumnName("saving_goal_id").IsRequired();
        builder.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(c => c.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(c => c.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(c => c.ContributedAt).HasColumnName("contributed_at").IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(c => new { c.SavingGoalId, c.ContributedAt })
            .HasDatabaseName("idx_goal_contributions_goal_contributed_at");

        builder.HasOne(c => c.SavingGoal)
            .WithMany(g => g.Contributions)
            .HasForeignKey(c => c.SavingGoalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
