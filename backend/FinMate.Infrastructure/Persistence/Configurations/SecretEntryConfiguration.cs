using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class SecretEntryConfiguration : IEntityTypeConfiguration<SecretEntry>
{
    public void Configure(EntityTypeBuilder<SecretEntry> builder)
    {
        builder.ToTable("secret_store", t =>
        {
            t.HasCheckConstraint("chk_secret_store_nonce", "octet_length(nonce) = 12");
        });

        builder.HasKey(s => s.Name);
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(48);
        builder.Property(s => s.Ciphertext).HasColumnName("ciphertext").IsRequired();
        builder.Property(s => s.Nonce).HasColumnName("nonce").IsRequired();
        builder.Property(s => s.KeyVersion).HasColumnName("key_version").HasDefaultValue((short)1).IsRequired();
        builder.Property(s => s.Last4).HasColumnName("last4").HasMaxLength(4).IsRequired();
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
