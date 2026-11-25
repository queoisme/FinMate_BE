using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class GroupBillConfiguration : IEntityTypeConfiguration<GroupBill>
{
    public void Configure(EntityTypeBuilder<GroupBill> builder)
    {
        builder.ToTable("group_bills", t =>
        {
            t.HasCheckConstraint("chk_group_bills_amount", "amount_cents > 0");
            t.HasCheckConstraint("chk_group_bills_split_mode", GroupColumns.CheckIn<GroupBillSplitMode>("split_mode"));
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(b => b.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(b => b.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(b => b.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(b => b.PaidByUserId).HasColumnName("paid_by_user_id");
        builder.Property(b => b.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(b => b.SplitMode).HasColumnName("split_mode").HasConversion(GroupColumns.Converter<GroupBillSplitMode>()).HasMaxLength(20).IsRequired();
        builder.Property(b => b.CategoryId).HasColumnName("category_id");
        builder.Property(b => b.GroupCategoryId).HasColumnName("group_category_id");
        builder.Property(b => b.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(b => b.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(b => new { b.GroupId, b.OccurredAt }).IsDescending(false, true).HasDatabaseName("ix_group_bills_group_occurred");

        builder.HasOne<Group>().WithMany().HasForeignKey(b => b.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.PaidByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(b => b.Category).WithMany().HasForeignKey(b => b.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.GroupCategory).WithMany().HasForeignKey(b => b.GroupCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Splits).WithOne().HasForeignKey(s => s.BillId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GroupBillSplitConfiguration : IEntityTypeConfiguration<GroupBillSplit>
{
    public void Configure(EntityTypeBuilder<GroupBillSplit> builder)
    {
        builder.ToTable("group_bill_splits", t => t.HasCheckConstraint("chk_group_bill_splits_share", "share_cents > 0"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.BillId).HasColumnName("bill_id").IsRequired();
        builder.Property(s => s.UserId).HasColumnName("user_id");
        builder.Property(s => s.ShareCents).HasColumnName("share_cents").IsRequired();

        builder.HasIndex(s => s.BillId).HasDatabaseName("ix_group_bill_splits_bill");
        builder.HasIndex(s => s.UserId).HasDatabaseName("ix_group_bill_splits_user");

        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class GroupSettlementConfiguration : IEntityTypeConfiguration<GroupSettlement>
{
    public void Configure(EntityTypeBuilder<GroupSettlement> builder)
    {
        builder.ToTable("group_settlements", t => t.HasCheckConstraint("chk_group_settlements_amount", "amount_cents > 0"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(s => s.FromUserId).HasColumnName("from_user_id");
        builder.Property(s => s.ToUserId).HasColumnName("to_user_id");
        builder.Property(s => s.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(s => s.Note).HasColumnName("note").HasMaxLength(200);
        builder.Property(s => s.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(s => new { s.GroupId, s.CreatedAt }).HasDatabaseName("ix_group_settlements_group_created");

        builder.HasOne<Group>().WithMany().HasForeignKey(s => s.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.FromUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.ToUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class GroupDebtReminderConfiguration : IEntityTypeConfiguration<GroupDebtReminder>
{
    public void Configure(EntityTypeBuilder<GroupDebtReminder> builder)
    {
        builder.ToTable("group_debt_reminders");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(r => r.DebtorUserId).HasColumnName("debtor_user_id").IsRequired();
        builder.Property(r => r.SentByUserId).HasColumnName("sent_by_user_id");
        builder.Property(r => r.SentAt).HasColumnName("sent_at").IsRequired();

        builder.HasIndex(r => new { r.GroupId, r.DebtorUserId, r.SentAt }).IsDescending(false, false, true).HasDatabaseName("ix_group_debt_reminders_debtor");

        builder.HasOne<Group>().WithMany().HasForeignKey(r => r.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.DebtorUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.SentByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
