using FinMate.Application.DataExport.Package;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.DataExport;

/// <summary>Hỗ trợ, cộng đồng, gói trả phí. Không ảnh, không ghi chú nội bộ, không id nhân viên (BR-EXP-08).</summary>
public sealed partial class PersonalDataReader
{
    private async Task<IEnumerable<PackageSection>> SupportAndCommunityAsync(Guid userId, CancellationToken ct) =>
    [
        new PackageSection("support.json", await SupportAsync(userId, ct), IsList: false),
        new PackageSection("community.json", await CommunityAsync(userId, ct), IsList: false),
        new PackageSection("subscriptions.json", await SubscriptionsAsync(userId, ct), IsList: false),
    ];

    private async Task<object> SupportAsync(Guid userId, CancellationToken ct)
    {
        var tickets = await db.SupportTickets.AsNoTracking().Where(t => t.RequesterUserId == userId).OrderBy(t => t.CreatedAt).ToListAsync(ct);
        var ids = tickets.Select(t => t.Id).ToList();
        var messages = (await db.SupportTicketMessages.AsNoTracking()
                .Where(m => ids.Contains(m.TicketId) && m.Kind != SupportMessageKind.InternalNote).OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    m.TicketId, m.Kind, m.Body, m.CreatedAt,
                    Attachments = db.SupportTicketAttachments.Count(a => a.MessageId == m.Id),
                }).ToListAsync(ct)).ToLookup(m => m.TicketId);
        var grants = (await db.SupportAccessGrants.AsNoTracking().Where(g => ids.Contains(g.TicketId)).OrderBy(g => g.CreatedAt).ToListAsync(ct))
            .ToLookup(g => g.TicketId);

        return new
        {
            tickets = tickets.Select(t => new
            {
                id = t.Id, subject = t.Subject, category = t.Category, priority = t.Priority, status = t.Status, createdAt = t.CreatedAt,
                firstRespondedAt = t.FirstRespondedAt, resolvedAt = t.ResolvedAt, closedAt = t.ClosedAt, rating = t.Rating,
                messages = messages[t.Id].Select(m => new
                {
                    from = m.Kind == SupportMessageKind.Customer ? "Bạn" : "Nhân viên", body = m.Body, createdAt = m.CreatedAt, attachmentCount = m.Attachments,
                }),
                accessRequests = grants[t.Id].Select(g => new
                {
                    scopes = g.Scopes, reason = g.Reason, status = g.Status, financialAccountId = g.FinancialAccountId, createdAt = g.CreatedAt,
                    decidedAt = g.DecidedAt, expiresAt = g.ExpiresAt, revokedAt = g.RevokedAt,
                }),
            }).ToList(),
            accessLog = await db.SupportDataAccessLogs.AsNoTracking().Where(l => l.CustomerUserId == userId).OrderBy(l => l.CreatedAt)
                .Select(l => new { ticketId = l.TicketId, scope = l.Scope, at = l.CreatedAt }).ToListAsync(ct),
        };
    }

    /// <summary>Bài và bình luận CỦA user (kể cả ẩn danh, kể cả đã xoá). Ảnh chỉ ghi số lượng.</summary>
    private async Task<object> CommunityAsync(Guid userId, CancellationToken ct) => new
    {
        posts = await db.CommunityPosts.IgnoreQueryFilters().AsNoTracking().Where(p => p.AuthorId == userId).OrderBy(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id, topic = p.Topic, title = p.Title, body = p.Body,
                placeName = db.CommunityPlaces.Where(pl => pl.Id == p.PlaceId).Select(pl => pl.Name).FirstOrDefault(),
                priceCents = p.PriceCents, tags = p.Tags, isAnonymous = p.IsAnonymous, spentHereVerified = p.SpentHereVerified, status = p.Status,
                imageCount = db.CommunityPostImages.Count(i => i.PostId == p.Id), createdAt = p.CreatedAt, editedAt = p.EditedAt, deletedAt = p.DeletedAt,
            }).ToListAsync(ct),
        comments = await db.CommunityComments.IgnoreQueryFilters().AsNoTracking().Where(c => c.AuthorId == userId).OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                id = c.Id, postId = c.PostId, body = c.Body, isAnonymous = c.IsAnonymous, status = c.Status, createdAt = c.CreatedAt,
                editedAt = c.EditedAt, deletedAt = c.DeletedAt,
            }).ToListAsync(ct),
        votes = await db.CommunityPostVotes.AsNoTracking().Where(v => v.UserId == userId).OrderBy(v => v.CreatedAt)
            .Select(v => new { postId = v.PostId, createdAt = v.CreatedAt }).ToListAsync(ct),
        reports = await db.CommunityReports.AsNoTracking().Where(r => r.ReporterId == userId).OrderBy(r => r.CreatedAt)
            .Select(r => new { postId = r.PostId, commentId = r.CommentId, reason = r.Reason, status = r.Status, createdAt = r.CreatedAt, resolvedAt = r.ResolvedAt })
            .ToListAsync(ct),
    };

    private async Task<object> SubscriptionsAsync(Guid userId, CancellationToken ct) => new
    {
        orders = (await db.SubscriptionOrders.AsNoTracking().Where(o => o.UserId == userId).OrderBy(o => o.CreatedAt).ToListAsync(ct))
            .Select(o => new
            {
                invoiceNumber = o.InvoiceNumber, planCode = o.PlanCode, planName = o.PlanName, cycle = o.Cycle, amountCents = o.AmountCents,
                status = o.Status, features = ParseJson(o.FeaturesJson), createdAt = o.CreatedAt, expiresAt = o.ExpiresAt, paidAt = o.PaidAt, voidedAt = o.VoidedAt,
            }).ToList(),
        periods = await db.UserSubscriptionPeriods.AsNoTracking().Where(p => p.UserId == userId).OrderBy(p => p.StartsAt)
            .Select(p => new { planCode = p.PlanCode, planName = p.PlanName, startsAt = p.StartsAt, endsAt = p.EndsAt, voidedAt = p.VoidedAt })
            .ToListAsync(ct),
        usage = await db.SubscriptionUsages.AsNoTracking().Where(u => u.UserId == userId).OrderBy(u => u.Month)
            .Select(u => new { featureKey = u.FeatureKey, month = u.Month, used = u.Used }).ToListAsync(ct),
    };
}
