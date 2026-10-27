using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class MethodGoalTemplateConfiguration : IEntityTypeConfiguration<MethodGoalTemplate>
{
    public void Configure(EntityTypeBuilder<MethodGoalTemplate> builder)
    {
        builder.ToTable("method_goal_templates", t => t.HasCheckConstraint(
            "chk_method_goal_templates_conditions", "conditions IS NULL OR jsonb_typeof(conditions) = 'object'"));

        builder.HasKey(m => new { m.MethodId, m.TemplateId });

        builder.Property(m => m.MethodId).HasColumnName("method_id");
        builder.Property(m => m.TemplateId).HasColumnName("template_id");
        builder.Property(m => m.Conditions).HasColumnName("conditions").HasColumnType("jsonb");
        builder.Property(m => m.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();

        // Phương pháp và mẫu không bao giờ bị xóa cứng (tắt bằng is_active): chặn xóa thay vì âm thầm gỡ liên kết.
        builder.HasOne<FinanceMethod>()
            .WithMany()
            .HasForeignKey(m => m.MethodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<GoalTemplate>()
            .WithMany()
            .HasForeignKey(m => m.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.TemplateId).HasDatabaseName("idx_method_goal_templates_template_id");
    }
}
