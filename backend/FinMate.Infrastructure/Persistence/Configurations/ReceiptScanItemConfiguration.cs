using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class ReceiptScanItemConfiguration : IEntityTypeConfiguration<ReceiptScanItem>
{
    public void Configure(EntityTypeBuilder<ReceiptScanItem> builder)
    {
        builder.ToTable("receipt_scan_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.ScanId).HasColumnName("scan_id").IsRequired();
        builder.Property(i => i.Position).HasColumnName("position").IsRequired();
        builder.Property(i => i.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        // Không có check dương: dòng giảm giá mang số âm.
        builder.Property(i => i.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(i => i.CategorySlug).HasColumnName("category_slug").HasMaxLength(100);
        builder.Property(i => i.CategorySource).HasColumnName("category_source").HasMaxLength(16);
        builder.Property(i => i.ParentPosition).HasColumnName("parent_position");

        builder.HasIndex(i => new { i.ScanId, i.Position })
            .IsUnique()
            .HasDatabaseName("uq_receipt_scan_items_scan_position");

        // Xóa lần quét (kể cả khi user bị xóa hẳn) thì xóa luôn các món.
        builder.HasOne<ReceiptScan>()
            .WithMany()
            .HasForeignKey(i => i.ScanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
