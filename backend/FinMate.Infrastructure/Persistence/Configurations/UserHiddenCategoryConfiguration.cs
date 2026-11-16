using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class UserHiddenCategoryConfiguration : IEntityTypeConfiguration<UserHiddenCategory>
{
    public void Configure(EntityTypeBuilder<UserHiddenCategory> builder)
    {
        builder.ToTable("user_hidden_categories");
        builder.HasKey(h => new { h.UserId, h.CategoryId });
        builder.Property(h => h.UserId).HasColumnName("user_id");
        builder.Property(h => h.CategoryId).HasColumnName("category_id");
        builder.Property(h => h.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>().WithMany().HasForeignKey(h => h.CategoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
