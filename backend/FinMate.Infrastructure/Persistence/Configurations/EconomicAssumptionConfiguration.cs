using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class EconomicAssumptionConfiguration : IEntityTypeConfiguration<EconomicAssumption>
{
    public void Configure(EntityTypeBuilder<EconomicAssumption> builder)
    {
        builder.ToTable("economic_assumptions", t =>
        {
            t.HasCheckConstraint("chk_economic_assumptions_status", "status IN ('draft','approved','retired')");
            t.HasCheckConstraint("chk_economic_assumptions_version", "version >= 1");
            t.HasCheckConstraint("chk_economic_assumptions_rate", "rate_bps BETWEEN -10000 AND 100000");

            // Chỉ bản nháp mới được thiếu nguồn: duyệt hay đã duyệt rồi thì nguồn bắt buộc, kể cả khi code có lỗi.
            t.HasCheckConstraint(
                "chk_economic_assumptions_source",
                "status = 'draft' OR (length(btrim(source_name)) > 0 AND length(btrim(source_url)) > 0)");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.Key).HasColumnName("key").HasMaxLength(48).IsRequired();
        builder.Property(a => a.RateBps).HasColumnName("rate_bps").IsRequired();
        builder.Property(a => a.Unit).HasColumnName("unit").HasMaxLength(16).IsRequired();
        builder.Property(a => a.SourceName).HasColumnName("source_name").IsRequired();
        builder.Property(a => a.SourceUrl).HasColumnName("source_url").IsRequired();
        builder.Property(a => a.AsOf).HasColumnName("as_of").IsRequired();
        builder.Property(a => a.Version).HasColumnName("version").IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<AssumptionStatus>(v, true))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(a => a.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(a => a.ApprovedBy).HasColumnName("approved_by");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(a => new { a.Key, a.Version }).IsUnique().HasDatabaseName("uq_economic_assumptions_key_version");
    }
}
