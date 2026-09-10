using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class FinancialAccountConfiguration : IEntityTypeConfiguration<FinancialAccount>
{
    public void Configure(EntityTypeBuilder<FinancialAccount> builder)
    {
        builder.ToTable("financial_accounts", t => t.HasCheckConstraint(
            "chk_financial_accounts_account_type", "account_type IN ('bank','ewallet','cash')"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(a => a.ProviderConfigId).HasColumnName("provider_config_id");

        builder.Property(a => a.AccountType)
            .HasColumnName("account_type")
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<AccountType>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.AccountName)
            .HasColumnName("account_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.PackageName)
            .HasColumnName("package_name")
            .HasMaxLength(200);

        builder.Property(a => a.IsMonitored)
            .HasColumnName("is_monitored")
            .HasDefaultValue(true);

        builder.Property(a => a.BalanceCents)
            .HasColumnName("balance_cents")
            .HasDefaultValue(0L);

        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(a => a.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(a => a.DeletedAt == null);

        builder.HasIndex(a => a.UserId)
            .HasDatabaseName("idx_financial_accounts_user_id");

        builder.HasIndex(a => new { a.UserId, a.PackageName })
            .IsUnique()
            .HasDatabaseName("uq_financial_accounts_user_package")
            .HasFilter("package_name IS NOT NULL AND deleted_at IS NULL");

        builder.HasOne(a => a.User)
            .WithMany(u => u.FinancialAccounts)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.ProviderConfig)
            .WithMany()
            .HasForeignKey(a => a.ProviderConfigId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
