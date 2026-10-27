using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class SurveyQuestionConfiguration : IEntityTypeConfiguration<SurveyQuestion>
{
    public void Configure(EntityTypeBuilder<SurveyQuestion> builder)
    {
        builder.ToTable("survey_questions", t => t.HasCheckConstraint(
            "chk_survey_questions_show_if", "show_if IS NULL OR jsonb_typeof(show_if) = 'object'"));

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(q => q.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(q => q.GroupCode).HasColumnName("group_code").HasMaxLength(50).IsRequired();
        builder.Property(q => q.Text).HasColumnName("text").HasMaxLength(500).IsRequired();
        builder.Property(q => q.ShowIf).HasColumnName("show_if").HasColumnType("jsonb");
        builder.Property(q => q.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        builder.Property(q => q.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        builder.Property(q => q.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(q => q.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(q => q.Code).IsUnique().HasDatabaseName("uq_survey_questions_code");
        builder.HasIndex(q => new { q.IsActive, q.SortOrder }).HasDatabaseName("idx_survey_questions_active_order");
    }
}
