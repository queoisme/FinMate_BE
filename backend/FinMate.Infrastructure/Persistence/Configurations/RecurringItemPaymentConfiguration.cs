using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class RecurringItemPaymentConfiguration : IEntityTypeConfiguration<RecurringItemPayment>
{
    public void Configure(EntityTypeBuilder<RecurringItemPayment> builder)
    {
        builder.ToTable("recurring_item_payments", t =>
            t.HasCheckConstraint("chk_recurring_item_payments_kind", "kind IN ('matched','manual','settlement')"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(p => p.RecurringItemId).HasColumnName("recurring_item_id").IsRequired();
        builder.Property(p => p.TransactionId).HasColumnName("transaction_id");
        builder.Property(p => p.DueDate).HasColumnName("due_date").IsRequired();
        builder.Property(p => p.Kind).HasColumnName("kind")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<RecurringPaymentKind>(v, true))
            .HasMaxLength(12).IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.RecurringItem).WithMany(r => r.Payments).HasForeignKey(p => p.RecurringItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Transaction).WithMany().HasForeignKey(p => p.TransactionId).OnDelete(DeleteBehavior.Cascade);

        // Một giao dịch phủ tối đa một khoản định kỳ một lần (BR-INS-04: "chưa gắn khoản định kỳ khác").
        builder.HasIndex(p => p.TransactionId)
            .IsUnique()
            .HasFilter("transaction_id IS NOT NULL AND deleted_at IS NULL")
            .HasDatabaseName("uq_recurring_item_payments_transaction");

        // Tối đa một lần trả cho mỗi kỳ hạn (BR-INS-03); lần tất toán đứng ngoài vì nó có thể rơi đúng kỳ đã có.
        builder.HasIndex(p => new { p.RecurringItemId, p.DueDate })
            .IsUnique()
            .HasFilter("deleted_at IS NULL AND kind <> 'settlement'")
            .HasDatabaseName("uq_recurring_item_payments_item_due");

        builder.HasIndex(p => p.UserId).HasDatabaseName("ix_recurring_item_payments_user");

        builder.HasQueryFilter(p => p.DeletedAt == null);
    }
}
