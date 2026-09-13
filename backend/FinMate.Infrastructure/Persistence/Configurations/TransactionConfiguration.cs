using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions", t =>
        {
            t.HasCheckConstraint("chk_transactions_transaction_type", "transaction_type IN ('debit','credit','transfer')");

            // Transfer buộc phải có ví đích khác ví nguồn và không mang category (nó không phải
            // chi tiêu nên không thuộc danh mục nào); debit/credit thì ngược lại, không được có
            // ví đích. Ràng ở DB chứ không chỉ ở validator: một transfer thiếu counter_account_id
            // sẽ trừ tiền ví nguồn mà không cộng vào đâu cả — mất tiền im lặng.
            t.HasCheckConstraint(
                "chk_transactions_transfer_shape",
                "(transaction_type = 'transfer' AND counter_account_id IS NOT NULL "
                + "AND counter_account_id <> financial_account_id AND category_id IS NULL) "
                + "OR (transaction_type <> 'transfer' AND counter_account_id IS NULL)");
            t.HasCheckConstraint(
                "chk_transactions_source",
                "source IN ('notification','manual','voice','receipt')");
            t.HasCheckConstraint("chk_transactions_status", "status IN ('draft','confirmed')");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.FinancialAccountId).HasColumnName("financial_account_id").IsRequired();
        builder.Property(t => t.CounterAccountId).HasColumnName("counter_account_id");
        builder.Property(t => t.CategoryId).HasColumnName("category_id");
        builder.Property(t => t.NotificationLogId).HasColumnName("notification_log_id");
        builder.Property(t => t.SavingGoalId).HasColumnName("saving_goal_id");

        builder.Property(t => t.AmountCents).HasColumnName("amount_cents").IsRequired();

        builder.Property(t => t.TransactionType)
            .HasColumnName("transaction_type")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<TransactionType>(v, true))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(t => t.Source)
            .HasColumnName("source")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<TransactionSource>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<TransactionStatus>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.MerchantName).HasColumnName("merchant_name").HasMaxLength(200);
        builder.Property(t => t.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(t => t.TransactedAt).HasColumnName("transacted_at").IsRequired();
        builder.Property(t => t.BalanceAfterCents).HasColumnName("balance_after_cents");

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(t => t.DeletedAt == null);

        builder.HasIndex(t => new { t.UserId, t.TransactedAt, t.Id })
            .HasDatabaseName("idx_transactions_user_transacted_at");

        builder.HasIndex(t => t.FinancialAccountId).HasDatabaseName("idx_transactions_financial_account_id");
        builder.HasIndex(t => t.CounterAccountId).HasDatabaseName("idx_transactions_counter_account_id");
        builder.HasIndex(t => t.CategoryId).HasDatabaseName("idx_transactions_category_id");

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.FinancialAccount)
            .WithMany()
            .HasForeignKey(t => t.FinancialAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict giống ví nguồn: xóa ví đang là đích của transfer sẽ làm giao dịch mất một
        // nửa thông tin. Guard ở DeleteFinancialAccountCommandHandler chặn trước khi tới đây.
        builder.HasOne(t => t.CounterAccount)
            .WithMany()
            .HasForeignKey(t => t.CounterAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.NotificationLog)
            .WithOne()
            .HasForeignKey<Transaction>(t => t.NotificationLogId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.SavingGoal)
            .WithMany()
            .HasForeignKey(t => t.SavingGoalId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
