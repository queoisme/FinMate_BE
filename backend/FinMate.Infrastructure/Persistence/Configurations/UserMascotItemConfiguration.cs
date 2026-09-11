using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class UserMascotItemConfiguration : IEntityTypeConfiguration<UserMascotItem>
{
    public void Configure(EntityTypeBuilder<UserMascotItem> builder)
    {
        builder.ToTable("user_mascot_items", t =>
            t.HasCheckConstraint("chk_user_mascot_items_item_type", "item_type IN ('hat','outfit','accessory','background')"));

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(i => i.MascotItemId).HasColumnName("mascot_item_id").IsRequired();

        builder.Property(i => i.ItemType)
            .HasColumnName("item_type")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<MascotItemType>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.UnlockedAt).HasColumnName("unlocked_at").IsRequired();
        builder.Property(i => i.IsEquipped).HasColumnName("is_equipped").HasDefaultValue(false);

        builder.HasIndex(i => new { i.UserId, i.MascotItemId })
            .IsUnique()
            .HasDatabaseName("uq_user_mascot_items_user_item");

        // Mỗi loại chỉ mặc được 1 món — ràng buộc ở tầng DB chứ không chỉ trong handler.
        builder.HasIndex(i => new { i.UserId, i.ItemType })
            .IsUnique()
            .HasDatabaseName("uq_user_mascot_items_equipped_per_type")
            .HasFilter("is_equipped");

        builder.HasOne(i => i.MascotItem)
            .WithMany()
            .HasForeignKey(i => i.MascotItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
