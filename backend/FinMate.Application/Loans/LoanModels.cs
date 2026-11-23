using FinMate.Application.Common;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Loans;

/// <summary>
/// Một khoản vay. Trường trước Phase 90 giữ nguyên tên và nghĩa (app đang đọc, xem mục S5 của kế hoạch); Phase 90 thêm ở cuối: <paramref name="Status"/>
/// (<c>open</c> | <c>settled</c> | <c>written_off</c>), <paramref name="IsOverdue"/>, <paramref name="Note"/>, mốc trả xong / xoá nợ.
/// </summary>
public record LoanDto(
    Guid Id, string Direction, string Title, string Counterparty, long PrincipalCents, decimal InterestRatePercentYear,
    DateOnly? DueDate, long PaidCents, long RemainingCents, DateTimeOffset CreatedAt,
    DateOnly StartDate = default, long AccruedInterestCents = 0,
    string Status = "open", bool IsOverdue = false, string? Note = null, DateTimeOffset? SettledAt = null, DateTimeOffset? WrittenOffAt = null)
{
    /// <summary>Số phải trả/thu tính đến hôm nay = gốc còn lại + lãi tạm tính.</summary>
    public long TotalDueCents => RemainingCents + AccruedInterestCents;

    public static LoanDto From(Loan l, DateOnly today)
    {
        var paid = LoanTotals.Repaid(l);
        var remaining = Math.Max(0, l.PrincipalCents - paid);
        // Q11: trả hết gốc là xong; lãi tạm tính của khoản đã đóng không còn ý nghĩa.
        var accrued = l.Status == LoanStatus.Open ? LoanInterest.Accrued(l, today) : 0;
        return new(l.Id, l.Direction.ToString().ToLowerInvariant(), l.Title, l.Counterparty, l.PrincipalCents, l.InterestRatePercentYear,
            l.DueDate, paid, remaining, l.CreatedAt, l.StartDate, accrued,
            StatusName(l.Status), IsOverdueOn(l, today), l.Note, l.SettledAt, l.WrittenOffAt);
    }

    public static string StatusName(LoanStatus status) => status switch
    {
        LoanStatus.Settled => "settled",
        LoanStatus.WrittenOff => "written_off",
        _ => "open",
    };

    /// <summary>Khoản <c>open</c> có hạn trước hôm nay (giờ VN).</summary>
    public static bool IsOverdueOn(Loan l, DateOnly today) => l.Status == LoanStatus.Open && l.DueDate is { } due && due < today;
}

/// <summary>Một dòng của khoản: giải ngân (<c>principal</c>) hoặc trả (<c>repayment</c>); <paramref name="InterestTransactionId"/>: phần lãi tách ra (S1).</summary>
public sealed record LoanEntryDto(Guid Id, string Kind, long AmountCents, DateTimeOffset At, Guid? TransactionId, Guid? InterestTransactionId);

public sealed record LoanDetailDto(LoanDto Loan, IReadOnlyList<LoanEntryDto> Entries)
{
    public static LoanDetailDto From(Loan l, DateOnly today) => new(
        LoanDto.From(l, today),
        l.Repayments.Where(e => e.DeletedAt is null).OrderBy(e => e.PaidAt).ThenBy(e => e.CreatedAt)
            .Select(e => new LoanEntryDto(e.Id, e.Kind == LoanEntryKind.Principal ? "principal" : "repayment", e.AmountCents, e.PaidAt, e.TransactionId, e.InterestTransactionId))
            .ToList());
}

/// <summary>D17: tổng gốc còn lại của các khoản <c>open</c>; quá hạn là khoản <c>open</c> có hạn trước hôm nay (giờ VN). Không cộng lãi (Q11).</summary>
public sealed record LoanSummaryDto(long OwedToMeCents, long IOweCents, int OverdueCount, long OverdueOwedToMeCents, long OverdueIOweCents)
{
    public static LoanSummaryDto From(IEnumerable<Loan> loans, DateOnly today)
    {
        var open = loans.Where(l => l.Status == LoanStatus.Open && l.DeletedAt is null).ToList();
        var overdue = open.Where(l => LoanDto.IsOverdueOn(l, today)).ToList();
        static long Left(Loan l) => Math.Max(0, l.PrincipalCents - l.RepaidCents);
        return new LoanSummaryDto(
            open.Where(l => l.Direction == LoanDirection.Lend).Sum(Left),
            open.Where(l => l.Direction == LoanDirection.Borrow).Sum(Left),
            overdue.Count,
            overdue.Where(l => l.Direction == LoanDirection.Lend).Sum(Left),
            overdue.Where(l => l.Direction == LoanDirection.Borrow).Sum(Left));
    }
}

/// <summary>
/// Tổng của một khoản tính từ các dòng còn sống (Phase 90): gốc = Σ giải ngân, đã trả = Σ trả. <see cref="Recompute"/> ghi lại hai tổng lưu sẵn và
/// trạng thái; mọi chỗ thêm, sửa, xoá dòng phải gọi nó trước khi lưu (CHECK <c>principal_cents &gt;= repaid_cents</c> ở DB là lưới cuối).
/// </summary>
public static class LoanTotals
{
    public static long Repaid(Loan l) => l.Live(LoanEntryKind.Repayment).Sum(r => r.AmountCents);

