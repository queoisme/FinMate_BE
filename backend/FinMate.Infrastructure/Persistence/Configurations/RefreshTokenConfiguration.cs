using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(r => r.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.RevokedAt).HasColumnName("revoked_at");
        builder.Property(r => r.ReplacedByTokenHash).HasColumnName("replaced_by_token_hash").HasMaxLength(64);

        builder.HasIndex(r => r.TokenHash)
            .IsUnique()
            .HasDatabaseName("uq_refresh_tokens_token_hash");

        builder.HasIndex(r => r.UserId)
            .HasDatabaseName("idx_refresh_tokens_user_id");

        builder.Ignore(r => r.IsActive);
    }
}
