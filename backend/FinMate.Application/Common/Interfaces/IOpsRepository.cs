using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public sealed record OpsAssigneeLoad(Guid UserId, string? DisplayName, int OpenCount, int OverdueCount);

public sealed record OpsCount(string Key, int Count);

public sealed record OpsDashboardData(
    IReadOnlyList<OpsCount> OpenByStatus,
    IReadOnlyList<OpsCount> OpenByPriority,
    int Unassigned,
    int Overdue,
    IReadOnlyList<OpsAssigneeLoad> Load,
    IReadOnlyList<OpsCount> RecentByCategory,
    int RecentCount,
    int RecentFirstResponded,
    int RecentFirstRespondedInTime,
    int RecentResolved,
    int RecentResolvedInTime,
    int RatingCount,
    double? AverageRating,
    int PendingApprovals);

public interface IOpsRepository
{
    /// <summary>Lưu đề xuất mới. Đã có đề xuất đang chờ cho cùng đối tượng thì 409, kể cả khi hai người gửi cùng lúc.</summary>
    Task AddApprovalAsync(OpsApprovalRequest approval, CancellationToken ct = default);
    Task<OpsApprovalRequest?> GetApprovalAsync(Guid id, CancellationToken ct = default);
    Task<OpsApprovalRequest?> GetPendingApprovalAsync(OpsApprovalType type, Guid targetId, CancellationToken ct = default);

    /// <summary>Ghi thẳng xuống DB, không qua change tracker: lúc này tracker có thể còn giữ thay đổi hỏng của bước thực hiện.</summary>
    Task MarkApprovalFailedAsync(Guid id, string failureCode, CancellationToken ct = default);
    /// <summary>Một trang đề xuất (mới nhất trước) và tổng số khớp. <paramref name="search"/> khớp lý do hoặc ghi chú quyết định.</summary>
    Task<(List<OpsApprovalRequest> Items, int Total)> ListApprovalsAsync(
        OpsApprovalStatus? status, Guid? requestedBy, string? search, int offset, int limit, CancellationToken ct = default);

    /// <summary>Một trang câu trả lời mẫu (theo tiêu đề) và tổng số khớp. <paramref name="search"/> khớp tiêu đề hoặc nội dung.</summary>
    Task<(List<CannedReply> Items, int Total)> ListCannedRepliesAsync(
        bool includeInactive, string? search, int offset, int limit, CancellationToken ct = default);
    Task<CannedReply?> GetCannedReplyAsync(Guid id, CancellationToken ct = default);
    void AddCannedReply(CannedReply reply);

    Task<OpsDashboardData> GetDashboardAsync(DateTimeOffset now, DateTimeOffset recentFrom, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
