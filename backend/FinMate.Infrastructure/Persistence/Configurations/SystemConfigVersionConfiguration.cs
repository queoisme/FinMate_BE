using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class SystemConfigVersionConfiguration : IEntityTypeConfiguration<SystemConfigVersion>
{
    public void Configure(EntityTypeBuilder<SystemConfigVersion> builder)
    {
        builder.ToTable("system_config_versions", t =>
        {
            t.HasCheckConstraint("chk_system_config_versions_status", "status IN ('active','archived')");
            t.HasCheckConstraint("chk_system_config_versions_content_object", "jsonb_typeof(content) = 'object'");
        });

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(v => v.Name).HasColumnName("name").HasMaxLength(48).IsRequired();
        builder.Property(v => v.Version).HasColumnName("version").IsRequired();

        builder.Property(v => v.Status)
            .HasColumnName("status")
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<SystemConfigStatus>(v, true))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(v => v.Content).HasColumnName("content").HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.Note).HasColumnName("note");
        builder.Property(v => v.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(v => new { v.Name, v.Version }).IsUnique().HasDatabaseName("uq_system_config_versions_name_version");

        // Mỗi tài liệu có đúng MỘT phiên bản đang chạy: hai người lưu cùng lúc thì dòng thứ hai bị chặn ở DB chứ không chỉ ở code.
        builder.HasIndex(v => v.Name).IsUnique().HasFilter("status = 'active'").HasDatabaseName("uq_system_config_versions_active");
    }
}
