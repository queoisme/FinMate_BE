using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class VietQrBankConfiguration : IEntityTypeConfiguration<VietQrBank>
{
    public void Configure(EntityTypeBuilder<VietQrBank> builder)
    {
        builder.ToTable("vietqr_banks", t =>
        {
            t.HasCheckConstraint("chk_vietqr_banks_bin", "bin ~ '^[0-9]{6}$'");
        });

        builder.HasKey(b => b.Bin);
        builder.Property(b => b.Bin).HasColumnName("bin").HasMaxLength(6);
        builder.Property(b => b.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
        builder.Property(b => b.DeeplinkAppCode).HasColumnName("deeplink_app_code").HasMaxLength(32).IsRequired();
        builder.Property(b => b.ProviderKey).HasColumnName("provider_key").HasMaxLength(64);
        builder.Property(b => b.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Mã app là thứ đưa vào liên kết dl.vietqr.io: hai ngân hàng cùng mã sẽ mở nhầm app.
        builder.HasIndex(b => b.DeeplinkAppCode).IsUnique().HasDatabaseName("uq_vietqr_banks_app_code");
        builder.HasIndex(b => b.ProviderKey).HasDatabaseName("ix_vietqr_banks_provider_key");
    }
}
