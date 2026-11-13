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
                "source IN ('notification','manual','voice','receipt','support')");
            t.HasCheckConstraint("chk_transactions_status", "status IN ('draft','confirmed')");

            // Chuyển khoản nội bộ không phải chi tiêu nên không có gì để chia sẻ.
            t.HasCheckConstraint("chk_transactions_group_shareable", "group_id IS NULL OR transaction_type <> 'transfer'");
            t.HasCheckConstraint("chk_transactions_group_category", "group_category_id IS NULL OR group_id IS NOT NULL");

            // Hoàn tiền / đảo giao dịch (Phase 81). Khoản mang quan hệ luôn là tiền VÀO, nên mọi nhánh số dư hiện có (credit → +)
            // vẫn đúng mà không phải biết tới hoàn tiền; còn mọi phép cộng thu nhập phải loại nó ra (relation IS NULL).
            t.HasCheckConstraint("chk_transactions_relation", "relation IS NULL OR relation IN ('refund','reversal')");
            t.HasCheckConstraint("chk_transactions_relation_money_in", "relation IS NULL OR transaction_type = 'credit'");
            t.HasCheckConstraint("chk_transactions_related_has_relation", "related_transaction_id IS NULL OR relation IS NOT NULL");
            t.HasCheckConstraint("chk_transactions_related_not_self", "related_transaction_id IS NULL OR related_transaction_id <> id");

            // Hoàn "mồ côi" (không có khoản gốc trong app) được phép; đảo thì luôn phải trỏ tới khoản bị đảo.
            t.HasCheckConstraint("chk_transactions_reversal_has_original", "relation IS NULL OR relation <> 'reversal' OR related_transaction_id IS NOT NULL");
            t.HasCheckConstraint("chk_transactions_reversed_debit", "is_reversed = false OR transaction_type = 'debit'");

            // Bản trùng có liên kết (Phase 82). Trạng thái và liên kết luôn đi cùng nhau: bỏ nghi trùng là bỏ cả hai.
            t.HasCheckConstraint("chk_transactions_duplicate_status", "duplicate_status IS NULL OR duplicate_status IN ('suspected','dismissed')");
            t.HasCheckConstraint("chk_transactions_duplicate_link", "(duplicate_status IS NULL) = (duplicate_of_transaction_id IS NULL)");
            t.HasCheckConstraint("chk_transactions_duplicate_not_self", "duplicate_of_transaction_id IS NULL OR duplicate_of_transaction_id <> id");
            t.HasCheckConstraint("chk_transactions_merged_not_self", "merged_into_transaction_id IS NULL OR merged_into_transaction_id <> id");

            // Bản bị gộp luôn xoá mềm, để mọi truy vấn đang lọc deleted_at tự loại nó mà không phải biết tới việc gộp.
            t.HasCheckConstraint("chk_transactions_merged_deleted", "merged_into_transaction_id IS NULL OR deleted_at IS NOT NULL");
            t.HasCheckConstraint(
                "chk_transactions_merged_sources",
                "merged_sources IS NULL OR merged_sources <@ ARRAY['notification','manual','voice','receipt','support']::text[]");

            // Lý do chỉ có khi đang bị loại khỏi dự báo, và bị loại thì phải có lý do.
            t.HasCheckConstraint(
                "chk_transactions_forecast_exclusion",
                "(exclude_from_forecast = false AND forecast_exclusion_reason IS NULL) "
                + "OR (exclude_from_forecast = true AND forecast_exclusion_reason IN ('one_off','user_marked'))");
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
        builder.Property(t => t.ExcludeFromForecast).HasColumnName("exclude_from_forecast").HasDefaultValue(false);
        builder.Property(t => t.ForecastExclusionReason)
            .HasColumnName("forecast_exclusion_reason")
            .HasConversion(
                v => v == null ? null : ForecastExclusionReasonColumn.ToColumn(v.Value),
                v => v == null ? null : ForecastExclusionReasonColumn.FromColumn(v))
            .HasMaxLength(20);

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(t => t.ClientRequestId).HasColumnName("client_request_id");

        // UNIQUE một phần: chỉ ràng buộc các dòng CÓ client_request_id, nếu không mọi giao
        // dịch NULL sẽ đụng nhau. Đây mới là thứ chặn được hai request gửi lại CÙNG LÚC —
        // kiểm tra ở tầng ứng dụng rồi mới ghi vẫn có khe hở giữa đọc và ghi, mà một hàng đợi
        // offline thì bắn cả loạt song song.
        builder.HasIndex(t => new { t.UserId, t.ClientRequestId })
            .IsUnique()
            .HasFilter("client_request_id IS NOT NULL")
            .HasDatabaseName("uq_transactions_user_client_request_id");
        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at");

        builder.Property(t => t.GroupId).HasColumnName("group_id");
        builder.Property(t => t.GroupCategoryId).HasColumnName("group_category_id");
        builder.Property(t => t.SharedAt).HasColumnName("shared_at");
        builder.Property(t => t.SupportTicketId).HasColumnName("support_ticket_id");

        builder.Property(t => t.RelatedTransactionId).HasColumnName("related_transaction_id");
        builder.Property(t => t.Relation)
            .HasColumnName("relation")
            .HasConversion(
                v => v == null ? null : v.Value.ToString().ToLowerInvariant(),
                v => v == null ? null : Enum.Parse<TransactionRelation>(v, true))
            .HasMaxLength(20);
        builder.Property(t => t.IsReversed).HasColumnName("is_reversed").HasDefaultValue(false);

        // NO ACTION (không phải Restrict/Cascade): Postgres kiểm ở cuối câu lệnh, nên xoá cứng cả user (cascade xoá mọi giao dịch
        // trong MỘT câu) không vướng thứ tự gốc/hoàn. Xoá mềm thì ứng dụng chặn bằng HAS_REFUNDS trước khi tới đây.
        builder.HasOne(t => t.RelatedTransaction)
            .WithMany()
            .HasForeignKey(t => t.RelatedTransactionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(t => t.RelatedTransactionId)
            .HasFilter("related_transaction_id IS NOT NULL")
            .HasDatabaseName("ix_transactions_related_transaction_id");

        builder.Property(t => t.DuplicateOfTransactionId).HasColumnName("duplicate_of_transaction_id");
        builder.Property(t => t.DuplicateStatus)
            .HasColumnName("duplicate_status")
            .HasConversion(
                v => v == null ? null : v.Value.ToString().ToLowerInvariant(),
                v => v == null ? null : Enum.Parse<DuplicateStatus>(v, true))
            .HasMaxLength(20);
        builder.Property(t => t.MergedIntoTransactionId).HasColumnName("merged_into_transaction_id");
        builder.Property(t => t.MergedSources).HasColumnName("merged_sources");
        builder.Property(t => t.TransferDismissedAt).HasColumnName("transfer_dismissed_at");

        // NO ACTION, cùng lý do với related_transaction_id: xoá cứng user xoá cả chuỗi trong một câu lệnh.
        builder.HasOne(t => t.DuplicateOf)
            .WithMany()
            .HasForeignKey(t => t.DuplicateOfTransactionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(t => t.DuplicateOfTransactionId)
            .HasFilter("duplicate_of_transaction_id IS NOT NULL")
            .HasDatabaseName("ix_transactions_duplicate_of_transaction_id");
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(t => t.MergedIntoTransactionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(t => t.MergedIntoTransactionId)
            .HasFilter("merged_into_transaction_id IS NOT NULL")
            .HasDatabaseName("ix_transactions_merged_into_transaction_id");

        builder.HasQueryFilter(t => t.DeletedAt == null);

        builder.HasIndex(t => new { t.GroupId, t.TransactedAt })
            .HasFilter("group_id IS NOT NULL")
            .HasDatabaseName("ix_transactions_group_transacted_at");
        builder.HasOne<Group>().WithMany().HasForeignKey(t => t.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.GroupCategory).WithMany().HasForeignKey(t => t.GroupCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupportTicket>().WithMany().HasForeignKey(t => t.SupportTicketId).OnDelete(DeleteBehavior.SetNull);

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
