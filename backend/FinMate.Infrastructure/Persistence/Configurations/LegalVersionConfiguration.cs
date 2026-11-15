using System.Text.Json;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class LegalVersionConfiguration : IEntityTypeConfiguration<LegalVersion>
{
    private static readonly ValueConverter<List<LegalSection>, string> SectionsConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<List<LegalSection>>(v, (JsonSerializerOptions?)null) ?? new List<LegalSection>());

    // LegalSection là record nên SequenceEqual so theo giá trị: sửa tại chỗ hay thay cả danh sách đều được nhận ra.
    private static readonly ValueComparer<List<LegalSection>> SectionsComparer = new(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s)),
        v => v.ToList());

    public void Configure(EntityTypeBuilder<LegalVersion> builder)
    {
        builder.ToTable("legal_versions", t =>
            t.HasCheckConstraint("chk_legal_versions_version_iff_published", "(status = 'published') = (version IS NOT NULL)"));

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(v => v.Version).HasColumnName("version");
        builder.Property(v => v.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<LegalVersionStatus>()).HasMaxLength(20).IsRequired();
        // Converter sang chuỗi thay vì bật JSON động toàn cục của Npgsql: chỉ hai cột này cần, và repo cho các cột jsonb khác dùng JsonElement.
        builder.Property(v => v.TermsSections).HasColumnName("terms_sections").HasColumnType("jsonb").HasConversion(SectionsConverter, SectionsComparer).IsRequired();
        builder.Property(v => v.PrivacySections).HasColumnName("privacy_sections").HasColumnType("jsonb").HasConversion(SectionsConverter, SectionsComparer).IsRequired();
        builder.Property(v => v.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(v => v.UpdatedAt).HasColumnName("updated_at").IsRequired().IsConcurrencyToken();
        builder.Property(v => v.PublishedBy).HasColumnName("published_by");
        builder.Property(v => v.PublishedAt).HasColumnName("published_at");

        builder.HasIndex(v => v.Version).IsUnique().HasDatabaseName("uq_legal_versions_version");

        // Tối đa một dòng nháp: người thứ hai tạo nháp mới sẽ đụng chỉ mục này thay vì sinh ra hai nháp song song.
        builder.HasIndex(v => v.Status).IsUnique().HasFilter("status = 'draft'").HasDatabaseName("uq_legal_versions_single_draft");
    }
}
