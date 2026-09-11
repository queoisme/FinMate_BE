using FinMate.Domain.Entities.Gamification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class UserMissionConfiguration : IEntityTypeConfiguration<UserMission>
{
    public void Configure(EntityTypeBuilder<UserMission> builder)
    {
        builder.ToTable("user_missions");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(m => m.MissionId).HasColumnName("mission_id").IsRequired();

        builder.Property(m => m.PeriodStart).HasColumnName("period_start").HasColumnType("date").IsRequired();
        builder.Property(m => m.PeriodEnd).HasColumnName("period_end").HasColumnType("date").IsRequired();

        builder.Property(m => m.Progress).HasColumnName("progress").HasDefaultValue(0);
        builder.Property(m => m.IsCompleted).HasColumnName("is_completed").HasDefaultValue(false);
        builder.Property(m => m.CompletedAt).HasColumnName("completed_at");
        builder.Property(m => m.ExpAwarded).HasColumnName("exp_awarded").HasDefaultValue(0);

        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(m => new { m.UserId, m.MissionId, m.PeriodStart })
            .IsUnique()
            .HasDatabaseName("uq_user_missions_user_mission_period");

        builder.HasIndex(m => new { m.UserId, m.PeriodEnd })
            .HasDatabaseName("idx_user_missions_user_period_end");

        builder.HasOne(m => m.Mission)
            .WithMany()
            .HasForeignKey(m => m.MissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
