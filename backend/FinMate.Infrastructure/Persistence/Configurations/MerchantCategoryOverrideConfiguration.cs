using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class MerchantCategoryOverrideConfiguration : IEntityTypeConfiguration<MerchantCategoryOverride>
{
    public void Configure(EntityTypeBuilder<MerchantCategoryOverride> builder)
    {
        builder.ToTable("merchant_category_overrides");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(o => o.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(o => o.MerchantKey).HasColumnName("merchant_key").HasMaxLength(120).IsRequired();
        builder.Property(o => o.DisplayName).HasColumnName("display_name").HasMaxLength(120);
        builder.Property(o => o.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasOne(o => o.User).WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);

        // Xóa danh mục thì mapping trỏ tới nó mất theo: một mapping không có danh mục thì vô nghĩa.
        builder.HasOne(o => o.Category).WithMany().HasForeignKey(o => o.CategoryId).OnDelete(DeleteBehavior.Cascade);

        // Một merchant chỉ có đúng MỘT danh mục tại một thời điểm, theo từng người dùng.
        builder.HasIndex(o => new { o.UserId, o.MerchantKey }).IsUnique().HasDatabaseName("uq_merchant_category_overrides_user_merchant");
    }
}