    /// <summary>Khoản dựng trong bộ nhớ chưa có dòng giải ngân (dữ liệu trước Phase 90 trong test) thì gốc là số đã lưu.</summary>
    public static long Principal(Loan l) => l.Live(LoanEntryKind.Principal).Any() ? l.Live(LoanEntryKind.Principal).Sum(r => r.AmountCents) : l.PrincipalCents;

    public static void Recompute(Loan l, DateTimeOffset now)
    {
        l.PrincipalCents = Principal(l);
        l.RepaidCents = Repaid(l);
        if (l.Status == LoanStatus.WrittenOff)
        {
            return;
        }

        var settled = l.PrincipalCents > 0 && l.RepaidCents >= l.PrincipalCents;
        l.Status = settled ? LoanStatus.Settled : LoanStatus.Open;
        l.SettledAt = settled ? l.SettledAt ?? now : null;
    }
}

/// <param name="StartDate">Ngày bắt đầu vay (mốc tính lãi); null = hôm nay (hoặc giữ nguyên khi sửa).</param>
/// <param name="Note">Phase 90: ghi chú tuỳ chọn (tối đa 500 ký tự); khi sửa, null = giữ nguyên.</param>
/// <param name="ClientRequestId">Phase 90, D19: gửi lại cùng mã thì trả khoản đã tạo, không tạo thêm.</param>
/// <param name="CreateTransaction">Phase 90, Q6 (BR-DEBT-03 cách 1): tạo kèm giao dịch giải ngân từ một ví.</param>
/// <param name="TransactionId">Phase 90, Q6 (cách 2): giao dịch có sẵn thành dòng giải ngân đầu; gốc phải bằng số tiền giao dịch.</param>
public record LoanInput(
    LoanDirection Direction, string Title, string Counterparty, long PrincipalCents, decimal InterestRatePercentYear, DateOnly? DueDate, DateOnly? StartDate = null,
    string? Note = null, Guid? ClientRequestId = null, NewLoanTransaction? CreateTransaction = null, Guid? TransactionId = null);

/// <summary>Tạo khoản kèm giao dịch giải ngân mới từ ví (Q6).</summary>
public sealed record NewLoanTransaction(Guid FinancialAccountId, DateTimeOffset? TransactedAt = null);

/// <summary>Một lần trả hoặc giải ngân thêm. Có ví thì tạo giao dịch <c>loan</c> đã xác nhận; không ví thì chỉ ghi sổ.</summary>
public sealed record LoanEntryInput(long AmountCents, Guid? FinancialAccountId = null, DateTimeOffset? At = null, Guid? ClientRequestId = null);

/// <summary><paramref name="AlreadyExisted"/>: gửi lại cùng <c>clientRequestId</c> (D19), controller trả 200 thay vì 201.</summary>
public sealed record LoanWriteResult(LoanDto Loan, bool AlreadyExisted);

/// <summary>
/// Lãi tạm tính: lãi đơn theo ngày trên dư nợ GỐC, từ ngày bắt đầu đến hôm nay; mỗi lần trả làm giảm gốc (coi tiền trả vào gốc) từ ngày trả,
/// mỗi lần giải ngân thêm làm tăng gốc từ ngày đó (Phase 90). Dòng đã xoá không tính.
/// Chỉ để tham khảo: không phải số của ngân hàng (không lãi kép, không phí phạt, không kỳ trả cố định).
/// </summary>
public static class LoanInterest
{
    public static long Accrued(Loan loan, DateOnly today)
    {
        if (loan.InterestRatePercentYear <= 0)
        {
            return 0;
        }

        var dailyRate = loan.InterestRatePercentYear / 100m / 365m;
        var hasPrincipalRows = loan.Live(LoanEntryKind.Principal).Any();
        var outstanding = hasPrincipalRows ? 0m : loan.PrincipalCents;
        var cursor = loan.StartDate;
        decimal interest = 0;
        foreach (var r in loan.Repayments.Where(r => r.DeletedAt is null).OrderBy(r => r.PaidAt).ThenBy(r => r.Kind))
        {
            var paidDay = VietnamTime.DateOf(r.PaidAt);
            if (paidDay > today)
            {
                break;
            }

            if (paidDay > cursor)
            {
                interest += outstanding * dailyRate * (paidDay.DayNumber - cursor.DayNumber);
                cursor = paidDay;
            }

            outstanding = r.Kind == LoanEntryKind.Principal ? outstanding + r.AmountCents : Math.Max(0, outstanding - r.AmountCents);
        }

        if (today > cursor)
        {
            interest += outstanding * dailyRate * (today.DayNumber - cursor.DayNumber);
        }

        return (long)Math.Floor(interest);
    }
}
