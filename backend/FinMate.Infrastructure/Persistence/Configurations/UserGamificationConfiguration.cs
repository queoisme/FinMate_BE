using FinMate.Domain.Entities.Gamification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class UserGamificationConfiguration : IEntityTypeConfiguration<UserGamification>
{
    public void Configure(EntityTypeBuilder<UserGamification> builder)
    {
        builder.ToTable("user_gamification", t =>
        {
            t.HasCheckConstraint("chk_user_gamification_level", "level >= 1");
            t.HasCheckConstraint("chk_user_gamification_exp", "exp_points >= 0");
        });

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(g => g.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(g => g.Level).HasColumnName("level").HasDefaultValue(1);
        builder.Property(g => g.ExpPoints).HasColumnName("exp_points").HasDefaultValue(0);
        builder.Property(g => g.CurrentStreakDays).HasColumnName("current_streak_days").HasDefaultValue(0);
        builder.Property(g => g.LongestStreakDays).HasColumnName("longest_streak_days").HasDefaultValue(0);
        builder.Property(g => g.LastActivityDate).HasColumnName("last_activity_date").HasColumnType("date");

        builder.Property(g => g.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(g => g.UserId).IsUnique().HasDatabaseName("uq_user_gamification_user_id");

        // StreakCheckJob quét theo ngày hoạt động gần nhất.
        builder.HasIndex(g => g.LastActivityDate).HasDatabaseName("idx_user_gamification_last_activity");

        builder.HasOne(g => g.User)
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
