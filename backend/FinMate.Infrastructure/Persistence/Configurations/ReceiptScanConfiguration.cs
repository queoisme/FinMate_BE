using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class ReceiptScanConfiguration : IEntityTypeConfiguration<ReceiptScan>
{
    public void Configure(EntityTypeBuilder<ReceiptScan> builder)
    {
        builder.ToTable("receipt_scans", t =>
        {
            t.HasCheckConstraint("chk_receipt_scans_ocr_result", "ocr_result IN ('success','no_amount','unreadable')");
            t.HasCheckConstraint("chk_receipt_scans_amount_positive", "amount_cents IS NULL OR amount_cents > 0");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.OcrResult).HasColumnName("ocr_result").HasMaxLength(16).IsRequired();
        builder.Property(s => s.AmountCents).HasColumnName("amount_cents");
        builder.Property(s => s.MerchantName).HasColumnName("merchant_name").HasMaxLength(200);
        builder.Property(s => s.TransactedAt).HasColumnName("transacted_at");
        builder.Property(s => s.CategorySlug).HasColumnName("category_slug").HasMaxLength(100);
        builder.Property(s => s.CategorySource).HasColumnName("category_source").HasMaxLength(16);
        builder.Property(s => s.Confidence).HasColumnName("confidence");
        builder.Property(s => s.NeedsReview).HasColumnName("needs_review").HasDefaultValue(false).IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(s => new { s.UserId, s.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_receipt_scans_user_created");

        // Xóa tài khoản thì xóa luôn các lần quét: DataDeletionJob hard-delete user, không để dữ liệu mồ côi của người đã rời đi.
        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
