using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Khiếu nại. Truy vấn của NGƯỜI DÙNG luôn kèm <c>userId</c> (không có cách lấy khiếu nại của người khác); truy vấn của nhân sự là các hàm không có <c>userId</c> và chỉ được gọi sau cổng vai trò.</summary>
public interface ISupportTicketRepository
{
    Task<SupportTicket?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<SupportTicket?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task<(int Total, List<SupportTicket> Items)> ListForUserAsync(Guid userId, SupportTicketStatus? status, int limit, int offset, CancellationToken ct = default);

    /// <summary>Phân trang và tìm kiếm (tiêu đề) trên máy chủ, mới cập nhật trước.</summary>
    Task<(int Total, List<SupportTicket> Items)> ListAllAsync(SupportTicketStatus? status, string? search, int limit, int offset, CancellationToken ct = default);

    /// <summary>Số khiếu nại chưa đóng (không phải Resolved / Rejected) của người dùng, để chặn gửi tràn.</summary>
    Task<int> CountOpenForUserAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(SupportTicket ticket, CancellationToken ct = default);

    Task UpdateAsync(SupportTicket ticket, CancellationToken ct = default);
}
