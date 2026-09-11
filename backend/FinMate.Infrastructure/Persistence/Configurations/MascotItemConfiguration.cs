using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class MascotItemConfiguration : IEntityTypeConfiguration<MascotItem>
{
    public void Configure(EntityTypeBuilder<MascotItem> builder)
    {
        builder.ToTable("mascot_items", t =>
        {
            t.HasCheckConstraint("chk_mascot_items_item_type", "item_type IN ('hat','outfit','accessory','background')");
            t.HasCheckConstraint("chk_mascot_items_unlock_type", "unlock_type IN ('default','level','mission')");

            // Cách mở khóa phải đi kèm đúng dữ liệu của nó, nếu không item sẽ vĩnh viễn
            // không mở được mà chẳng ai nhận ra.
            t.HasCheckConstraint(
                "chk_mascot_items_unlock_payload",
                "(unlock_type = 'default' AND unlock_level IS NULL AND unlock_mission_code IS NULL) "
                + "OR (unlock_type = 'level' AND unlock_level IS NOT NULL) "
                + "OR (unlock_type = 'mission' AND unlock_mission_code IS NOT NULL)");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(i => i.Name).HasColumnName("name").HasMaxLength(100).IsRequired();

        builder.Property(i => i.ItemType)
            .HasColumnName("item_type")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<MascotItemType>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.UnlockType)
            .HasColumnName("unlock_type")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<MascotUnlockType>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.UnlockLevel).HasColumnName("unlock_level");
        builder.Property(i => i.UnlockMissionCode).HasColumnName("unlock_mission_code").HasMaxLength(50);
        builder.Property(i => i.IsPremium).HasColumnName("is_premium").HasDefaultValue(false);
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(i => i.Code).IsUnique().HasDatabaseName("uq_mascot_items_code");
    }
}
