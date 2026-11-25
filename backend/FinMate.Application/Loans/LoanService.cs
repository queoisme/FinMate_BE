using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Commands;
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
        var paid = l.Repayments.Sum(r => r.AmountCents);
        return new(l.Id, l.Direction.ToString().ToLowerInvariant(), l.Title, l.Counterparty, l.PrincipalCents, l.InterestRatePercentYear,
            l.DueDate, paid, Math.Max(0, l.PrincipalCents - paid), l.CreatedAt, l.StartDate, LoanInterest.Accrued(l, today));
    }
}

/// <param name="StartDate">Ngày bắt đầu vay (mốc tính lãi); null = hôm nay (hoặc giữ nguyên khi sửa).</param>
public record LoanInput(LoanDirection Direction, string Title, string Counterparty, long PrincipalCents, decimal InterestRatePercentYear, DateOnly? DueDate, DateOnly? StartDate = null);

/// <summary>
/// Lãi tạm tính: lãi đơn theo ngày trên dư nợ GỐC, từ ngày bắt đầu đến hôm nay; mỗi lần trả làm giảm gốc (coi tiền trả vào gốc) từ ngày trả.
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
        var outstanding = (decimal)loan.PrincipalCents;
        var cursor = loan.StartDate;
        decimal interest = 0;
        foreach (var r in loan.Repayments.OrderBy(r => r.PaidAt))
        {
            var paidDay = DateOnly.FromDateTime(r.PaidAt.UtcDateTime.AddHours(7));
            if (paidDay > today)
            {
                break;
            }

            if (paidDay > cursor)
            {
                interest += outstanding * dailyRate * (paidDay.DayNumber - cursor.DayNumber);
                cursor = paidDay;
            }

            outstanding = Math.Max(0, outstanding - r.AmountCents);
        }

        if (today > cursor)
        {
            interest += outstanding * dailyRate * (today.DayNumber - cursor.DayNumber);
        }

        return (long)Math.Floor(interest);
    }
}

public interface ILoanService
{
    Task<IReadOnlyList<LoanDto>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<LoanDto> CreateAsync(Guid userId, LoanInput input, CancellationToken ct = default);
    Task<LoanDto> UpdateAsync(Guid userId, Guid id, LoanInput input, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <param name="financialAccountId">Có thì sinh giao dịch ví (vay: trả = chi; cho vay: thu hồi = thu) trong danh mục "Cho vay / Mượn"; null thì chỉ ghi sổ.</param>
    Task<LoanDto> RepayAsync(Guid userId, Guid id, long amountCents, Guid? financialAccountId, DateTimeOffset? paidAt, CancellationToken ct = default);
}

public class LoanService(
    ILoanRepository loans,
    ICategoryRepository categories,
    ICreateManualTransactionCommandHandler transactions) : ILoanService
{
    public const long MaxCents = 100_000_000_000L;

    public async Task<IReadOnlyList<LoanDto>> ListAsync(Guid userId, CancellationToken ct = default)
        => (await loans.GetListAsync(userId, ct)).Select(l => LoanDto.From(l, Today())).ToList();

    public async Task<LoanDto> CreateAsync(Guid userId, LoanInput input, CancellationToken ct = default)
    {
        Validate(input);
        var now = DateTimeOffset.UtcNow;
        var loan = new Loan { Id = Guid.NewGuid(), UserId = userId, CreatedAt = now, UpdatedAt = now };
        Apply(loan, input);
        await loans.AddAsync(loan, ct);
        return LoanDto.From(loan, Today());
    }

    public async Task<LoanDto> UpdateAsync(Guid userId, Guid id, LoanInput input, CancellationToken ct = default)
    {
        Validate(input);
        var loan = await loans.GetOwnedAsync(id, userId, ct) ?? throw new NotFoundException("Loan", id);
        if (input.PrincipalCents < loan.Repayments.Sum(r => r.AmountCents))
        {
            throw new BusinessRuleException(LoanErrorCodes.PrincipalBelowPaid, "Số tiền gốc không được nhỏ hơn số đã trả.");
        }

        if (loan.DueDate != input.DueDate)
        {
            loan.DueNotifiedAt = null;   // hạn mới thì được nhắc lại
        }

        Apply(loan, input);
        loan.UpdatedAt = DateTimeOffset.UtcNow;
        await loans.UpdateAsync(loan, ct);
        return LoanDto.From(loan, Today());
    }

    /// <summary>Xóa khoản vay và các lần trả trong sổ. Giao dịch ví đã sinh ra giữ nguyên (đó là lịch sử tiền thật).</summary>
    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var loan = await loans.GetOwnedAsync(id, userId, ct) ?? throw new NotFoundException("Loan", id);
        await loans.DeleteAsync(loan, ct);
    }

