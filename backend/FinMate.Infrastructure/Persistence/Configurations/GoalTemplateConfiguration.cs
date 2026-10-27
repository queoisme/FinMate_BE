using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class GoalTemplateConfiguration : IEntityTypeConfiguration<GoalTemplate>
{
    public void Configure(EntityTypeBuilder<GoalTemplate> builder)
    {
        builder.ToTable("goal_templates", t => t.HasCheckConstraint(
            "chk_goal_templates_default_params", "jsonb_typeof(default_params) = 'object'"));

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(g => g.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(g => g.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(g => g.Description).HasColumnName("description").HasMaxLength(500).IsRequired();

        builder.Property(g => g.DefaultParams)
            .HasColumnName("default_params")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(g => g.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        builder.Property(g => g.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(g => g.Code).IsUnique().HasDatabaseName("uq_goal_templates_code");
    }
}
