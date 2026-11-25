using FinMate.Domain.Entities;

namespace FinMate.Application.Forecasting;

/// <summary>Phase 90 (D13): khoản vay không có hạn, không vào dự báo; chỉ đếm để app nhắc đặt hạn.</summary>
public sealed record UndatedLoansDto(int Count, long IOweCents, long OwedToMeCents);

/// <summary>Phase 90 (S5): số giao dịch <c>loan</c> gần đây chưa gắn khoản vay. Không tên, không số tiền.</summary>
public sealed record UnlinkedLoanTransactionsDto(int Count);

/// <summary>Khoản đi vay đến hạn trong kỳ tính "có thể chi" (BR-DEBT-17). Ngày là hạn thật (khoản quá hạn giữ hạn cũ để app hiện "quá hạn").</summary>
public sealed record SpendableLoanDto(Guid LoanId, DateOnly DueDate, string Label, long AmountCents);

/// <summary>
/// Đưa sổ vay vào dự báo (Phase 90, BR-DEBT-15, 16, 17; chép từ <c>DebtForecast</c> của Phase 86 cũ). Hàm thuần trên các khoản <c>open</c> còn gốc:
/// <list type="bullet">
/// <item>Đi vay có hạn trong kỳ → tiền ra đúng ngày hạn, ở cả ba đường. Quá hạn mà chưa trả → tính vào ngày mai (không quá ngày cuối kỳ).</item>
/// <item>Cho vay có hạn trong kỳ → tiền vào CHỈ ở đường lạc quan (người ta có thể không trả). Quá hạn → không tính.</item>
/// <item>Không có hạn → không vào dự báo, chỉ đếm (<see cref="Undated"/>).</item>
/// </list>
/// Số tiền là GỐC còn lại hôm nay, không cộng lãi tạm tính (Q11). Nhãn là chữ cố định, không tên người hay tiêu đề khoản: dự báo còn đi tới Agent
/// và LLM; app tra khoản bằng <c>loanId</c>. Không chống trùng với khoản định kỳ người dùng tự tạo cho cùng lần trả (D11, ghi trong tài liệu API).
/// </summary>
public static class LoanForecast
{
    public const string OutflowLabel = "Trả khoản vay";
    public const string InflowLabel = "Thu khoản cho vay";

    public static IReadOnlyList<CashflowEvent> Events(IEnumerable<Loan> loans, DateOnly today, DateOnly end)
    {
        var events = new List<CashflowEvent>();
        foreach (var loan in Live(loans))
        {
            if (loan.DueDate is not { } due || due > end)
            {
                continue;
            }

            if (loan.Direction == LoanDirection.Borrow)
            {
                var date = due >= today ? due : Min(today.AddDays(1), end);
                events.Add(new CashflowEvent(date, -Remaining(loan), OutflowLabel, CashflowEventKinds.LoanOutflow, null, CashflowBand.All, loan.Id));
            }
            else if (due >= today)
            {
                events.Add(new CashflowEvent(due, Remaining(loan), InflowLabel, CashflowEventKinds.LoanInflow, null, CashflowBand.OptimisticOnly, loan.Id));
            }
        }

        return events;
    }

    /// <summary>BR-DEBT-17: khoản đi vay có hạn tới hết <paramref name="end"/>, kể cả đã quá hạn (vẫn phải trả).</summary>
    public static IReadOnlyList<SpendableLoanDto> DueForSpendable(IEnumerable<Loan> loans, DateOnly end)
        => Live(loans)
            .Where(l => l.Direction == LoanDirection.Borrow && l.DueDate is { } due && due <= end)
            .OrderBy(l => l.DueDate)
            .Select(l => new SpendableLoanDto(l.Id, l.DueDate!.Value, OutflowLabel, Remaining(l)))
            .ToList();

    public static UndatedLoansDto? Undated(IEnumerable<Loan> loans)
    {
        var undated = Live(loans).Where(l => l.DueDate is null).ToList();
        return undated.Count == 0
            ? null
            : new UndatedLoansDto(
                undated.Count,
                undated.Where(l => l.Direction == LoanDirection.Borrow).Sum(Remaining),
                undated.Where(l => l.Direction == LoanDirection.Lend).Sum(Remaining));
    }

    private static long Remaining(Loan loan) => loan.PrincipalCents - loan.RepaidCents;

    private static IEnumerable<Loan> Live(IEnumerable<Loan> loans)
        => loans.Where(l => l.Status == LoanStatus.Open && l.DeletedAt is null && Remaining(l) > 0);

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
