using FinMate.Application.Common;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Loans;

public record LoanDto(
    Guid Id, string Direction, string Title, string Counterparty, long PrincipalCents, decimal InterestRatePercentYear,
    DateOnly? DueDate, long PaidCents, long RemainingCents, DateTimeOffset CreatedAt,
    DateOnly StartDate = default, long AccruedInterestCents = 0)
{
    /// <summary>Số phải trả/thu tính đến hôm nay = gốc còn lại + lãi tạm tính.</summary>
    public long TotalDueCents => RemainingCents + AccruedInterestCents;

    public static LoanDto From(Loan l, DateOnly today)
    {
        var paid = LoanTotals.Repaid(l);
        return new(l.Id, l.Direction.ToString().ToLowerInvariant(), l.Title, l.Counterparty, l.PrincipalCents, l.InterestRatePercentYear,
            l.DueDate, paid, Math.Max(0, l.PrincipalCents - paid), l.CreatedAt, l.StartDate, LoanInterest.Accrued(l, today));
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
