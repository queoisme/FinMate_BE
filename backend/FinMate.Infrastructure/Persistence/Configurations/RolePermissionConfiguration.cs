using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(r => r.Role);

        // Khóa ngoại tới roles(id); id vai trò chính là giá trị enum UserRole.
        builder.Property(r => r.Role).HasColumnName("role_id").ValueGeneratedNever();
        builder.HasOne<Role>().WithMany().HasForeignKey(r => r.Role).OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.Capabilities).HasColumnName("capabilities").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(r => r.UpdatedByUserId).HasColumnName("updated_by_user_id");
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
