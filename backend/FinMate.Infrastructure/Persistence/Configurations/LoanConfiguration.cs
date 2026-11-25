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
            t.HasCheckConstraint("ck_loans_principal", "principal_cents > 0");
            t.HasCheckConstraint("ck_loans_interest", "interest_rate_percent_year >= 0 AND interest_rate_percent_year <= 100");
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
        // Khóa lạc quan: hai lần ghi trả đồng thời không cùng qua được kiểm "không trả quá" (middleware đổi xung đột thành 409).
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => l.UserId).HasDatabaseName("ix_loans_user_id");
    }
}

public class LoanRepaymentConfiguration : IEntityTypeConfiguration<LoanRepayment>
{
    public void Configure(EntityTypeBuilder<LoanRepayment> builder)
    {
        builder.ToTable("loan_repayments", t => t.HasCheckConstraint("ck_loan_repayments_amount", "amount_cents > 0"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.LoanId).HasColumnName("loan_id").IsRequired();
        builder.Property(r => r.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(r => r.PaidAt).HasColumnName("paid_at").IsRequired();
        builder.Property(r => r.TransactionId).HasColumnName("transaction_id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne<Loan>().WithMany(l => l.Repayments).HasForeignKey(r => r.LoanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.LoanId).HasDatabaseName("ix_loan_repayments_loan_id");
    }
}
