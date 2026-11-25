using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class GroupApprovalConfiguration : IEntityTypeConfiguration<GroupApproval>
{
    public void Configure(EntityTypeBuilder<GroupApproval> builder)
    {
        builder.ToTable("group_approvals", t =>
        {
            t.HasCheckConstraint("chk_group_approvals_kind", GroupColumns.CheckIn<GroupApprovalKind>("kind"));
            t.HasCheckConstraint("chk_group_approvals_status", GroupColumns.CheckIn<GroupApprovalStatus>("status"));
            t.HasCheckConstraint("chk_group_approvals_decision", "(status = 'pending') = (decided_at IS NULL)");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(a => a.Kind).HasColumnName("kind").HasConversion(GroupColumns.Converter<GroupApprovalKind>()).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<GroupApprovalStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(a => a.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(a => a.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(a => a.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(a => a.PayloadJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.ClientRequestId).HasColumnName("client_request_id");
        builder.Property(a => a.ResultId).HasColumnName("result_id");
        builder.Property(a => a.DecisionReason).HasColumnName("decision_reason").HasMaxLength(200);
        builder.Property(a => a.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(a => a.DecidedAt).HasColumnName("decided_at");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        // Khóa lạc quan: hai lần duyệt cùng một yêu cầu thì đúng một lần thắng.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(a => new { a.GroupId, a.Status, a.CreatedAt }).IsDescending(false, false, true).HasDatabaseName("ix_group_approvals_group_status");
        builder.HasIndex(a => new { a.GroupId, a.RequestedByUserId, a.ClientRequestId })
            .IsUnique()
            .HasFilter("client_request_id IS NOT NULL")
            .HasDatabaseName("uq_group_approvals_client_request");

        builder.HasOne<Group>().WithMany().HasForeignKey(a => a.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.DecidedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
