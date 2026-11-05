using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinMate.Infrastructure.Persistence.Configurations;

/// <summary>Enum của nhóm lưu dạng TEXT snake_case. CHECK sinh từ chính enum, nên thêm giá trị mới thì migration kế tiếp tự thấy.</summary>
internal static class GroupColumns
{
    public static string SnakeCase(string name) => System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();

    public static string CheckIn<T>(string column)
        where T : struct, Enum
        => $"{column} IN ({string.Join(",", Enum.GetNames<T>().Select(n => $"'{SnakeCase(n)}'"))})";

    public static ValueConverter<T, string> Converter<T>()
        where T : struct, Enum
        => new(v => SnakeCase(v.ToString()), v => Enum.Parse<T>(v.Replace("_", string.Empty), true));
}

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups", t =>
        {
            t.HasCheckConstraint("chk_groups_type", GroupColumns.CheckIn<GroupType>("type"));
            t.HasCheckConstraint("chk_groups_transaction_visibility", GroupColumns.CheckIn<GroupTransactionVisibility>("transaction_visibility"));
        });

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(g => g.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(g => g.Type).HasColumnName("type").HasConversion(GroupColumns.Converter<GroupType>()).HasMaxLength(20).IsRequired();
        builder.Property(g => g.TransactionVisibility).HasColumnName("transaction_visibility").HasConversion(GroupColumns.Converter<GroupTransactionVisibility>()).HasMaxLength(20).IsRequired();
        builder.Property(g => g.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(g => g.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(g => g.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(g => g.DeletedAt == null);

        builder.HasOne<User>().WithMany().HasForeignKey(g => g.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("group_members", t => t.HasCheckConstraint("chk_group_members_role", GroupColumns.CheckIn<GroupRole>("role")));

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(m => m.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(m => m.Role).HasColumnName("role").HasConversion(GroupColumns.Converter<GroupRole>()).HasMaxLength(20).IsRequired();
        builder.Property(m => m.PermissionsGranted).HasColumnName("permissions_granted").HasDefaultValue(GroupPermission.None).IsRequired();
        builder.Property(m => m.PermissionsRevoked).HasColumnName("permissions_revoked").HasDefaultValue(GroupPermission.None).IsRequired();
        builder.Property(m => m.AlertsEnabled).HasColumnName("alerts_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(m => m.JoinedAt).HasColumnName("joined_at").IsRequired();
        builder.Property(m => m.LeftAt).HasColumnName("left_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(m => new { m.GroupId, m.UserId }).IsUnique().HasDatabaseName("uq_group_members_group_user");
        builder.HasIndex(m => m.GroupId)
            .IsUnique()
            .HasFilter("role = 'owner' AND left_at IS NULL")
            .HasDatabaseName("uq_group_members_one_owner");
        builder.HasIndex(m => m.UserId).HasFilter("left_at IS NULL").HasDatabaseName("ix_group_members_user_active");

        builder.HasOne(m => m.Group).WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Restrict);

        // IsRequired(false): User có query filter xóa mềm, còn người đã yêu cầu xóa vẫn là thành viên tới lúc xóa cứng.
        builder.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GroupInvitationConfiguration : IEntityTypeConfiguration<GroupInvitation>
{
    public void Configure(EntityTypeBuilder<GroupInvitation> builder)
    {
        builder.ToTable("group_invitations", t => t.HasCheckConstraint("chk_group_invitations_role", "role IN ('manager','member','viewer')"));

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(i => i.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(i => i.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(i => i.Email).HasColumnName("email").HasMaxLength(320);
        builder.Property(i => i.Role).HasColumnName("role").HasConversion(GroupColumns.Converter<GroupRole>()).HasMaxLength(20).IsRequired();
        builder.Property(i => i.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(i => i.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(i => i.AcceptedByUserId).HasColumnName("accepted_by_user_id");
        builder.Property(i => i.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(i => i.RevokedAt).HasColumnName("revoked_at");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();

        // Khóa lạc quan: hai người nhận cùng một mã thì đúng một người thắng.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(i => i.TokenHash).IsUnique().HasDatabaseName("uq_group_invitations_token_hash");
        builder.HasIndex(i => new { i.GroupId, i.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_group_invitations_group_created");

        builder.HasOne<Group>().WithMany().HasForeignKey(i => i.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.AcceptedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class GroupActivityConfiguration : IEntityTypeConfiguration<GroupActivity>
{
    public void Configure(EntityTypeBuilder<GroupActivity> builder)
    {
        builder.ToTable("group_activities", t => t.HasCheckConstraint("chk_group_activities_type", GroupColumns.CheckIn<GroupActivityType>("type")));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(a => a.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(a => a.Type).HasColumnName("type").HasConversion(GroupColumns.Converter<GroupActivityType>()).HasMaxLength(40).IsRequired();
        builder.Property(a => a.SubjectId).HasColumnName("subject_id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(a => new { a.GroupId, a.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_group_activities_group_created");

        builder.HasOne<Group>().WithMany().HasForeignKey(a => a.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.ActorUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class GroupCategoryConfiguration : IEntityTypeConfiguration<GroupCategory>
{
    public void Configure(EntityTypeBuilder<GroupCategory> builder)
    {
        builder.ToTable("group_categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(c => c.Slug).HasColumnName("slug").HasMaxLength(120).IsRequired();
        builder.Property(c => c.IconName).HasColumnName("icon_name").HasMaxLength(50);
        builder.Property(c => c.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(c => c.DeletedAt == null);

        builder.HasIndex(c => new { c.GroupId, c.Slug }).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("uq_group_categories_group_slug");

        builder.HasOne<Group>().WithMany().HasForeignKey(c => c.GroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class GroupShareRuleConfiguration : IEntityTypeConfiguration<GroupShareRule>
{
    public void Configure(EntityTypeBuilder<GroupShareRule> builder)
    {
        builder.ToTable("group_share_rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(r => r.FinancialAccountId).HasColumnName("financial_account_id");
        builder.Property(r => r.CategoryId).HasColumnName("category_id");
        builder.Property(r => r.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(r => new { r.UserId, r.IsActive }).HasDatabaseName("ix_group_share_rules_user_active");

        builder.HasOne<Group>().WithMany().HasForeignKey(r => r.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FinancialAccount>().WithMany().HasForeignKey(r => r.FinancialAccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>().WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
