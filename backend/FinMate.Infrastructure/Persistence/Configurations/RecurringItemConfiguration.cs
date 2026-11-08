using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class RecurringItemConfiguration : IEntityTypeConfiguration<RecurringItem>
{
    public void Configure(EntityTypeBuilder<RecurringItem> builder)
    {
        builder.ToTable("recurring_items", t =>
        {
            t.HasCheckConstraint("chk_recurring_items_direction", "direction IN ('inflow','outflow')");
            t.HasCheckConstraint("chk_recurring_items_cadence", "cadence IN ('weekly','monthly','yearly')");
            t.HasCheckConstraint("chk_recurring_items_status", "status IN ('suggested','active','paused','rejected','cancelled')");
            t.HasCheckConstraint("chk_recurring_items_source", "source IN ('detected','manual')");
            t.HasCheckConstraint("chk_recurring_items_amount", "amount_cents > 0");
            t.HasCheckConstraint("chk_recurring_items_occurrences", "occurrences >= 0");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(r => r.Direction).HasColumnName("direction").HasConversion(Lower<RecurringDirection>()).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(r => r.CounterpartyKey).HasColumnName("counterparty_key").HasMaxLength(120);
        builder.Property(r => r.CategoryId).HasColumnName("category_id");
        builder.Property(r => r.FinancialAccountId).HasColumnName("financial_account_id");
        builder.Property(r => r.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(r => r.Cadence).HasColumnName("cadence").HasConversion(Lower<RecurringCadence>()).HasMaxLength(10).IsRequired();
        builder.Property(r => r.NextDueDate).HasColumnName("next_due_date").IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion(Lower<RecurringStatus>()).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Source).HasColumnName("source").HasConversion(Lower<RecurringSource>()).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Occurrences).HasColumnName("occurrences").HasDefaultValue(0);
        builder.Property(r => r.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(r => r.LastSeenAmountCents).HasColumnName("last_seen_amount_cents");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(r => r.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.Category).WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(r => r.FinancialAccount).WithMany().HasForeignKey(r => r.FinancialAccountId).OnDelete(DeleteBehavior.SetNull);

        // Một người nhận chỉ có một khoản định kỳ mỗi chiều. Dòng bị từ chối vẫn giữ chỗ (không xóa) để bộ phát hiện không đề xuất lại.
        builder.HasIndex(r => new { r.UserId, r.Direction, r.CounterpartyKey })
            .IsUnique()
            .HasFilter("counterparty_key IS NOT NULL AND deleted_at IS NULL")
            .HasDatabaseName("uq_recurring_items_user_direction_counterparty");

        builder.HasIndex(r => new { r.UserId, r.Status }).HasDatabaseName("ix_recurring_items_user_status");

        builder.HasQueryFilter(r => r.DeletedAt == null);
    }

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> Lower<T>()
        where T : struct, Enum
        => new(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<T>(v, true));
}
