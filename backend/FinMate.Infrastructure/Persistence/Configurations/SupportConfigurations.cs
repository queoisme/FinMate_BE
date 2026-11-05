using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("support_tickets", t =>
        {
            t.HasCheckConstraint("chk_support_tickets_category", GroupColumns.CheckIn<SupportTicketCategory>("category"));
            t.HasCheckConstraint("chk_support_tickets_priority", GroupColumns.CheckIn<SupportTicketPriority>("priority"));
            t.HasCheckConstraint("chk_support_tickets_status", GroupColumns.CheckIn<SupportTicketStatus>("status"));
            t.HasCheckConstraint("chk_support_tickets_rating", "rating IS NULL OR rating BETWEEN 1 AND 5");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.RequesterUserId).HasColumnName("requester_user_id").IsRequired();
        builder.Property(t => t.AssigneeUserId).HasColumnName("assignee_user_id");
        builder.Property(t => t.ParentTicketId).HasColumnName("parent_ticket_id");
        builder.Property(t => t.Category).HasColumnName("category").HasConversion(GroupColumns.Converter<SupportTicketCategory>()).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Priority).HasColumnName("priority").HasConversion(GroupColumns.Converter<SupportTicketPriority>()).HasMaxLength(5).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<SupportTicketStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
        builder.Property(t => t.FirstResponseDueAt).HasColumnName("first_response_due_at").IsRequired();
        builder.Property(t => t.ResolveDueAt).HasColumnName("resolve_due_at").IsRequired();
        builder.Property(t => t.FirstRespondedAt).HasColumnName("first_responded_at");
        builder.Property(t => t.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(t => t.ClosedAt).HasColumnName("closed_at");
        builder.Property(t => t.Rating).HasColumnName("rating");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Hai người nhận cùng một ca: người đến sau nhận 409 thay vì ghi đè người trước.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(t => new { t.Status, t.ResolveDueAt }).HasDatabaseName("ix_support_tickets_queue");
        builder.HasIndex(t => new { t.AssigneeUserId, t.Status }).HasDatabaseName("ix_support_tickets_assignee");
        builder.HasIndex(t => new { t.RequesterUserId, t.CreatedAt }).HasDatabaseName("ix_support_tickets_requester");
        builder.HasIndex(t => t.ParentTicketId).HasDatabaseName("ix_support_tickets_parent").HasFilter("parent_ticket_id IS NOT NULL");

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.RequesterUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.AssigneeUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SupportTicket>().WithMany().HasForeignKey(t => t.ParentTicketId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SupportTicketMessageConfiguration : IEntityTypeConfiguration<SupportTicketMessage>
{
    public void Configure(EntityTypeBuilder<SupportTicketMessage> builder)
    {
        builder.ToTable("support_ticket_messages", t =>
            t.HasCheckConstraint("chk_support_ticket_messages_kind", GroupColumns.CheckIn<SupportMessageKind>("kind")));

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(m => m.TicketId).HasColumnName("ticket_id").IsRequired();
        builder.Property(m => m.AuthorUserId).HasColumnName("author_user_id");
        builder.Property(m => m.Kind).HasColumnName("kind").HasConversion(GroupColumns.Converter<SupportMessageKind>()).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Body).HasColumnName("body").HasMaxLength(4000).IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(m => new { m.TicketId, m.CreatedAt }).HasDatabaseName("ix_support_ticket_messages_ticket");

        builder.HasOne<SupportTicket>().WithMany().HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.AuthorUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SupportTicketAttachmentConfiguration : IEntityTypeConfiguration<SupportTicketAttachment>
{
    public void Configure(EntityTypeBuilder<SupportTicketAttachment> builder)
    {
        builder.ToTable("support_ticket_attachments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.TicketId).HasColumnName("ticket_id").IsRequired();
        builder.Property(a => a.MessageId).HasColumnName("message_id").IsRequired();
        builder.Property(a => a.ContentType).HasColumnName("content_type").HasMaxLength(20).IsRequired();
        builder.Property(a => a.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(a => a.Data).HasColumnName("data").IsRequired();
        builder.Property(a => a.Nonce).HasColumnName("nonce").IsRequired();
        builder.Property(a => a.DeleteAfter).HasColumnName("delete_after");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(a => a.MessageId).HasDatabaseName("ix_support_ticket_attachments_message");
        builder.HasIndex(a => a.DeleteAfter).HasDatabaseName("ix_support_ticket_attachments_delete_after").HasFilter("delete_after IS NOT NULL");

        builder.HasOne<SupportTicket>().WithMany().HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SupportTicketMessage>().WithMany().HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SupportAccessGrantConfiguration : IEntityTypeConfiguration<SupportAccessGrant>
{
    public void Configure(EntityTypeBuilder<SupportAccessGrant> builder)
    {
        builder.ToTable("support_access_grants", t =>
        {
            t.HasCheckConstraint("chk_support_access_grants_status", GroupColumns.CheckIn<SupportAccessStatus>("status"));
            t.HasCheckConstraint("chk_support_access_grants_scopes", "scopes > 0");
        });

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(g => g.TicketId).HasColumnName("ticket_id").IsRequired();
        builder.Property(g => g.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(g => g.Scopes).HasColumnName("scopes").HasConversion<int>().IsRequired();
        builder.Property(g => g.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        builder.Property(g => g.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<SupportAccessStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(g => g.FinancialAccountId).HasColumnName("financial_account_id");
        builder.Property(g => g.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(g => g.RequestExpiresAt).HasColumnName("request_expires_at").IsRequired();
        builder.Property(g => g.DecidedAt).HasColumnName("decided_at");
        builder.Property(g => g.ExpiresAt).HasColumnName("expires_at");
        builder.Property(g => g.RevokedAt).HasColumnName("revoked_at");

        // Hai lần bấm đồng ý hoặc đồng ý cùng lúc thu hồi: chỉ một lần có hiệu lực.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(g => g.TicketId, "ix_support_access_grants_ticket");
        builder.HasIndex(g => g.TicketId, "uq_support_access_grants_one_pending").IsUnique().HasFilter("status = 'pending'");

        builder.HasOne<SupportTicket>().WithMany().HasForeignKey(g => g.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(g => g.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<FinancialAccount>().WithMany().HasForeignKey(g => g.FinancialAccountId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SupportDataAccessLogConfiguration : IEntityTypeConfiguration<SupportDataAccessLog>
{
    public void Configure(EntityTypeBuilder<SupportDataAccessLog> builder)
    {
        builder.ToTable("support_data_access_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(l => l.TicketId).HasColumnName("ticket_id").IsRequired();
        builder.Property(l => l.GrantId).HasColumnName("grant_id").IsRequired();
        builder.Property(l => l.CustomerUserId).HasColumnName("customer_user_id").IsRequired();
        builder.Property(l => l.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(l => l.Scope).HasColumnName("scope").HasConversion<int>().IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(l => new { l.CustomerUserId, l.CreatedAt }).HasDatabaseName("ix_support_data_access_logs_customer");

        builder.HasOne<SupportTicket>().WithMany().HasForeignKey(l => l.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SupportAccessGrant>().WithMany().HasForeignKey(l => l.GrantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.CustomerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.ActorUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class OpsApprovalRequestConfiguration : IEntityTypeConfiguration<OpsApprovalRequest>
{
    public void Configure(EntityTypeBuilder<OpsApprovalRequest> builder)
    {
        builder.ToTable("ops_approval_requests", t =>
        {
            t.HasCheckConstraint("chk_ops_approval_requests_type", GroupColumns.CheckIn<OpsApprovalType>("type"));
            t.HasCheckConstraint("chk_ops_approval_requests_status", GroupColumns.CheckIn<OpsApprovalStatus>("status"));
            t.HasCheckConstraint("chk_ops_approval_requests_two_people", "decided_by_user_id IS NULL OR requested_by_user_id IS NULL OR decided_by_user_id <> requested_by_user_id");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.Type).HasColumnName("type").HasConversion(GroupColumns.Converter<OpsApprovalType>()).HasMaxLength(30).IsRequired();
        builder.Property(a => a.TargetId).HasColumnName("target_id").IsRequired();
        builder.Property(a => a.NewOwnerUserId).HasColumnName("new_owner_user_id");
        builder.Property(a => a.TicketId).HasColumnName("ticket_id");
        builder.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(a => a.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(a => a.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<OpsApprovalStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(a => a.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(a => a.DecidedAt).HasColumnName("decided_at");
        builder.Property(a => a.DecisionNote).HasColumnName("decision_note").HasMaxLength(1000);
        builder.Property(a => a.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        builder.Property(a => a.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        // Hai người duyệt cùng lúc: chỉ một người thành công, nên việc chỉ chạy một lần.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(a => new { a.Status, a.CreatedAt }).HasDatabaseName("ix_ops_approval_requests_status");
        builder.HasIndex(a => new { a.Type, a.TargetId })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName("uq_ops_approval_requests_one_pending");

        builder.HasOne<SupportTicket>().WithMany().HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.DecidedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class CannedReplyConfiguration : IEntityTypeConfiguration<CannedReply>
{
    public void Configure(EntityTypeBuilder<CannedReply> builder)
    {
        builder.ToTable("canned_replies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(c => c.Body).HasColumnName("body").HasMaxLength(4000).IsRequired();
        builder.Property(c => c.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
