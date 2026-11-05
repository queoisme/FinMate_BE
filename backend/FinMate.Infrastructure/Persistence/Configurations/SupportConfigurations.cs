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

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.RequesterUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.AssigneeUserId).OnDelete(DeleteBehavior.SetNull);
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
