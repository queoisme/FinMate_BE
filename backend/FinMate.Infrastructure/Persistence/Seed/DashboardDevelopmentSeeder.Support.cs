using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Support;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Infrastructure.Persistence.Seed;

public static partial class DashboardDevelopmentSeeder
{
    private static void AddSupport(FinMateDbContext db, Dictionary<string, User> people, Dictionary<string, Ledger> ledgers,
        SupportSettings settings, DateTimeOffset now)
    {
        AddTicket(db, people, settings, "notification", "an", SupportTicketCategory.Notification, SupportTicketStatus.New,
            "Không thấy giao dịch sau khi bật đọc thông báo", "Tôi đã cấp quyền đọc thông báo nhưng chưa thấy giao dịch mới. Cần kiểm tra quyền chạy nền và ví được theo dõi.", null, now.AddMinutes(-8));
        var access = AddTicket(db, people, settings, "receipt", "an", SupportTicketCategory.Receipt, SupportTicketStatus.InProgress,
            "Kiểm tra danh mục sau khi quét hóa đơn", "Một món trên hóa đơn được xếp vào danh mục khác với mong muốn. Tôi muốn đối chiếu rồi sửa lại.", "staff", now.AddHours(-3));
        var pending = AddTicket(db, people, settings, "duplicate", "binh", SupportTicketCategory.Transaction, SupportTicketStatus.WaitingCustomer,
            "Cùng một khoản chi xuất hiện hai lần", "Tôi vừa nhập tay rồi nhận thêm thông báo ngân hàng. Cần xác định bản trùng trước khi gộp.", "staff", now.AddHours(-6));
        AddTicket(db, people, settings, "overdue", "chi", SupportTicketCategory.Account, SupportTicketStatus.InProgress,
            "Chuyển sang điện thoại mới và kiểm tra số dư", "Tôi cần đối chiếu ví trên thiết bị mới trước khi tiếp tục ghi giao dịch.", "staff", now.AddDays(-3));
        AddTicket(db, people, settings, "security", "dung", SupportTicketCategory.Security, SupportTicketStatus.Escalated,
            "Rà soát tài khoản đang bị khóa", "Tài khoản kiểm thử được đặt ở trạng thái khóa để diễn tập quy trình xác minh và đề xuất mở khóa.", "manager", now.AddHours(-2));
        AddTicket(db, people, settings, "resolved", "binh", SupportTicketCategory.Question, SupportTicketStatus.Resolved,
            "Hiểu cách tính số tiền có thể chi", "Tôi muốn biết khoản dự phòng và khoản định kỳ ảnh hưởng đến số tiền có thể chi thế nào.", "staff", now.AddDays(-2));
        AddTicket(db, people, settings, "closed", "chi", SupportTicketCategory.Notification, SupportTicketStatus.Closed,
            "Đã bật lại quyền hoạt động nền", "Sau khi kiểm tra cài đặt pin, ứng dụng đã đọc được thông báo trở lại.", "manager", now.AddDays(-5));
        AddTicket(db, people, settings, "new-user", "linh", SupportTicketCategory.Question, SupportTicketStatus.New,
            "Hướng dẫn tạo ví và ngân sách đầu tiên", "Tôi mới cài ứng dụng và muốn biết thứ tự thiết lập ví, thu nhập, ngân sách.", null, now.AddMinutes(-20));
        AddGrant(db, access, people["staff"], ledgers["an"].Bank.Id, SupportAccessStatus.Approved, now);
        AddGrant(db, pending, people["staff"], null, SupportAccessStatus.Pending, now);
        AddApprovals(db, people, now);
        AddCannedReplies(db, now);
    }

