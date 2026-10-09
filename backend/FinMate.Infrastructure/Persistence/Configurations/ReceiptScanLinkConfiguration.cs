using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class ReceiptScanLinkConfiguration : IEntityTypeConfiguration<ReceiptScanLink>
{
    public void Configure(EntityTypeBuilder<ReceiptScanLink> builder)
    {
        builder.ToTable("receipt_scan_links");

        // transaction_id là khóa chính: một giao dịch đến từ tối đa một lần quét (phép so "AI đoán đúng bao nhiêu" không đếm trùng).
        builder.HasKey(l => l.TransactionId);
        builder.Property(l => l.TransactionId).HasColumnName("transaction_id");
        builder.Property(l => l.ScanId).HasColumnName("scan_id").IsRequired();
        builder.Property(l => l.GroupCategorySlug).HasColumnName("group_category_slug").HasMaxLength(100).IsRequired();

        // Mỗi nhóm của một lần quét nối tối đa một giao dịch: bấm Lưu hai lần không nối thêm.
        builder.HasIndex(l => new { l.ScanId, l.GroupCategorySlug })
            .IsUnique()
            .HasDatabaseName("uq_receipt_scan_links_scan_group");

        // Giao dịch bị xóa hẳn thì chỉ gỡ dòng nối, lần quét và các món vẫn còn (giữ ý nghĩa SET NULL của cột cũ).
        builder.HasOne(l => l.Transaction)
            .WithMany()
            .HasForeignKey(l => l.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Scan)
            .WithMany()
            .HasForeignKey(l => l.ScanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
