namespace FinMate.Application.Agent;

/// <summary>Thu và chi theo tháng, hai danh sách cùng độ dài, cũ nhất trước. Chỉ gồm các tháng đã trọn, bắt đầu từ tháng đầu tiên có hoạt động.</summary>
public sealed record MonthlySeries(IReadOnlyList<long> IncomeVnd, IReadOnlyList<long> SpendVnd);

/// <summary>Chi tiêu một danh mục trong một khoảng ngày. Danh mục trống được gom vào slug <c>uncategorized</c>.</summary>
public sealed record CategorySpendVnd(string Slug, string Name, long SpentVnd);

/// <summary>Phase 90 (D14): một khoản vay <c>open</c>, KHÔNG có tên người hay tiêu đề khoản (kết quả công cụ đi tới LLM bên ngoài). Số là gốc còn lại (Q11).</summary>
public sealed record LoanVnd(Domain.Entities.LoanDirection Direction, long RemainingVnd, DateOnly? DueDate);

/// <summary>
/// NGUỒN DỮ LIỆU DUY NHẤT mà mọi công cụ agent đọc. Bản thật dựa trên repository (luôn lọc theo <c>userId</c>); bản trong bộ nhớ dùng cho bộ đánh giá agent_eval
/// và test, nên công cụ đọc được dữ liệu giả lập mà không phải sửa code.
/// </summary>
public interface IFinancialDataSource
{
    Task<MonthlySeries> GetMonthlySeriesAsync(Guid userId, int months, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Chi theo danh mục trong [from, to] (ngày giờ VN, gồm cả hai đầu), sắp theo số tiền giảm dần; mỗi slug chỉ xuất hiện một lần.</summary>
    Task<IReadOnlyList<CategorySpendVnd>> GetCategorySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<long> GetTotalBalanceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Nhóm thu nhập người dùng tự chọn; null = chưa chọn hoặc giá trị không hợp lệ (để hệ thống tự suy).</summary>
    Task<IncomeSegment?> GetIncomeSegmentOverrideAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Phase 90: khoản vay <c>open</c>, không tên người. Mặc định rỗng: nguồn giả lập cũ không cần biết tới sổ vay.</summary>
    Task<IReadOnlyList<LoanVnd>> GetOpenLoansAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LoanVnd>>([]);
}
