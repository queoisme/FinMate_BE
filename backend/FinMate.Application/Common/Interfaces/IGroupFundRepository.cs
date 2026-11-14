using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <param name="MemberName">Tên hiện tại của người gắn với dòng; null khi đã bị ẩn danh (xoá cứng tài khoản).</param>
/// <param name="IsMemberActive">False khi người đó đang chờ xoá tài khoản.</param>
/// <param name="HasPersonalLink">Còn giao dịch cá nhân (chưa xoá) gắn với dòng này.</param>
public sealed record GroupFundEntryView(GroupFundEntry Entry, string? MemberName, bool IsMemberActive, string? CategoryName, bool HasPersonalLink);

public sealed record GroupFundEntryPage(IReadOnlyList<GroupFundEntryView> Items, string? NextCursor);

public sealed record GroupFundEntryQuery(Guid FundId, GroupFundEntryKind? Kind, Guid? MemberUserId, string? Cursor, int Limit);

/// <summary>Quỹ nhóm và dòng quỹ (Phase 85). Không kiểm quyền: mọi lời gọi phải đi sau <c>IGroupAccessService</c>. Lưu qua <c>IGroupRepository.SaveChangesAsync</c>.</summary>
public interface IGroupFundRepository
{
    /// <summary>Quỹ chưa lưu trữ trước, rồi theo thời điểm tạo.</summary>
    Task<List<GroupFund>> ListAsync(Guid groupId, CancellationToken ct = default);

    Task<GroupFund?> GetAsync(Guid groupId, Guid fundId, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid groupId, CancellationToken ct = default);

    void Add(GroupFund fund);

    /// <summary>Dòng chưa xoá của một quỹ, mới nhất trước, phân trang con trỏ như các danh sách nhóm khác.</summary>
    Task<GroupFundEntryPage> ListEntriesAsync(GroupFundEntryQuery query, CancellationToken ct = default);

    Task<GroupFundEntryView?> GetEntryViewAsync(Guid fundId, Guid entryId, CancellationToken ct = default);

    /// <summary>Dòng của quỹ, kể cả đã xoá mềm (để đọc liên kết của giao dịch cá nhân).</summary>
    Task<GroupFundEntry?> GetEntryAsync(Guid fundId, Guid entryId, CancellationToken ct = default);

    Task<GroupFundEntry?> GetEntryByClientRequestAsync(Guid fundId, Guid memberUserId, Guid clientRequestId, CancellationToken ct = default);

    /// <summary>
    /// Giao dịch cá nhân của <paramref name="userId"/> gắn với dòng quỹ; null nếu không có hoặc của người khác. <paramref name="includeDeleted"/>: kể cả
    /// bản đã xoá mềm (mỗi dòng quỹ chỉ có một giao dịch, nên gắn lại ví thì dùng lại đúng dòng đó).
    /// </summary>
    Task<Transaction?> GetPersonalTransactionAsync(Guid entryId, Guid userId, bool includeDeleted = false, CancellationToken ct = default);

    void AddEntry(GroupFundEntry entry);

    void AddPersonalTransaction(Transaction transaction);
}
