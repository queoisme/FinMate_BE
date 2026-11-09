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
        builder.Property(d => d.AiAttemptedAt).HasColumnName("ai_attempted_at");
        builder.Property(d => d.AiFailures).HasColumnName("ai_failures").HasDefaultValue(0).IsRequired();

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
        builder.Property(p => p.AlertsProcessedAt).HasColumnName("alerts_processed_at");

        builder.HasIndex(p => new { p.FuelKey, p.FirstSeenAt }).HasDatabaseName("ix_fuel_prices_key_time");
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class MarketSignalConfiguration : IEntityTypeConfiguration<MarketSignal>
{
    public void Configure(EntityTypeBuilder<MarketSignal> builder)
    {
        builder.ToTable("market_signals", t =>
        {
            t.HasCheckConstraint("chk_market_signals_direction", GroupColumns.CheckIn<MarketDirection>("direction"));
            t.HasCheckConstraint("chk_market_signals_fact_status", GroupColumns.CheckIn<MarketFactStatus>("fact_status"));
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(s => s.Topic).HasColumnName("topic").HasMaxLength(30).IsRequired();
        builder.Property(s => s.Direction).HasColumnName("direction").HasConversion(GroupColumns.Converter<MarketDirection>()).HasMaxLength(20).IsRequired();
        builder.Property(s => s.FactStatus).HasColumnName("fact_status").HasConversion(GroupColumns.Converter<MarketFactStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(s => s.ChangePercent).HasColumnName("change_percent").HasPrecision(7, 2);
        builder.Property(s => s.EffectiveDate).HasColumnName("effective_date");
        builder.Property(s => s.Evidence).HasColumnName("evidence").HasMaxLength(300).IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(s => new { s.Topic, s.Direction, s.FactStatus, s.CreatedAt }).HasDatabaseName("ix_market_signals_topic");
        builder.HasIndex(s => s.DocumentId).HasDatabaseName("ix_market_signals_document");
        builder.HasOne<MarketDocument>().WithMany().HasForeignKey(s => s.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MarketEventConfiguration : IEntityTypeConfiguration<MarketEvent>
{
    public void Configure(EntityTypeBuilder<MarketEvent> builder)
    {
        builder.ToTable("market_events", t =>
        {
            t.HasCheckConstraint("chk_market_events_direction", GroupColumns.CheckIn<MarketDirection>("direction"));
            t.HasCheckConstraint("chk_market_events_status", GroupColumns.CheckIn<MarketEventStatus>("status"));
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.Topic).HasColumnName("topic").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Direction).HasColumnName("direction").HasConversion(GroupColumns.Converter<MarketDirection>()).HasMaxLength(20).IsRequired();
        builder.Property(e => e.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
        builder.Property(e => e.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
        builder.Property(e => e.SourceCount).HasColumnName("source_count").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<MarketEventStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(e => e.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(e => e.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(e => e.ReviewNote).HasColumnName("review_note").HasMaxLength(500);
        builder.Property(e => e.AlertsProcessedAt).HasColumnName("alerts_processed_at");

        builder.HasIndex(e => new { e.Topic, e.Direction })
            .IsUnique()
            .HasFilter("status IN ('pending', 'approved')")
            .HasDatabaseName("uq_market_events_one_open");
        builder.HasIndex(e => new { e.Status, e.LastSeenAt }).HasDatabaseName("ix_market_events_status");
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ReviewedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
