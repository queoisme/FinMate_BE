using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("loans", t =>
        {
            // Phase 90: gốc = Σ dòng giải ngân còn sống, nên được bằng 0 trong lúc khoản đang bị xoá theo các dòng.
            t.HasCheckConstraint("ck_loans_principal", "principal_cents >= 0");
            t.HasCheckConstraint("ck_loans_interest", "interest_rate_percent_year >= 0 AND interest_rate_percent_year <= 100");
            t.HasCheckConstraint("ck_loans_repaid", "repaid_cents >= 0 AND principal_cents >= repaid_cents");
            t.HasCheckConstraint("ck_loans_status", "status IN ('open','settled','written_off')");
            t.HasCheckConstraint("ck_loans_settled_at", "(status = 'settled') = (settled_at IS NOT NULL)");
            t.HasCheckConstraint("ck_loans_written_off_at", "(status = 'written_off') = (written_off_at IS NOT NULL)");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(l => l.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(l => l.Direction).HasColumnName("direction").HasMaxLength(10).IsRequired()
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<LoanDirection>(v, true));
        builder.Property(l => l.Title).HasColumnName("title").HasMaxLength(100).IsRequired();
        builder.Property(l => l.Counterparty).HasColumnName("counterparty").HasMaxLength(100).IsRequired();
        builder.Property(l => l.PrincipalCents).HasColumnName("principal_cents").IsRequired();
        builder.Property(l => l.InterestRatePercentYear).HasColumnName("interest_rate_percent_year").HasPrecision(5, 2).IsRequired();
        builder.Property(l => l.StartDate).HasColumnName("start_date").IsRequired();
        builder.Property(l => l.DueDate).HasColumnName("due_date");
        builder.Property(l => l.DueNotifiedAt).HasColumnName("due_notified_at");
        builder.Property(l => l.RepaidCents).HasColumnName("repaid_cents").IsRequired();
        builder.Property(l => l.Status).HasColumnName("status").HasMaxLength(12).IsRequired()
            .HasConversion(v => GroupColumns.SnakeCase(v.ToString()), v => Enum.Parse<LoanStatus>(v.Replace("_", string.Empty), true));
        builder.Property(l => l.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(l => l.SettledAt).HasColumnName("settled_at");
        builder.Property(l => l.WrittenOffAt).HasColumnName("written_off_at");
        builder.Property(l => l.DeletedAt).HasColumnName("deleted_at");
        builder.Property(l => l.ClientRequestId).HasColumnName("client_request_id");
        // Khóa lạc quan: hai lần ghi trả đồng thời không cùng qua được kiểm "không trả quá" (middleware đổi xung đột thành 409).
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => l.UserId).HasDatabaseName("ix_loans_user_id");
        builder.HasIndex(l => new { l.UserId, l.Status, l.DueDate }).HasDatabaseName("ix_loans_user_status_due").HasFilter("deleted_at IS NULL");
        builder.HasIndex(l => new { l.UserId, l.ClientRequestId }).IsUnique().HasDatabaseName("ux_loans_user_client_request")
            .HasFilter("client_request_id IS NOT NULL");
    }
}

public class LoanRepaymentConfiguration : IEntityTypeConfiguration<LoanRepayment>
{
    public void Configure(EntityTypeBuilder<LoanRepayment> builder)
    {
        builder.ToTable("loan_repayments", t =>
        {
            t.HasCheckConstraint("ck_loan_repayments_amount", "amount_cents > 0");
            t.HasCheckConstraint("ck_loan_repayments_kind", "kind IN ('principal','repayment')");
            t.HasCheckConstraint("ck_loan_repayments_interest_only_on_repayment", "interest_transaction_id IS NULL OR kind = 'repayment'");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.LoanId).HasColumnName("loan_id").IsRequired();
        builder.Property(r => r.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(r => r.PaidAt).HasColumnName("paid_at").IsRequired();
        builder.Property(r => r.TransactionId).HasColumnName("transaction_id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.Kind).HasColumnName("kind").HasMaxLength(10).IsRequired()
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<LoanEntryKind>(v, true));   // mặc định 'repayment' cho dòng cũ chỉ ở migration
        builder.Property(r => r.InterestTransactionId).HasColumnName("interest_transaction_id");
        builder.Property(r => r.DeletedAt).HasColumnName("deleted_at");
        builder.Property(r => r.ClientRequestId).HasColumnName("client_request_id");

        // Phase 90: một giao dịch gắn tối đa một dòng còn sống; xoá giao dịch (mềm) do ILoanLinkSync xử lý. Hai FK này là DEFERRABLE INITIALLY
        // DEFERRED (SQL trong migration MergeDebtBookIntoLoans, EF không biểu diễn được): xoá cứng người dùng cascade transactions trước loan_repayments.
        builder.HasOne<Transaction>().WithMany().HasForeignKey(r => r.TransactionId).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_loan_repayments_transaction");
        builder.HasOne<Transaction>().WithMany().HasForeignKey(r => r.InterestTransactionId).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_loan_repayments_interest_transaction");
        builder.HasIndex(r => r.TransactionId).IsUnique().HasDatabaseName("ux_loan_repayments_transaction")
            .HasFilter("transaction_id IS NOT NULL AND deleted_at IS NULL");
        builder.HasIndex(r => r.InterestTransactionId).IsUnique().HasDatabaseName("ux_loan_repayments_interest_transaction")
            .HasFilter("interest_transaction_id IS NOT NULL");
        builder.HasIndex(r => new { r.LoanId, r.ClientRequestId }).IsUnique().HasDatabaseName("ux_loan_repayments_client_request")
            .HasFilter("client_request_id IS NOT NULL");

        builder.HasOne<Loan>().WithMany(l => l.Repayments).HasForeignKey(r => r.LoanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.LoanId).HasDatabaseName("ix_loan_repayments_loan_id");
    }
}
