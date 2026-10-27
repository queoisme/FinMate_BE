namespace FinMate.Application.Common.Interfaces;

/// <summary>Các tổng hợp theo tháng cho "tiền khả dụng". Luôn lọc theo <c>userId</c>.</summary>
public interface IFinanceSnapshotRepository
{
    /// <summary>
    /// Tổng chi (giao dịch ĐÃ XÁC NHẬN, loại <c>debit</c>) trong [<paramref name="start"/>, <paramref name="end"/>). Không tính chuyển khoản nội bộ
    /// (ARCHITECTURE.md §4.3: <c>transfer</c> luôn bị loại khỏi mọi phép cộng dồn) và không tính bản nháp chưa duyệt.
    /// </summary>
    Task<long> GetDebitTotalAsync(Guid userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);

    /// <summary>Tổng tiền đã nạp vào từng mục tiêu trong [<paramref name="start"/>, <paramref name="end"/>), khóa theo id mục tiêu.</summary>
    Task<Dictionary<Guid, long>> GetGoalDepositsAsync(Guid userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
}
