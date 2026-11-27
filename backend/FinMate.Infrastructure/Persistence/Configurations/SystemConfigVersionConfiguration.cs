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
            t.HasCheckConstraint("chk_system_config_versions_status", "status IN ('active','archived','draft','pending_approval','rejected','cancelled')");
            // Phase 95: chỉ phiên bản trong lịch sử có số; bản nháp phải có phạm vi; bản nháp đã duyệt phải ghi người duyệt; không ai tự duyệt.
            t.HasCheckConstraint("chk_system_config_versions_numbered", "(status IN ('active','archived')) = (version IS NOT NULL)");
            t.HasCheckConstraint("chk_system_config_versions_draft_scope", "status IN ('active','archived') OR scope IS NOT NULL");
            t.HasCheckConstraint("chk_system_config_versions_approved_by", "scope IS NULL OR status NOT IN ('active','archived') OR decided_by IS NOT NULL");
            t.HasCheckConstraint("chk_system_config_versions_two_people", "decided_by IS NULL OR decided_by <> created_by");
            t.HasCheckConstraint("chk_system_config_versions_content_object", "jsonb_typeof(content) = 'object'");
        });

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(v => v.Name).HasColumnName("name").HasMaxLength(48).IsRequired();
        builder.Property(v => v.Version).HasColumnName("version");

        builder.Property(v => v.Status)
            .HasColumnName("status")
            .HasConversion(
                v => GroupColumns.SnakeCase(v.ToString()),
                v => Enum.Parse<SystemConfigStatus>(v.Replace("_", string.Empty), true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.Content).HasColumnName("content").HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.Note).HasColumnName("note");
        builder.Property(v => v.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(v => v.Scope).HasColumnName("scope").HasMaxLength(24);
        builder.Property(v => v.BaseVersion).HasColumnName("base_version");
        builder.Property(v => v.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(v => v.DecidedBy).HasColumnName("decided_by");
        builder.Property(v => v.DecidedAt).HasColumnName("decided_at");
        builder.Property(v => v.DecisionNote).HasColumnName("decision_note").HasMaxLength(1000);
        builder.Property(v => v.Conflicts).HasColumnName("conflicts").HasColumnType("jsonb");
        builder.Property(v => v.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(v => new { v.Name, v.Version }).IsUnique().HasDatabaseName("uq_system_config_versions_name_version");

        // Mỗi tài liệu có đúng MỘT phiên bản đang chạy: hai người lưu cùng lúc thì dòng thứ hai bị chặn ở DB chứ không chỉ ở code.
        builder.HasIndex(v => v.Name).IsUnique().HasFilter("status = 'active'").HasDatabaseName("uq_system_config_versions_active");

        // Phase 95 (D8): mỗi phạm vi chỉ một bản đang chờ duyệt.
        builder.HasIndex(v => new { v.Name, v.Scope }).IsUnique().HasFilter("status = 'pending_approval'").HasDatabaseName("uq_system_config_versions_pending");
        builder.HasIndex(v => new { v.Name, v.Scope, v.Status, v.CreatedAt }).HasDatabaseName("ix_system_config_versions_drafts");
    }
}
