using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();   // = giá trị enum UserRole
        builder.Property(r => r.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(r => r.IsSystem).HasColumnName("is_system").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(r => r.Code).IsUnique();
    }
}

public class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(a => a.UserId);   // khóa chính là user_id: mỗi người đúng một vai trò
        builder.Property(a => a.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(a => a.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(a => a.AssignedAt).HasColumnName("assigned_at").IsRequired();
        builder.Property(a => a.AssignedByUserId).HasColumnName("assigned_by_user_id");

        // Quan hệ tới users (cascade) được cấu hình ở UserConfiguration.
        builder.HasOne<Role>().WithMany().HasForeignKey(a => a.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.AssignedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(a => a.RoleId);
    }
}
