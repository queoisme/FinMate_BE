using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class GroupFundConfiguration : IEntityTypeConfiguration<GroupFund>
{
    public void Configure(EntityTypeBuilder<GroupFund> builder)
    {
        builder.ToTable("group_funds", t =>
        {
            t.HasCheckConstraint("chk_group_funds_name", "char_length(name) BETWEEN 1 AND 100");

            // BR-FUND-03 ở mức DB: không có quỹ lưu trữ nào còn tiền.
            t.HasCheckConstraint("chk_group_funds_archived_settled", "archived_at IS NULL OR balance_cents = 0");
            t.HasCheckConstraint("chk_group_funds_negative_episode", "negative_episode >= 0");
        });

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(f => f.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(f => f.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(f => f.BalanceCents).HasColumnName("balance_cents").HasDefaultValue(0L).IsRequired();
        builder.Property(f => f.NegativeEpisode).HasColumnName("negative_episode").HasDefaultValue(0).IsRequired();
        builder.Property(f => f.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(f => f.ArchivedAt).HasColumnName("archived_at");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Ignore(f => f.IsArchived);

        // Khóa lạc quan theo cột hệ thống xmin của PostgreSQL: số dư quỹ được đọc rồi cộng trong tiến trình, thiếu khóa thì hai người ghi cùng lúc làm mất một dòng.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(f => f.GroupId).HasFilter("archived_at IS NULL").HasDatabaseName("ix_group_funds_group_active");

        builder.HasOne<Group>().WithMany().HasForeignKey(f => f.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class GroupFundEntryConfiguration : IEntityTypeConfiguration<GroupFundEntry>
{
    public void Configure(EntityTypeBuilder<GroupFundEntry> builder)
    {
        builder.ToTable("group_fund_entries", t =>
        {
            t.HasCheckConstraint("chk_group_fund_entries_kind", GroupColumns.CheckIn<GroupFundEntryKind>("kind"));
            t.HasCheckConstraint("chk_group_fund_entries_origin", GroupColumns.CheckIn<GroupFundEntryOrigin>("origin"));

            // D3: điều chỉnh mang dấu và bắt buộc ghi chú; mọi loại khác dương.
            t.HasCheckConstraint(
                "chk_group_fund_entries_amount",
                "(kind = 'adjustment' AND amount_cents <> 0 AND note IS NOT NULL) OR (kind <> 'adjustment' AND amount_cents > 0)");

            // D10: chỉ khoản chi có danh mục và nơi chi, và khoản chi phải có ít nhất một danh mục.
            t.HasCheckConstraint(
                "chk_group_fund_entries_expense_fields",
                "kind = 'expense' OR (category_id IS NULL AND group_category_id IS NULL AND merchant_name IS NULL)");
            t.HasCheckConstraint(
                "chk_group_fund_entries_expense_category",
                "kind <> 'expense' OR category_id IS NOT NULL OR group_category_id IS NOT NULL");

            // D24: chỉ khoản chi chuyển được từ một giao dịch; danh mục gốc chỉ có ở dòng chuyển đổi.
            t.HasCheckConstraint("chk_group_fund_entries_origin_kind", "origin = 'created' OR kind = 'expense'");
            t.HasCheckConstraint("chk_group_fund_entries_origin_category", "origin_category_id IS NULL OR origin = 'converted'");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.FundId).HasColumnName("fund_id").IsRequired();
        builder.Property(e => e.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(e => e.MemberUserId).HasColumnName("member_user_id");
        builder.Property(e => e.Kind).HasColumnName("kind").HasConversion(GroupColumns.Converter<GroupFundEntryKind>()).HasMaxLength(20).IsRequired();
        builder.Property(e => e.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(e => e.TransactedAt).HasColumnName("transacted_at").IsRequired();
        builder.Property(e => e.CategoryId).HasColumnName("category_id");
        builder.Property(e => e.GroupCategoryId).HasColumnName("group_category_id");
        builder.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(e => e.MerchantName).HasColumnName("merchant_name").HasMaxLength(200);
        builder.Property(e => e.ClientRequestId).HasColumnName("client_request_id");
        builder.Property(e => e.Origin)
            .HasColumnName("origin")
            .HasConversion(GroupColumns.Converter<GroupFundEntryOrigin>())
            .HasMaxLength(10)
            .HasDefaultValue(GroupFundEntryOrigin.Created)
            .HasSentinel((GroupFundEntryOrigin)(-1))
            .IsRequired();
        builder.Property(e => e.OriginCategoryId).HasColumnName("origin_category_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        builder.Property(e => e.DeletedByUserId).HasColumnName("deleted_by_user_id");

        builder.HasIndex(e => new { e.FundId, e.TransactedAt, e.Id })
            .IsDescending(false, true, true)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_group_fund_entries_fund_transacted");
        builder.HasIndex(e => new { e.GroupId, e.Kind, e.TransactedAt })
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_group_fund_entries_group_kind_transacted");

        // D19: gửi lại cùng mã thì trả dòng cũ. UNIQUE một phần để chặn được cả hai request gửi lại cùng lúc.
        builder.HasIndex(e => new { e.FundId, e.MemberUserId, e.ClientRequestId })
            .IsUnique()
            .HasFilter("client_request_id IS NOT NULL")
            .HasDatabaseName("uq_group_fund_entries_client_request");

        builder.HasOne(e => e.Fund).WithMany().HasForeignKey(e => e.FundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Group>().WithMany().HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.Restrict);

        // BR-FUND-14/15: xoá cứng tài khoản ẩn danh dòng quỹ, không xoá (số dư quỹ cả nhóm dựa vào nó).
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.MemberUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.DeletedByUserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.GroupCategory).WithMany().HasForeignKey(e => e.GroupCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(e => e.OriginCategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}
