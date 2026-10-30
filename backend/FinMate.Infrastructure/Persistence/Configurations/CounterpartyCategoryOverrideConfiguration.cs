using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class CounterpartyCategoryOverrideConfiguration : IEntityTypeConfiguration<CounterpartyCategoryOverride>
{
    public void Configure(EntityTypeBuilder<CounterpartyCategoryOverride> builder)
    {
        builder.ToTable("counterparty_category_overrides", t =>
        {
            t.HasCheckConstraint("chk_counterparty_category_overrides_bin", "bank_bin ~ '^[0-9]{6}$'");
        });

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(o => o.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(o => o.BankBin).HasColumnName("bank_bin").HasMaxLength(6).IsRequired();
        builder.Property(o => o.AccountNumber).HasColumnName("account_number").HasMaxLength(32).IsRequired();
        builder.Property(o => o.DisplayName).HasColumnName("display_name").HasMaxLength(100);
        builder.Property(o => o.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasOne(o => o.User).WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);

        // Xóa danh mục thì mapping trỏ tới nó mất theo: một mapping không có danh mục thì vô nghĩa.
        builder.HasOne(o => o.Category).WithMany().HasForeignKey(o => o.CategoryId).OnDelete(DeleteBehavior.Cascade);

        // Một người nhận chỉ có đúng MỘT danh mục tại một thời điểm, theo từng người dùng.
        builder.HasIndex(o => new { o.UserId, o.BankBin, o.AccountNumber }).IsUnique().HasDatabaseName("uq_counterparty_category_overrides_user_account");
    }
}
