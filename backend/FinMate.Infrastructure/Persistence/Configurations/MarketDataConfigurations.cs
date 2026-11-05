using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class MarketSourceConfiguration : IEntityTypeConfiguration<MarketSource>
{
    public void Configure(EntityTypeBuilder<MarketSource> builder)
    {
        builder.ToTable("market_sources", t =>
            t.HasCheckConstraint("chk_market_sources_kind", GroupColumns.CheckIn<MarketSourceKind>("kind")));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(s => s.Url).HasColumnName("url").HasMaxLength(2000).IsRequired();
        builder.Property(s => s.Kind).HasColumnName("kind").HasConversion(GroupColumns.Converter<MarketSourceKind>()).HasMaxLength(20).IsRequired();
        builder.Property(s => s.AiAllowed).HasColumnName("ai_allowed").IsRequired();
        builder.Property(s => s.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(s => s.LastCheckedAt).HasColumnName("last_checked_at");
        builder.Property(s => s.LastSuccessAt).HasColumnName("last_success_at");
        builder.Property(s => s.ConsecutiveFailures).HasColumnName("consecutive_failures").IsRequired();
        builder.Property(s => s.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(50);
        builder.Property(s => s.LastContentHash).HasColumnName("last_content_hash").HasMaxLength(64);
        builder.Property(s => s.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(s => s.Url).IsUnique().HasDatabaseName("uq_market_sources_url");
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class MarketDocumentConfiguration : IEntityTypeConfiguration<MarketDocument>
{
    public void Configure(EntityTypeBuilder<MarketDocument> builder)
    {
        builder.ToTable("market_documents", t =>
            t.HasCheckConstraint("chk_market_documents_status", GroupColumns.CheckIn<MarketDocumentStatus>("status")));

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(d => d.Url).HasColumnName("url").HasMaxLength(2000).IsRequired();
        builder.Property(d => d.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(d => d.PublishedAt).HasColumnName("published_at");
        builder.Property(d => d.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
        builder.Property(d => d.TextLength).HasColumnName("text_length").IsRequired();
        builder.Property(d => d.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<MarketDocumentStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(d => d.ErrorCode).HasColumnName("error_code").HasMaxLength(50);
        builder.Property(d => d.FetchedAt).HasColumnName("fetched_at").IsRequired();

        builder.HasIndex(d => d.Url).IsUnique().HasDatabaseName("uq_market_documents_url");
        builder.HasIndex(d => new { d.Status, d.FetchedAt }).HasDatabaseName("ix_market_documents_status");
        builder.HasIndex(d => new { d.SourceId, d.FetchedAt }).HasDatabaseName("ix_market_documents_source");
        builder.HasOne<MarketSource>().WithMany().HasForeignKey(d => d.SourceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FuelPriceConfiguration : IEntityTypeConfiguration<FuelPrice>
{
    public void Configure(EntityTypeBuilder<FuelPrice> builder)
    {
        builder.ToTable("fuel_prices", t =>
        {
            t.HasCheckConstraint("chk_fuel_prices_origin", GroupColumns.CheckIn<FuelPriceOrigin>("origin"));
            t.HasCheckConstraint("chk_fuel_prices_positive", "region1_price_cents > 0 AND region2_price_cents > 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.FuelKey).HasColumnName("fuel_key").HasMaxLength(30).IsRequired();
        builder.Property(p => p.Region1PriceCents).HasColumnName("region1_price_cents").IsRequired();
        builder.Property(p => p.Region2PriceCents).HasColumnName("region2_price_cents").IsRequired();
        builder.Property(p => p.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
        builder.Property(p => p.Origin).HasColumnName("origin").HasConversion(GroupColumns.Converter<FuelPriceOrigin>()).HasMaxLength(20).IsRequired();
        builder.Property(p => p.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(p => p.Note).HasColumnName("note").HasMaxLength(500);

        builder.HasIndex(p => new { p.FuelKey, p.FirstSeenAt }).HasDatabaseName("ix_fuel_prices_key_time");
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
