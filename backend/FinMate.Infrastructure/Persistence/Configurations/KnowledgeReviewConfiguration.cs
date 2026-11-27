using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class KnowledgeReviewConfiguration : IEntityTypeConfiguration<KnowledgeReview>
{
    public void Configure(EntityTypeBuilder<KnowledgeReview> builder)
    {
        builder.ToTable("knowledge_reviews", t =>
        {
            t.HasCheckConstraint("chk_knowledge_reviews_status", GroupColumns.CheckIn<KnowledgeReviewStatus>("status"));

            // Phase 95: không ai tự duyệt / tự từ chối tài liệu của mình.
            t.HasCheckConstraint("chk_knowledge_reviews_two_people", "decided_by IS NULL OR submitted_by IS NULL OR decided_by <> submitted_by");
        });

        builder.HasKey(r => r.DocumentId);
        builder.Property(r => r.DocumentId).HasColumnName("document_id").ValueGeneratedNever();
        builder.Property(r => r.SubmittedBy).HasColumnName("submitted_by");
        builder.Property(r => r.SubmittedAt).HasColumnName("submitted_at").IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<KnowledgeReviewStatus>()).HasMaxLength(10).IsRequired();
        builder.Property(r => r.DecidedBy).HasColumnName("decided_by");
        builder.Property(r => r.DecidedAt).HasColumnName("decided_at");
        builder.Property(r => r.DecisionNote).HasColumnName("decision_note").HasMaxLength(1000);
    }
}

public class KnowledgeReviewCutoverConfiguration : IEntityTypeConfiguration<KnowledgeReviewCutover>
{
    public void Configure(EntityTypeBuilder<KnowledgeReviewCutover> builder)
    {
        builder.ToTable("knowledge_review_cutover", t => t.HasCheckConstraint("chk_knowledge_review_cutover_single", "id = 1"));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.TrackedSince).HasColumnName("tracked_since").IsRequired();
    }
}
