using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class SavingGoalConfiguration : IEntityTypeConfiguration<SavingGoal>
{
    public void Configure(EntityTypeBuilder<SavingGoal> builder)
    {
        builder.ToTable("saving_goals");

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
            .HasMaxLength(20)
            .HasDefaultValue("active")
            .IsRequired();

        builder.Property(g => g.Deadline).HasColumnName("deadline");

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
