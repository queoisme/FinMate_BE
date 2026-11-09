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
}
