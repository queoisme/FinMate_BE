using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class MethodRecommendationConfiguration : IEntityTypeConfiguration<MethodRecommendation>
{
    private static readonly Dictionary<RecommendationSource, string> SourceToColumn = new()
    {
        [RecommendationSource.Ai] = "ai",
        [RecommendationSource.RuleFallback] = "rule_fallback",
    };

    private static readonly Dictionary<string, RecommendationSource> SourceFromColumn =
        SourceToColumn.ToDictionary(kv => kv.Value, kv => kv.Key);

    public void Configure(EntityTypeBuilder<MethodRecommendation> builder)
    {
        builder.ToTable("method_recommendations", t =>
        {
            t.HasCheckConstraint("chk_method_recommendations_source", "source IN ('ai','rule_fallback')");
            t.HasCheckConstraint(
                "chk_method_recommendations_outcome", "outcome IN ('shown','accepted','changed','skipped')");
            t.HasCheckConstraint("chk_method_recommendations_reasons", "jsonb_typeof(reasons) = 'array'");
            t.HasCheckConstraint(
                "chk_method_recommendations_signals_snapshot", "jsonb_typeof(signals_snapshot) = 'object'");
            t.HasCheckConstraint(
                "chk_method_recommendations_method_versions", "jsonb_typeof(method_versions) = 'object'");

            // Cách bổ trợ phải khác cách chính — gợi ý cùng một phương pháp hai lần là lỗi của bộ chọn.
            t.HasCheckConstraint(
                "chk_method_recommendations_support_differs",
                "support_method_id IS NULL OR support_method_id <> primary_method_id");

            // decided_at và outcome phải khớp nhau: chưa quyết định thì không có mốc, đã quyết định thì phải có.
            t.HasCheckConstraint(
                "chk_method_recommendations_decided",
                "(outcome = 'shown') = (decided_at IS NULL)");

            // Nguồn AI thì phải biết prompt phiên bản nào đã chạy, không thì không truy ngược được.
            t.HasCheckConstraint(
                "chk_method_recommendations_prompt_version",
                "source <> 'ai' OR prompt_version IS NOT NULL");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(r => r.PrimaryMethodId).HasColumnName("primary_method_id").IsRequired();
        builder.Property(r => r.SupportMethodId).HasColumnName("support_method_id");

        builder.Property(r => r.Reasons)
            .HasColumnName("reasons")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(r => r.SignalsSnapshot)
            .HasColumnName("signals_snapshot")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(r => r.Source)
            .HasColumnName("source")
            .HasConversion(v => SourceToColumn[v], v => SourceFromColumn[v])
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.PromptVersion).HasColumnName("prompt_version").HasMaxLength(50);

        builder.Property(r => r.MethodVersions)
            .HasColumnName("method_versions")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(r => r.Outcome)
            .HasColumnName("outcome")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<RecommendationOutcome>(v, true))
            .HasMaxLength(20)
            .HasDefaultValue(RecommendationOutcome.Shown)
            .IsRequired();

        builder.Property(r => r.DecidedAt).HasColumnName("decided_at");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(r => new { r.UserId, r.CreatedAt })
            .HasDatabaseName("idx_method_recommendations_user_created")
            .IsDescending(false, true);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<FinanceMethod>()
            .WithMany()
            .HasForeignKey(r => r.PrimaryMethodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FinanceMethod>()
            .WithMany()
            .HasForeignKey(r => r.SupportMethodId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
