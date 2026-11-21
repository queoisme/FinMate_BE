using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <param name="PaidByName">Null khi người trả đã bị xóa cứng.</param>
public sealed record GroupBillView(GroupBill Bill, string? PaidByName, string? CategoryName, string? GroupCategoryName);

public sealed record GroupBillPage(IReadOnlyList<GroupBillView> Items, string? NextCursor);

/// <summary>Chỉ số liệu thô để tính nợ: <c>UserId</c> null là người đã xóa cứng tài khoản.</summary>
public sealed record GroupDebtRows(IReadOnlyList<GroupBill> Bills, IReadOnlyList<GroupSettlement> Settlements);

/// <summary>Quyền được kiểm ở service trước khi gọi vào đây (như <see cref="IGroupFinanceRepository"/>).</summary>
public interface IGroupBillRepository
{
    void AddBill(GroupBill bill);
    Task<GroupBillView?> GetViewAsync(Guid groupId, Guid billId, CancellationToken ct = default);

    /// <param name="onlyUserId">Khác null thì chỉ hóa đơn người đó trả hoặc có phần.</param>
    Task<GroupBillPage> ListAsync(Guid groupId, Guid? onlyUserId, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>Mọi hóa đơn (kèm phần chia) và mọi lần trả nợ của nhóm, không giới hạn thời gian: nợ cộng dồn từ đầu.</summary>
    Task<GroupDebtRows> LoadDebtRowsAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Hóa đơn trong khoảng, cùng hình dạng với dòng chi từ quỹ để báo cáo tháng dùng chung.</summary>
    Task<List<GroupFundExpenseRow>> ListExpenseRowsAsync(Guid groupId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);

    /// <summary>Tổng phần của <paramref name="userId"/> trong các hóa đơn của nhóm trong khoảng (đã tính chia đều).</summary>
    Task<long> SumUserShareAsync(Guid groupId, Guid userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);

    void AddSettlement(GroupSettlement settlement);
    void AddReminder(GroupDebtReminder reminder);

    /// <summary>Lần nhắc gần nhất tới người đó trong nhóm, null nếu chưa từng.</summary>
    Task<DateTimeOffset?> LastReminderAtAsync(Guid groupId, Guid debtorUserId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
