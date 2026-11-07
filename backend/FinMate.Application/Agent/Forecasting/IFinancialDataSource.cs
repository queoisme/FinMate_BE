namespace FinMate.Application.Agent;

/// <summary>Thu và chi theo tháng, hai danh sách cùng độ dài, cũ nhất trước. Chỉ gồm các tháng đã trọn, bắt đầu từ tháng đầu tiên có hoạt động.</summary>
public sealed record MonthlySeries(IReadOnlyList<long> IncomeVnd, IReadOnlyList<long> SpendVnd);

/// <summary>
/// NGUỒN DỮ LIỆU DUY NHẤT mà mọi công cụ agent đọc. Bản thật dựa trên repository (luôn lọc theo <c>userId</c>); bản trong bộ nhớ dùng cho bộ đánh giá agent_eval
/// và test, nên công cụ đọc được dữ liệu giả lập mà không phải sửa code.
/// </summary>
public interface IFinancialDataSource
{
    Task<MonthlySeries> GetMonthlySeriesAsync(Guid userId, int months, DateTimeOffset now, CancellationToken ct = default);

    Task<long> GetTotalBalanceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Nhóm thu nhập người dùng tự chọn; null = chưa chọn hoặc giá trị không hợp lệ (để hệ thống tự suy).</summary>
    Task<IncomeSegment?> GetIncomeSegmentOverrideAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Khoản thu/chi định kỳ: đã xác nhận (nguồn <c>Confirmed</c>) cùng ứng viên phát hiện từ lịch sử nhưng chưa xác nhận (nguồn <c>Estimated</c>). Mặc định không có khoản nào.</summary>
    Task<IReadOnlyList<RecurringEntry>> GetRecurringAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RecurringEntry>>([]);
}

/// <param name="PaidThisMonth">Đã có giao dịch thật trả/nhận khoản này trong THÁNG HIỆN TẠI: số dư đã phản ánh nên sổ dòng tiền không tính lần nữa.</param>
public sealed record RecurringEntry(string Name, long AmountVnd, FinMate.Domain.Entities.RecurringKind Kind, int DayOfMonth, EvidenceSource Source, bool PaidThisMonth);
