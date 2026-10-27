using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class SurveyOptionConfiguration : IEntityTypeConfiguration<SurveyOption>
{
    public void Configure(EntityTypeBuilder<SurveyOption> builder)
    {
        builder.ToTable("survey_options", t => t.HasCheckConstraint(
            "chk_survey_options_signals", "jsonb_typeof(signals) = 'object'"));

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(o => o.QuestionId).HasColumnName("question_id").IsRequired();
        builder.Property(o => o.Code).HasColumnName("code").HasMaxLength(10).IsRequired();
        builder.Property(o => o.Text).HasColumnName("text").HasMaxLength(500).IsRequired();

        builder.Property(o => o.Signals)
            .HasColumnName("signals")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(o => o.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        builder.Property(o => o.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(o => new { o.QuestionId, o.Code }).IsUnique().HasDatabaseName("uq_survey_options_question_code");

        // Câu hỏi không bao giờ bị xóa cứng (tắt bằng is_active), nên chặn xóa thay vì kéo theo đáp án.
        builder.HasOne<SurveyQuestion>()
            .WithMany()
            .HasForeignKey(o => o.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