    private static SupportTicket AddTicket(FinMateDbContext db, Dictionary<string, User> people, SupportSettings settings,
        string key, string customer, SupportTicketCategory category, SupportTicketStatus status,
        string subject, string body, string? assignee, DateTimeOffset created)
    {
        var ticket = new SupportTicket { Id = Id($"ticket:{key}"), RequesterUserId = people[customer].Id,
            AssigneeUserId = assignee == null ? null : people[assignee].Id, Category = category,
            Priority = SupportTicketRules.DefaultPriority(category), Status = status, Subject = $"{Label} {subject}",
            CreatedAt = created, UpdatedAt = created };
        SupportTicketRules.ApplyDueDates(ticket, settings);
        db.SupportTickets.Add(ticket);
        db.SupportTicketMessages.Add(new SupportTicketMessage { Id = Id($"message:{key}:customer"), TicketId = ticket.Id,
            AuthorUserId = people[customer].Id, Kind = SupportMessageKind.Customer, Body = $"{Label} {body}", CreatedAt = created });
        if (assignee != null)
        {
            ticket.FirstRespondedAt = created.AddMinutes(10);
            ticket.UpdatedAt = created.AddMinutes(12);
            db.SupportTicketMessages.Add(new SupportTicketMessage { Id = Id($"message:{key}:staff"), TicketId = ticket.Id,
                AuthorUserId = people[assignee].Id, Kind = SupportMessageKind.Staff, CreatedAt = created.AddMinutes(10),
                Body = "Mình đã tiếp nhận. Bạn vui lòng mô tả các bước vừa thực hiện; không gửi mật khẩu, OTP hay số tài khoản đầy đủ." });
            db.SupportTicketMessages.Add(new SupportTicketMessage { Id = Id($"message:{key}:note"), TicketId = ticket.Id,
                AuthorUserId = people[assignee].Id, Kind = SupportMessageKind.InternalNote, CreatedAt = created.AddMinutes(12),
                Body = $"{Label} Ca phục vụ diễn tập vận hành. Chỉ sử dụng dữ liệu và tài khoản của bộ kiểm thử dashboard." });
        }
        if (status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed)
        {
            ticket.ResolvedAt = created.AddHours(1);
            ticket.UpdatedAt = ticket.ResolvedAt.Value;
            ticket.Rating = 5;
        }
        if (status == SupportTicketStatus.Closed) { ticket.ClosedAt = created.AddDays(1); ticket.UpdatedAt = ticket.ClosedAt.Value; }
        return ticket;
    }

    private static void AddGrant(FinMateDbContext db, SupportTicket ticket, User staff, Guid? account,
        SupportAccessStatus status, DateTimeOffset now)
    {
        db.SupportAccessGrants.Add(new SupportAccessGrant { Id = Id($"grant:{ticket.Id}"), TicketId = ticket.Id,
            RequestedByUserId = staff.Id, Scopes = SupportAccessScope.Accounts | SupportAccessScope.Transactions | SupportAccessScope.Budgets
                | (account.HasValue ? SupportAccessScope.Draft : SupportAccessScope.None), FinancialAccountId = account,
            Status = status, Reason = $"{Label} Sự đồng ý mô phỏng cho đúng nhân viên và dữ liệu kiểm thử của ca này.",
            CreatedAt = now.AddMinutes(-30), RequestExpiresAt = now.AddDays(1),
            DecidedAt = status == SupportAccessStatus.Approved ? now.AddMinutes(-20) : null,
            ExpiresAt = status == SupportAccessStatus.Approved ? now.AddDays(1) : null });
    }

