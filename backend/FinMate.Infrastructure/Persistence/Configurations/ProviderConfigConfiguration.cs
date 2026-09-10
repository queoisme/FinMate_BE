using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class ProviderConfigConfiguration : IEntityTypeConfiguration<ProviderConfig>
{
    public void Configure(EntityTypeBuilder<ProviderConfig> builder)
    {
        builder.ToTable("provider_configs", t => t.HasCheckConstraint(
            "chk_provider_configs_account_type", "account_type IN ('bank','ewallet')"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.ProviderKey)
            .HasColumnName("provider_key")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.PackageName)
            .HasColumnName("package_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.AccountType)
            .HasColumnName("account_type")
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<AccountType>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(p => p.ProviderKey)
            .IsUnique()
            .HasDatabaseName("uq_provider_configs_provider_key");

        builder.HasIndex(p => p.PackageName)
            .IsUnique()
            .HasDatabaseName("uq_provider_configs_package_name");
    }
}
