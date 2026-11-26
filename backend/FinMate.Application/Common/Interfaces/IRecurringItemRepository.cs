using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Khoản định kỳ theo người dùng. Mọi hàm lọc theo <c>userId</c>: không có đường nào lấy một dòng chỉ bằng id. Các hàm đọc khoản điền sẵn
/// <see cref="RecurringItem.LivePaymentCount"/> (số kỳ đã trả có liên kết) để <c>ToView()</c> tính được số kỳ còn lại (Phase 92).
/// </summary>
public interface IRecurringItemRepository
{
    Task<RecurringItem?> GetAsync(Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>Không truyền trạng thái thì trả mọi khoản trừ đã hủy, đã từ chối và đã hoàn thành (Phase 92, D16).</summary>
    Task<IReadOnlyList<RecurringItem>> ListAsync(Guid userId, RecurringStatus? status, CancellationToken ct = default);

    /// <summary>Mọi khoản (mọi trạng thái), có theo dõi thay đổi — dùng cho job phát hiện và cập nhật.</summary>
    Task<IReadOnlyList<RecurringItem>> ListAllTrackedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Khoản chưa hoàn thành của người nhận này (tối đa một, theo index duy nhất); dòng <c>Completed</c> không tính (Phase 92, BR-INS-09).</summary>
    Task<RecurringItem?> FindByCounterpartyAsync(Guid userId, RecurringDirection direction, string counterpartyKey, CancellationToken ct = default);

    /// <summary>
    /// Số liên kết thanh toán còn sống của từng khoản (Phase 92, BR-INS-03). Các hàm đọc khoản ở trên đã tự điền <see cref="RecurringItem.LivePaymentCount"/>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountLivePaymentsAsync(Guid userId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);

    /// <summary>Giao dịch đang gắn một khoản định kỳ (liên kết còn sống) của user — tracker không khớp lại chúng.</summary>
    Task<IReadOnlySet<Guid>> LinkedTransactionIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Theo dõi một liên kết mới; lưu cùng <see cref="SaveChangesAsync"/>.</summary>
    void AddPayment(RecurringItemPayment payment);

    /// <summary>Một liên kết của khoản này, kể cả đã gỡ (để DELETE gọi lại vẫn 204); có theo dõi thay đổi.</summary>
    Task<RecurringItemPayment?> GetPaymentAsync(Guid userId, Guid itemId, Guid paymentId, CancellationToken ct = default);

    /// <summary>Các liên kết còn sống của khoản, mới nhất trước.</summary>
    Task<IReadOnlyList<RecurringItemPayment>> ListPaymentsAsync(Guid userId, Guid itemId, CancellationToken ct = default);

    /// <summary>Liên kết còn sống của giao dịch này (với bất kỳ khoản nào của user); có theo dõi thay đổi.</summary>
    Task<RecurringItemPayment?> FindLivePaymentByTransactionAsync(Guid userId, Guid transactionId, CancellationToken ct = default);

    /// <summary>
    /// Chạy <paramref name="work"/> trong một DB transaction giữ khoá tư vấn theo user (Phase 92, S3): job đêm, job theo user và thao tác tay
    /// trên kỳ đã trả xếp hàng, không cùng ghi liên kết. Đang ở trong một transaction khác thì dùng luôn nó.
    /// </summary>
    Task<T> RunWithUserLockedAsync<T>(Guid userId, Func<Task<T>> work, CancellationToken ct = default);
    Task AddAsync(RecurringItem item, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<RecurringItem> items, CancellationToken ct = default);
    Task UpdateAsync(RecurringItem item, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