    public async Task<LoanDto> RepayAsync(Guid userId, Guid id, long amountCents, Guid? financialAccountId, DateTimeOffset? paidAt, CancellationToken ct = default)
    {
        var loan = await loans.GetOwnedAsync(id, userId, ct) ?? throw new NotFoundException("Loan", id);
        var remaining = loan.PrincipalCents - loan.Repayments.Sum(r => r.AmountCents);
        if (amountCents <= 0 || amountCents > MaxCents)
        {
            throw new BusinessRuleException(LoanErrorCodes.Invalid, "Số tiền không hợp lệ.");
        }

        if (amountCents > remaining)
        {
            throw new BusinessRuleException(LoanErrorCodes.Overpay, "Số tiền vượt quá số còn lại của khoản này.");
        }

        // Ghi lần trả TRƯỚC, kèm khóa xmin của khoản vay: hai lần trả đồng thời thì một bên nhận 409 (CONCURRENT_UPDATE), không sinh giao dịch ví thừa.
        var now = DateTimeOffset.UtcNow;
        var repayment = new LoanRepayment { Id = Guid.NewGuid(), LoanId = loan.Id, AmountCents = amountCents, PaidAt = paidAt ?? now, CreatedAt = now };
        await loans.AddRepaymentAsync(loan, repayment, ct);
        loan.Repayments.Add(repayment);

        if (financialAccountId is { } accountId)
        {
            try
            {
                var loanCategory = await categories.GetSystemBySlugAsync(Category.LoanSlug, ct);
                var borrow = loan.Direction == LoanDirection.Borrow;
                var result = await transactions.HandleAsync(new CreateManualTransactionCommand(
                    userId, accountId, loanCategory?.Id, amountCents,
                    borrow ? TransactionType.Debit : TransactionType.Credit,
                    repayment.PaidAt, loan.Counterparty,
                    borrow ? $"Trả nợ: {loan.Title}" : $"Thu hồi: {loan.Title}",
                    ClientRequestId: repayment.Id), ct);
                repayment.TransactionId = result.Transaction.Id;
                await loans.SaveAsync(ct);
            }
            catch
            {
                // Ví lỗi (không có ví, không hợp lệ...) thì bỏ lần trả vừa ghi: sổ và ví không được lệch nhau.
                loan.Repayments.Remove(repayment);
                await loans.RemoveRepaymentAsync(loan, repayment, CancellationToken.None);
                throw;
            }
        }

        return LoanDto.From(loan, Today());
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));   // ngày theo giờ VN

    private static void Validate(LoanInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Title) || i.Title.Trim().Length > 100
            || string.IsNullOrWhiteSpace(i.Counterparty) || i.Counterparty.Trim().Length > 100)
        {
            throw new BusinessRuleException(LoanErrorCodes.Invalid, "Tên khoản vay và đối tác là bắt buộc (tối đa 100 ký tự).");
        }

        if (i.PrincipalCents <= 0 || i.PrincipalCents > MaxCents)
        {
            throw new BusinessRuleException(LoanErrorCodes.Invalid, "Số tiền gốc phải lớn hơn 0 và không quá 100 tỷ đồng.");
        }

        if (i.InterestRatePercentYear is < 0 or > 100)
        {
            throw new BusinessRuleException(LoanErrorCodes.Invalid, "Lãi suất phải từ 0 đến 100 %/năm.");
        }
    }

    private static void Apply(Loan loan, LoanInput i)
    {
        loan.Direction = i.Direction;
        loan.Title = i.Title.Trim();
        loan.Counterparty = i.Counterparty.Trim();
        loan.PrincipalCents = i.PrincipalCents;
        loan.InterestRatePercentYear = i.InterestRatePercentYear;
        loan.DueDate = i.DueDate;
        loan.StartDate = i.StartDate ?? (loan.StartDate == default ? Today() : loan.StartDate);
    }
}
