using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class TransactionMergeSnapshotConfiguration : IEntityTypeConfiguration<TransactionMergeSnapshot>
{
    public void Configure(EntityTypeBuilder<TransactionMergeSnapshot> builder)
    {
        builder.ToTable("transaction_merge_snapshots", t =>
        {
            t.HasCheckConstraint("chk_transaction_merge_snapshots_kind", "kind IN ('duplicate_merge','transfer_pair','transfer_convert','group_fund_expense')");
            t.HasCheckConstraint(
                "chk_transaction_merge_snapshots_transfer_kind",
                "transfer_kind IS NULL OR transfer_kind IN ('atm_withdrawal','atm_deposit','wallet_topup','wallet_withdrawal','own_account')");
            t.HasCheckConstraint("chk_transaction_merge_snapshots_before", "jsonb_typeof(before) = 'object'");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.SurvivorId).HasColumnName("survivor_id").IsRequired();
        builder.Property(s => s.MergedId).HasColumnName("merged_id");
        builder.Property(s => s.Kind)
            .HasColumnName("kind")
            .HasConversion(v => MergeSnapshotKindColumn.ToColumn(v), v => MergeSnapshotKindColumn.FromColumn(v))
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(s => s.TransferKind)
            .HasColumnName("transfer_kind")
            .HasConversion(
                v => v == null ? null : TransferKindColumn.ToColumn(v.Value),
                v => v == null ? null : TransferKindColumn.FromColumn(v))
            .HasMaxLength(24);
        builder.Property(s => s.Before).HasColumnName("before").HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        // Xoá cứng tài khoản xoá dòng users; ảnh chụp đi theo trong cùng câu lệnh. Không có FK tới transactions: đây là lịch sử.
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => new { s.UserId, s.SurvivorId }).HasDatabaseName("ix_transaction_merge_snapshots_user_survivor");
        builder.HasIndex(s => s.CreatedAt).HasDatabaseName("ix_transaction_merge_snapshots_created_at");
    }
}

internal static class MergeSnapshotKindColumn
{
    private static readonly Dictionary<MergeSnapshotKind, string> ToText = new()
    {
        [MergeSnapshotKind.DuplicateMerge] = "duplicate_merge",
        [MergeSnapshotKind.TransferPair] = "transfer_pair",
        [MergeSnapshotKind.TransferConvert] = "transfer_convert",
        [MergeSnapshotKind.GroupFundExpense] = "group_fund_expense",
    };

    private static readonly Dictionary<string, MergeSnapshotKind> FromText = ToText.ToDictionary(p => p.Value, p => p.Key);

    public static string ToColumn(MergeSnapshotKind value) => ToText[value];

    public static MergeSnapshotKind FromColumn(string value) => FromText[value];
}
