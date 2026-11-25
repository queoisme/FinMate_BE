using FinMate.Application.DataExport.Package;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.DataExport;

/// <summary>
/// Nhóm (BR-EXP-07/08): nhóm đang và đã tham gia, vai trò, và phần của CHÍNH user. Thành viên khác chỉ còn vai trò: không id, tên, email hay ngày.
/// Chữ do người khác viết (tiêu đề hoá đơn họ trả, lý do họ duyệt, lời nhắn mời) không vào gói (D10).
/// </summary>
public sealed partial class PersonalDataReader
{
    private async Task<PackageSection> GroupsAsync(Guid userId, CancellationToken ct)
    {
        var memberships = await db.GroupMembers.AsNoTracking().Where(m => m.UserId == userId).OrderBy(m => m.JoinedAt).ToListAsync(ct);
        var groups = new List<object>();
        foreach (var membership in memberships)
        {
            groups.Add(await GroupAsync(userId, membership, ct));
        }

        var email = await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.Email).SingleAsync(ct);
        var invitationsToMe = await (from i in db.GroupInvitations
                                     join g in db.Groups.IgnoreQueryFilters() on i.GroupId equals g.Id
                                     where i.Email != null && i.Email.ToLower() == email.ToLower()
                                     orderby i.CreatedAt
                                     select new
                                     {
                                         groupName = g.Name, role = i.Role, createdAt = i.CreatedAt, expiresAt = i.ExpiresAt, acceptedAt = i.AcceptedAt,
                                         declinedAt = i.DeclinedAt, revokedAt = i.RevokedAt,
                                     }).AsNoTracking().ToListAsync(ct);
        return new PackageSection("groups.json", new { groups, invitationsToYourEmail = invitationsToMe }, IsList: false);
    }

    private async Task<object> GroupAsync(Guid userId, Domain.Entities.GroupMember membership, CancellationToken ct)
    {
        var groupId = membership.GroupId;
        var group = await db.Groups.IgnoreQueryFilters().AsNoTracking().Where(g => g.Id == groupId)
            .Select(g => new { id = g.Id, name = g.Name, type = g.Type, createdAt = g.CreatedAt, deletedAt = g.DeletedAt }).SingleAsync(ct);
        return new
        {
            group,
            membership = new
            {
                role = membership.Role, permissionsGranted = membership.PermissionsGranted, permissionsRevoked = membership.PermissionsRevoked,
                alertsEnabled = membership.AlertsEnabled, defaultFinancialAccountId = membership.DefaultFinancialAccountId,
                joinedAt = membership.JoinedAt, leftAt = membership.LeftAt,
            },
            members = await db.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId && m.LeftAt == null).OrderBy(m => m.Role)
                .Select(m => new { role = m.Role, isYou = m.UserId == userId }).ToListAsync(ct),
            shareRules = await db.GroupShareRules.AsNoTracking().Where(r => r.GroupId == groupId && r.UserId == userId)
                .Select(r => new { financialAccountId = r.FinancialAccountId, categoryId = r.CategoryId, isActive = r.IsActive, createdAt = r.CreatedAt })
                .ToListAsync(ct),
            bills = await BillsAsync(userId, groupId, ct),
            settlements = await db.GroupSettlements.AsNoTracking().Where(s => s.GroupId == groupId && (s.FromUserId == userId || s.ToUserId == userId))
                .OrderBy(s => s.CreatedAt)
                .Select(s => new
                {
                    direction = s.FromUserId == userId ? "you_paid" : "you_received", amountCents = s.AmountCents,
                    note = s.CreatedByUserId == userId ? s.Note : null, createdAt = s.CreatedAt,
                }).ToListAsync(ct),
            debtReminders = await db.GroupDebtReminders.AsNoTracking().Where(r => r.GroupId == groupId && (r.DebtorUserId == userId || r.SentByUserId == userId))
                .OrderBy(r => r.SentAt).Select(r => new { direction = r.DebtorUserId == userId ? "received" : "sent", sentAt = r.SentAt }).ToListAsync(ct),
            fundEntries = await FundEntriesAsync(userId, groupId, ct),
            invitations = await InvitationsAsync(userId, groupId, ct),
            approvals = await ApprovalsAsync(userId, groupId, ct),
            activity = await db.GroupActivities.AsNoTracking().Where(a => a.GroupId == groupId && a.ActorUserId == userId).OrderBy(a => a.CreatedAt)
                .Select(a => new { type = a.Type, subjectId = a.SubjectId, createdAt = a.CreatedAt }).ToListAsync(ct),
        };
    }

    /// <summary>D10: hoá đơn user trả hoặc tạo thì đủ; hoá đơn người khác trả mà user có phần thì chỉ ngày, phần của user, danh mục.</summary>
    private async Task<object> BillsAsync(Guid userId, Guid groupId, CancellationToken ct)
    {
        var bills = await db.GroupBills.AsNoTracking().Where(b => b.GroupId == groupId
                && (b.PaidByUserId == userId || b.CreatedByUserId == userId || b.Splits.Any(s => s.UserId == userId)))
            .OrderBy(b => b.OccurredAt)
            .Select(b => new
            {
                b.Id, b.Title, b.AmountCents, b.PaidByUserId, b.CreatedByUserId, b.OccurredAt, b.SplitMode, b.Note,
                CategoryName = b.Category != null ? b.Category.Name : b.GroupCategory != null ? b.GroupCategory.Name : null,
                YourShare = b.Splits.Where(s => s.UserId == userId).Select(s => (long?)s.ShareCents).FirstOrDefault(),
            }).ToListAsync(ct);
        return bills.Select(b => b.PaidByUserId == userId || b.CreatedByUserId == userId
            ? (object)new
            {
                id = b.Id, title = b.Title, amountCents = b.AmountCents, youPaid = b.PaidByUserId == userId, occurredAt = b.OccurredAt,
                splitMode = b.SplitMode, categoryName = b.CategoryName, note = b.Note, yourShareCents = b.YourShare,
            }
            : new { id = b.Id, youPaid = false, occurredAt = b.OccurredAt, categoryName = b.CategoryName, yourShareCents = b.YourShare }).ToList();
    }

    private async Task<object> FundEntriesAsync(Guid userId, Guid groupId, CancellationToken ct)
        => await db.GroupFundEntries.AsNoTracking().Where(e => e.GroupId == groupId && e.MemberUserId == userId).OrderBy(e => e.TransactedAt)
            .Select(e => new
            {
                id = e.Id, fundName = e.Fund!.Name, kind = e.Kind, amountCents = e.AmountCents, transactedAt = e.TransactedAt,
                categoryName = e.Category != null ? e.Category.Name : e.GroupCategory != null ? e.GroupCategory.Name : null,
                note = e.Note, merchantName = e.MerchantName, origin = e.Origin, createdAt = e.CreatedAt, deletedAt = e.DeletedAt,
            }).ToListAsync(ct);

    /// <summary>Lời mời user tạo (không email người được mời) hoặc user đã nhận.</summary>
    private async Task<object> InvitationsAsync(Guid userId, Guid groupId, CancellationToken ct)
        => await db.GroupInvitations.AsNoTracking().Where(i => i.GroupId == groupId && (i.CreatedByUserId == userId || i.AcceptedByUserId == userId))
            .OrderBy(i => i.CreatedAt)
            .Select(i => new
            {
                direction = i.CreatedByUserId == userId ? "sent" : "accepted", role = i.Role, createdAt = i.CreatedAt, expiresAt = i.ExpiresAt,
                acceptedAt = i.AcceptedAt, declinedAt = i.DeclinedAt, revokedAt = i.RevokedAt, message = i.CreatedByUserId == userId ? i.Message : null,
            }).ToListAsync(ct);

    /// <summary>Đề xuất user gửi: đủ (trừ payload và lý do người duyệt viết). Đề xuất user duyệt của người khác: chỉ loại, kết quả, thời điểm.</summary>
    private async Task<object> ApprovalsAsync(Guid userId, Guid groupId, CancellationToken ct)
        => await db.GroupApprovals.AsNoTracking().Where(a => a.GroupId == groupId && (a.RequestedByUserId == userId || a.DecidedByUserId == userId))
            .OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                direction = a.RequestedByUserId == userId ? "requested" : "decided", kind = a.Kind, status = a.Status,
                title = a.RequestedByUserId == userId ? a.Title : null, amountCents = a.RequestedByUserId == userId ? (long?)a.AmountCents : null,
                createdAt = a.CreatedAt, decidedAt = a.DecidedAt,
            }).ToListAsync(ct);
}
