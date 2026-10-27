using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class UserSurveyAnswerConfiguration : IEntityTypeConfiguration<UserSurveyAnswer>
{
    public void Configure(EntityTypeBuilder<UserSurveyAnswer> builder)
    {
        builder.ToTable("user_survey_answers");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(a => a.QuestionCode).HasColumnName("question_code").HasMaxLength(50).IsRequired();
        builder.Property(a => a.OptionCode).HasColumnName("option_code").HasMaxLength(10).IsRequired();
        builder.Property(a => a.AnsweredAt).HasColumnName("answered_at").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Mỗi người chỉ có MỘT câu trả lời cho mỗi câu hỏi; trả lời lại thì ghi đè.
        builder.HasIndex(a => new { a.UserId, a.QuestionCode })
            .IsUnique()
            .HasDatabaseName("uq_user_survey_answers_user_question");

        // Xóa tài khoản kéo theo câu trả lời — UserHardDeleter chỉ xóa dòng users, phần còn lại dựa vào cascade.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
