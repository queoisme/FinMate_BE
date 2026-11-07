using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("support_tickets", t =>
        {
            t.HasCheckConstraint("chk_support_tickets_status", "status IN ('open','triaged','responded','resolved','escalated','managerreview','rejected','reopened')");
            t.HasCheckConstraint("chk_support_tickets_subject", "length(btrim(subject)) > 0");
            t.HasCheckConstraint("chk_support_tickets_body", "length(btrim(body)) > 0");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.Subject).HasColumnName("subject").HasMaxLength(120).IsRequired();
        builder.Property(t => t.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<SupportTicketStatus>(v, true))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(t => t.Reply).HasColumnName("reply").HasMaxLength(2000);
        builder.Property(t => t.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(500);
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.UserId, t.UpdatedAt }).HasDatabaseName("ix_support_tickets_user_updated");
        builder.HasIndex(t => new { t.Status, t.UpdatedAt }).HasDatabaseName("ix_support_tickets_status_updated");
    }
}