    private static void AddApprovals(FinMateDbContext db, Dictionary<string, User> people, DateTimeOffset now)
    {
        db.DataDeletionRequests.Add(new DataDeletionRequest { Id = Id("deletion:ha"), UserId = people["ha"].Id,
            RequestedAt = now.AddDays(-10), ScheduledHardDeleteAt = now.AddDays(20), Status = DataDeletionStatus.Pending });
        foreach (var (type, key) in new[] { (OpsApprovalType.UnlockUser, "dung"), (OpsApprovalType.CancelDeletion, "ha") })
            db.OpsApprovalRequests.Add(new OpsApprovalRequest { Id = Id($"approval:{key}"), Type = type,
                TargetId = people[key].Id, RequestedByUserId = people["staff"].Id, Status = OpsApprovalStatus.Pending,
                Reason = $"{Label} Diễn tập xác minh và duyệt hai người trên tài khoản mô phỏng {key}.",
                CreatedAt = now.AddHours(-1), ExpiresAt = now.AddDays(7) });
        var group = new Group { Id = Id("group:household"), Name = $"{Label} Gia đình cùng quản lý chi tiêu",
            Type = GroupType.Family, CreatedByUserId = people["an"].Id, CreatedAt = now.AddMonths(-2), UpdatedAt = now };
        db.Groups.Add(group);
        foreach (var key in new[] { "an", "binh", "chi" }) db.GroupMembers.Add(new GroupMember {
            Id = Id($"member:{key}"), GroupId = group.Id, UserId = people[key].Id,
            Role = key == "an" ? GroupRole.Owner : GroupRole.Member, JoinedAt = now.AddMonths(-2), UpdatedAt = now });
        db.OpsApprovalRequests.Add(new OpsApprovalRequest { Id = Id("approval:group"), Type = OpsApprovalType.TransferGroupOwner,
            TargetId = group.Id, NewOwnerUserId = people["binh"].Id, RequestedByUserId = people["staff"].Id,
            Status = OpsApprovalStatus.Pending, Reason = $"{Label} Chủ nhóm mô phỏng đề nghị chuyển quyền cho một thành viên đang tham gia.",
            CreatedAt = now.AddMinutes(-45), ExpiresAt = now.AddDays(7) });
    }

    private static void AddCannedReplies(FinMateDbContext db, DateTimeOffset now)
    {
        (string Title, string Body)[] replies = [
            ("Kiểm tra quyền đọc thông báo", "Bạn kiểm tra quyền truy cập thông báo của FinMate, cho phép chạy nền và tắt hạn chế pin. Sau đó đối chiếu ngân hàng/ví được theo dõi. Không cần gửi mật khẩu hay OTP."),
            ("Gộp giao dịch nghi trùng", "Bạn đối chiếu số tiền, thời điểm và ví của hai bản ghi. Chỉ gộp khi chắc chắn là cùng một giao dịch; chọn không trùng nếu đó là hai khoản chi riêng."),
            ("Sửa kết quả quét hóa đơn", "Bạn kiểm tra từng mặt hàng, tổng tiền và danh mục trước khi xác nhận. Nếu ảnh khó đọc, chụp thẳng, đủ sáng và giữ trọn hóa đơn."),
            ("Xin quyền đối chiếu dữ liệu", "Mình sẽ gửi yêu cầu xem đúng phần dữ liệu cần cho ca này. Bạn có thể xem phạm vi, thời hạn và thu hồi quyền bất kỳ lúc nào."),
            ("Giải thích dự báo", "Dự báo dựa trên lịch sử đã ghi, các khoản định kỳ và khoản dự phòng. Khoản chi chưa nhập hoặc thu nhập đến muộn có thể làm số thực tế khác dự báo."),
            ("Chuyển cấp xác minh tài khoản", "Mình đã chuyển ca để quản lý rà soát. Không chia sẻ mật khẩu, mã xác thực hoặc thông tin đăng nhập ngân hàng qua ca hỗ trợ."),
            ("Bắt đầu quản lý ngân sách", "Bạn tạo ví, khai thu nhập dự kiến, chọn hạn mức cho các danh mục cần theo dõi rồi ghi nhận giao dịch. Chuyển tiền giữa hai ví của mình được ghi là chuyển khoản.") ];
        for (var i = 0; i < replies.Length; i++) db.CannedReplies.Add(new CannedReply { Id = Id($"reply:{i}"),
            Title = $"{Label} {replies[i].Title}", Body = replies[i].Body, IsActive = true, CreatedAt = now, UpdatedAt = now });
    }
}
