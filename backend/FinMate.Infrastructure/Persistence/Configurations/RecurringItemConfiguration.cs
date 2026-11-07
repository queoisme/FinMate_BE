using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class RecurringItemConfiguration : IEntityTypeConfiguration<RecurringItem>
{
    public void Configure(EntityTypeBuilder<RecurringItem> builder)
    {
        builder.ToTable("recurring_items", t =>
        {
            t.HasCheckConstraint("chk_recurring_items_kind", "kind IN ('obligation','inflow')");
            t.HasCheckConstraint("chk_recurring_items_amount", "amount_cents > 0");
            t.HasCheckConstraint("chk_recurring_items_day", "day_of_month BETWEEN 1 AND 31");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(r => r.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(r => r.Kind)
            .HasColumnName("kind")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<RecurringKind>(v, true))
            .HasMaxLength(12)
            .IsRequired();
        builder.Property(r => r.DayOfMonth).HasColumnName("day_of_month").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.UserId).HasDatabaseName("ix_recurring_items_user");
    }
}
