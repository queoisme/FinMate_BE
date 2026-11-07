using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using S = FinMate.Domain.Entities.SupportTicketStatus;

namespace FinMate.Application.Support;

/// <summary>Ai đang thực hiện chuyển trạng thái: chủ khiếu nại, nhân sự hỗ trợ (SupportStaff trở lên), hay quản lý (Manager trở lên).</summary>
public enum TicketActor
{
    Owner,
    Support,
    Manager,
}

/// <summary>
/// Bảng chuyển trạng thái DUY NHẤT của khiếu nại. Mỗi chuyển trạng thái hợp lệ gắn với đúng một loại người được làm; chuyển ngoài bảng bị từ chối. Quyết định cuối của khiếu nại đã leo thang
/// (<c>ManagerReview → Resolved | Rejected</c>) chỉ Manager trở lên được: kiểm ở đây chứ không chỉ ở attribute của controller, để một controller thiếu policy cũng không mở được lỗ hổng.
/// </summary>
public static class TicketStateMachine
{
    private static readonly Dictionary<(S From, S To), TicketActor> Table = new()
    {
        [(S.Open, S.Triaged)] = TicketActor.Support,
        [(S.Triaged, S.Responded)] = TicketActor.Support,
        [(S.Triaged, S.Escalated)] = TicketActor.Support,
        [(S.Responded, S.Resolved)] = TicketActor.Support,
        [(S.Escalated, S.ManagerReview)] = TicketActor.Manager,
        [(S.ManagerReview, S.Resolved)] = TicketActor.Manager,
        [(S.ManagerReview, S.Rejected)] = TicketActor.Manager,
        [(S.Reopened, S.Triaged)] = TicketActor.Support,
    };

    private static readonly Dictionary<(S From, S To), TicketActor> OwnerTable = new()
    {
        [(S.Responded, S.Resolved)] = TicketActor.Owner, // người dùng xác nhận đã được giải quyết
        [(S.Responded, S.Reopened)] = TicketActor.Owner,
        [(S.Resolved, S.Reopened)] = TicketActor.Owner,
        [(S.Rejected, S.Reopened)] = TicketActor.Owner,
    };

    /// <summary>Loại hành động mà một vai trò nhân sự được làm. Admin và Manager làm được cả việc của nhân sự hỗ trợ; vai trò khác (kể cả ContentStaff, User) không làm được gì ở khiếu nại.</summary>
    public static IReadOnlySet<TicketActor> StaffActors(UserRole role) => role switch
    {
        UserRole.Admin or UserRole.Manager => new HashSet<TicketActor> { TicketActor.Support, TicketActor.Manager },
        UserRole.SupportStaff => new HashSet<TicketActor> { TicketActor.Support },
        _ => new HashSet<TicketActor>(),
    };

    public static readonly IReadOnlySet<TicketActor> OwnerActors = new HashSet<TicketActor> { TicketActor.Owner };

    public static bool IsLegal(S from, S to, IReadOnlySet<TicketActor> actors)
        => (Table.TryGetValue((from, to), out var staffNeeded) && actors.Contains(staffNeeded))
           || (OwnerTable.TryGetValue((from, to), out var ownerNeeded) && actors.Contains(ownerNeeded));

    /// <summary>Có chuyển <paramref name="from"/> → <paramref name="to"/> nào trong bảng không (bất kể ai làm)? Để phân biệt "sai trạng thái" với "sai người".</summary>
    public static bool Exists(S from, S to) => Table.ContainsKey((from, to)) || OwnerTable.ContainsKey((from, to));
}
