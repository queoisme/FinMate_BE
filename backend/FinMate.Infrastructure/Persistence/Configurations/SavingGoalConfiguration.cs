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
            t.HasCheckConstraint("chk_saving_goals_status", "status IN ('active','completed','cancelled')");
            t.HasCheckConstraint("chk_saving_goals_target_cents", "target_cents > 0");
        });

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(g => g.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(g => g.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(g => g.TargetCents).HasColumnName("target_cents").IsRequired();
        builder.Property(g => g.SavedCents).HasColumnName("saved_cents").HasDefaultValue(0L);

        builder.Property(g => g.Status)
            .HasColumnName("status")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<SavingGoalStatus>(v, true))
            .HasMaxLength(20)
            .HasDefaultValue(SavingGoalStatus.Active)
            .IsRequired();

        builder.Property(g => g.Deadline).HasColumnName("deadline");
        builder.Property(g => g.CompletedAt).HasColumnName("completed_at");
        builder.Property(g => g.DeadlineNotifiedAt).HasColumnName("deadline_notified_at");

        builder.Property(g => g.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(g => g.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(g => g.DeletedAt == null);

        builder.HasIndex(g => g.UserId).HasDatabaseName("idx_saving_goals_user_id");

        builder.HasOne(g => g.User)
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
