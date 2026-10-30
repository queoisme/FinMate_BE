using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class QrTransferIntentConfiguration : IEntityTypeConfiguration<QrTransferIntent>
{
    public void Configure(EntityTypeBuilder<QrTransferIntent> builder)
    {
        builder.ToTable("qr_transfer_intents", t =>
        {
            t.HasCheckConstraint("chk_qr_transfer_intents_status", "status IN ('pending','matched','cancelled')");
            t.HasCheckConstraint("chk_qr_transfer_intents_amount", "amount_cents > 0");
            t.HasCheckConstraint("chk_qr_transfer_intents_bins", "receiver_bank_bin ~ '^[0-9]{6}$' AND sender_bank_bin ~ '^[0-9]{6}$'");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(i => i.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(i => i.ReceiverBankBin).HasColumnName("receiver_bank_bin").HasMaxLength(6).IsRequired();
        builder.Property(i => i.ReceiverAccountNumber).HasColumnName("receiver_account_number").HasMaxLength(32).IsRequired();
        builder.Property(i => i.ReceiverName).HasColumnName("receiver_name").HasMaxLength(100);
        builder.Property(i => i.SenderBankBin).HasColumnName("sender_bank_bin").HasMaxLength(6).IsRequired();
        builder.Property(i => i.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(i => i.Message).HasColumnName("message").HasMaxLength(100);
        builder.Property(i => i.CategoryId).HasColumnName("category_id");

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<QrTransferStatus>(v, true))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(i => i.ScannedAt).HasColumnName("scanned_at").IsRequired();
        builder.Property(i => i.MatchedTransactionId).HasColumnName("matched_transaction_id");
        builder.Property(i => i.MatchedAt).HasColumnName("matched_at");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne(i => i.User).WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>().WithMany().HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.SetNull);

        // Xóa giao dịch không được làm mất lịch sử quét: chỉ bỏ liên kết.
        builder.HasOne<Transaction>().WithMany().HasForeignKey(i => i.MatchedTransactionId).OnDelete(DeleteBehavior.SetNull);

        // Màn "Lịch sử QR": mới nhất trước, theo người dùng.
        builder.HasIndex(i => new { i.UserId, i.ScannedAt }).IsDescending(false, true).HasDatabaseName("ix_qr_transfer_intents_user_scanned");

        // Đường khớp thông báo: chỉ nhìn các dòng đang chờ nên index nhỏ và không phình theo lịch sử.
        builder.HasIndex(i => new { i.UserId, i.SenderBankBin, i.AmountCents })
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ix_qr_transfer_intents_pending_match");

        builder.HasIndex(i => i.MatchedTransactionId).HasDatabaseName("ix_qr_transfer_intents_matched_transaction");
    }
}
