using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class AiResultConfiguration : IEntityTypeConfiguration<AiResult>
{
    // "non_financial"/"extraction_failed" có underscore — không dùng ToLowerInvariant() đơn thuần
    // như các enum khác trong project, phải map tường minh từng giá trị.
    private static readonly Dictionary<PipelineResult, string> ToColumn = new()
    {
        [PipelineResult.Financial] = "financial",
        [PipelineResult.NonFinancial] = "non_financial",
        [PipelineResult.Uncertain] = "uncertain",
        [PipelineResult.ExtractionFailed] = "extraction_failed",
        [PipelineResult.Error] = "error",
    };

    private static readonly Dictionary<string, PipelineResult> FromColumn =
        ToColumn.ToDictionary(kv => kv.Value, kv => kv.Key);

    public void Configure(EntityTypeBuilder<AiResult> builder)
    {
        builder.ToTable("ai_results", t => t.HasCheckConstraint(
            "chk_ai_results_pipeline_result",
            "pipeline_result IN ('financial','non_financial','uncertain','extraction_failed','error')"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(a => a.NotificationLogId).HasColumnName("notification_log_id").IsRequired();

        builder.Property(a => a.PipelineResult)
            .HasColumnName("pipeline_result")
            .HasConversion(v => ToColumn[v], v => FromColumn[v])
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.ClassifierLabel).HasColumnName("classifier_label").HasMaxLength(20);
        builder.Property(a => a.ClassifierConfidence).HasColumnName("classifier_confidence");

        builder.Property(a => a.AmountCents).HasColumnName("amount_cents");
        builder.Property(a => a.TransactionType)
            .HasColumnName("transaction_type")
            .HasConversion(
                v => v == null ? null : v.Value.ToString().ToLowerInvariant(),
                v => v == null ? null : Enum.Parse<TransactionType>(v, true))
            .HasMaxLength(10);
        builder.Property(a => a.MerchantName).HasColumnName("merchant_name").HasMaxLength(200);
        builder.Property(a => a.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(a => a.TransactedAt).HasColumnName("transacted_at");
        builder.Property(a => a.BalanceAfterCents).HasColumnName("balance_after_cents");
        builder.Property(a => a.ExtractionConfidence).HasColumnName("extraction_confidence");

        builder.Property(a => a.CategorySlug).HasColumnName("category_slug").HasMaxLength(100);
        builder.Property(a => a.CategorizationConfidence).HasColumnName("categorization_confidence");

        builder.Property(a => a.IsPotentialDuplicate).HasColumnName("is_potential_duplicate").HasDefaultValue(false);
        builder.Property(a => a.DuplicateRequestId).HasColumnName("duplicate_request_id");

        builder.Property(a => a.ClassifierVersion).HasColumnName("classifier_version").HasMaxLength(20);
        builder.Property(a => a.ExtractorVersion).HasColumnName("extractor_version").HasMaxLength(20);
        builder.Property(a => a.CategorizerVersion).HasColumnName("categorizer_version").HasMaxLength(20);

        builder.Property(a => a.ProcessingMs).HasColumnName("processing_ms");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(a => a.NotificationLogId)
            .IsUnique()
            .HasDatabaseName("uq_ai_results_notification_log_id");

        builder.HasOne(a => a.NotificationLog)
            .WithOne(n => n.AiResult)
            .HasForeignKey<AiResult>(a => a.NotificationLogId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
