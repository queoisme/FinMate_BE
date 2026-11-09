using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class CommunityPlaceConfiguration : IEntityTypeConfiguration<CommunityPlace>
{
    public void Configure(EntityTypeBuilder<CommunityPlace> builder)
    {
        builder.ToTable("community_places");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(p => p.NameKey).HasColumnName("name_key").HasMaxLength(120).IsRequired();
        builder.Property(p => p.ProvinceCode).HasColumnName("province_code").HasMaxLength(40).IsRequired();
        builder.Property(p => p.AreaCode).HasColumnName("area_code").HasMaxLength(40).IsRequired();
        builder.Property(p => p.AddressText).HasColumnName("address_text").HasMaxLength(200);
        builder.Property(p => p.HiddenAt).HasColumnName("hidden_at");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(p => new { p.ProvinceCode, p.AreaCode, p.NameKey }).IsUnique().HasDatabaseName("uq_community_places_area_name");
    }
}

public class CommunityPostConfiguration : IEntityTypeConfiguration<CommunityPost>
{
    public void Configure(EntityTypeBuilder<CommunityPost> builder)
    {
        builder.ToTable("community_posts", t =>
        {
            t.HasCheckConstraint("chk_community_posts_status", GroupColumns.CheckIn<CommunityContentStatus>("status"));
            t.HasCheckConstraint("chk_community_posts_place_fields", "place_id IS NOT NULL OR (price_cents IS NULL AND NOT spent_here_verified)");
            t.HasCheckConstraint("chk_community_posts_price", "price_cents IS NULL OR price_cents > 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.Topic).HasColumnName("topic").HasMaxLength(40).IsRequired();
        builder.Property(p => p.AuthorId).HasColumnName("author_id").IsRequired();
        builder.Property(p => p.Title).HasColumnName("title").HasMaxLength(120);
        builder.Property(p => p.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(p => p.PlaceId).HasColumnName("place_id");
        builder.Property(p => p.PriceCents).HasColumnName("price_cents");
        builder.Property(p => p.Tags).HasColumnName("tags").HasColumnType("text[]").IsRequired();
        builder.Property(p => p.IsAnonymous).HasColumnName("is_anonymous").HasDefaultValue(true).IsRequired();
        builder.Property(p => p.SpentHereVerified).HasColumnName("spent_here_verified").HasDefaultValue(false).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<CommunityContentStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(p => p.EditedAt).HasColumnName("edited_at");
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(p => p.DeletedAt == null);

        builder.HasIndex(p => new { p.Topic, p.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_community_posts_topic_created");
        builder.HasIndex(p => p.CreatedAt).IsDescending().HasDatabaseName("ix_community_posts_created");
        builder.HasIndex(p => new { p.AuthorId, p.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_community_posts_author_created");
        builder.HasIndex(p => new { p.AuthorId, p.PlaceId })
            .IsUnique()
            .HasFilter("place_id IS NOT NULL AND deleted_at IS NULL")
            .HasDatabaseName("uq_community_posts_author_place");
        builder.HasIndex(p => p.PlaceId).HasFilter("place_id IS NOT NULL").HasDatabaseName("ix_community_posts_place");

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.AuthorId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CommunityPlace>().WithMany().HasForeignKey(p => p.PlaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CommunityPostImageConfiguration : IEntityTypeConfiguration<CommunityPostImage>
{
    public void Configure(EntityTypeBuilder<CommunityPostImage> builder)
    {
        builder.ToTable("community_post_images", t => t.HasCheckConstraint("chk_community_post_images_status", GroupColumns.CheckIn<CommunityImageStatus>("status")));

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(i => i.PostId).HasColumnName("post_id").IsRequired();
        builder.Property(i => i.Data).HasColumnName("data").HasColumnType("bytea").IsRequired();
        builder.Property(i => i.Width).HasColumnName("width").IsRequired();
        builder.Property(i => i.Height).HasColumnName("height").IsRequired();
        builder.Property(i => i.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(i => i.Position).HasColumnName("position").IsRequired();
        builder.Property(i => i.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<CommunityImageStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(i => i.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(i => i.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(i => new { i.Status, i.CreatedAt }).HasDatabaseName("ix_community_post_images_queue");
        builder.HasIndex(i => i.PostId).HasDatabaseName("ix_community_post_images_post");

        builder.HasOne<CommunityPost>().WithMany().HasForeignKey(i => i.PostId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.ReviewedBy).OnDelete(DeleteBehavior.SetNull);
    }
}

public class CommunityCommentConfiguration : IEntityTypeConfiguration<CommunityComment>
{
    public void Configure(EntityTypeBuilder<CommunityComment> builder)
    {
        builder.ToTable("community_comments", t => t.HasCheckConstraint("chk_community_comments_status", GroupColumns.CheckIn<CommunityContentStatus>("status")));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.PostId).HasColumnName("post_id").IsRequired();
        builder.Property(c => c.AuthorId).HasColumnName("author_id").IsRequired();
        builder.Property(c => c.Body).HasColumnName("body").HasMaxLength(300).IsRequired();
        builder.Property(c => c.IsAnonymous).HasColumnName("is_anonymous").HasDefaultValue(true).IsRequired();
        builder.Property(c => c.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<CommunityContentStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(c => c.EditedAt).HasColumnName("edited_at");
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(c => c.DeletedAt == null);

        builder.HasIndex(c => new { c.PostId, c.CreatedAt }).HasDatabaseName("ix_community_comments_post_created");
        builder.HasIndex(c => new { c.AuthorId, c.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_community_comments_author_created");

        builder.HasOne<CommunityPost>().WithMany().HasForeignKey(c => c.PostId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CommunityPostVoteConfiguration : IEntityTypeConfiguration<CommunityPostVote>
{
    public void Configure(EntityTypeBuilder<CommunityPostVote> builder)
    {
        builder.ToTable("community_post_votes");

        builder.HasKey(v => new { v.PostId, v.UserId });
        builder.Property(v => v.PostId).HasColumnName("post_id");
        builder.Property(v => v.UserId).HasColumnName("user_id");
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(v => new { v.UserId, v.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_community_post_votes_user_created");

        builder.HasOne<CommunityPost>().WithMany().HasForeignKey(v => v.PostId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CommunityReportConfiguration : IEntityTypeConfiguration<CommunityReport>
{
    public void Configure(EntityTypeBuilder<CommunityReport> builder)
    {
        builder.ToTable("community_reports", t =>
        {
            t.HasCheckConstraint("chk_community_reports_target", "(post_id IS NULL) <> (comment_id IS NULL)");
            t.HasCheckConstraint("chk_community_reports_reason", GroupColumns.CheckIn<CommunityReportReason>("reason"));
            t.HasCheckConstraint("chk_community_reports_priority", GroupColumns.CheckIn<CommunityReportPriority>("priority"));
            t.HasCheckConstraint("chk_community_reports_status", GroupColumns.CheckIn<CommunityReportStatus>("status"));
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.PostId).HasColumnName("post_id");
        builder.Property(r => r.CommentId).HasColumnName("comment_id");
        builder.Property(r => r.ReporterId).HasColumnName("reporter_id").IsRequired();
        builder.Property(r => r.Reason).HasColumnName("reason").HasConversion(GroupColumns.Converter<CommunityReportReason>()).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Priority).HasColumnName("priority").HasConversion(GroupColumns.Converter<CommunityReportPriority>()).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<CommunityReportStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(r => r.ResolvedBy).HasColumnName("resolved_by");
        builder.Property(r => r.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(r => new { r.PostId, r.ReporterId }).IsUnique().HasFilter("post_id IS NOT NULL").HasDatabaseName("uq_community_reports_post_reporter");
        builder.HasIndex(r => new { r.CommentId, r.ReporterId }).IsUnique().HasFilter("comment_id IS NOT NULL").HasDatabaseName("uq_community_reports_comment_reporter");
        builder.HasIndex(r => new { r.Status, r.Priority, r.CreatedAt }).HasDatabaseName("ix_community_reports_queue");
        builder.HasIndex(r => new { r.ReporterId, r.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_community_reports_reporter_created");

        builder.HasOne<CommunityPost>().WithMany().HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CommunityComment>().WithMany().HasForeignKey(r => r.CommentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ResolvedBy).OnDelete(DeleteBehavior.SetNull);
    }
}
