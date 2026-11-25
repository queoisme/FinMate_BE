using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Loans;

/// <summary>
/// Một khoản vay gợi ý cho giao dịch (BR-DEBT-13). <paramref name="NameMatched"/>: tên người có trong nội dung giao dịch (D8, Q15).
/// <paramref name="InterestCents"/>: phần vượt gốc còn lại sẽ thành giao dịch lãi nếu gắn (S1); 0 khi không vượt.
/// </summary>
public sealed record LoanSuggestionDto(
    Guid LoanId, string Title, string Counterparty, string Direction, long RemainingCents, DateOnly? DueDate, bool NameMatched, long InterestCents);

/// <param name="Text">Nơi chi và mô tả của giao dịch (hoặc câu chat). Không dùng <c>notification_body</c> (D8).</param>
/// <param name="Hint">Gợi ý trả nợ của AI và độ mạnh của nó; null khi không có.</param>
public sealed record LoanSuggestionInput(TransactionType Type, long AmountCents, string? Text, DebtHintStrength? Hint);

/// <summary>Tham số của bộ xếp hạng, lấy từ nhóm cấu hình <c>loans</c>.</summary>
public sealed record LoanSuggestionRules(DateOnly Today, long InterestToleranceCents, IReadOnlyCollection<string> Honorifics, int Max);

/// <summary>
/// Xếp hạng khoản vay cho một giao dịch tiền vào/ra (Phase 90, BR-DEBT-13, D8, S1, S3, Q15). Hàm thuần. Ứng viên: khoản <c>open</c>, đúng chiều TRẢ,
/// số tiền không vượt số còn lại, hoặc vượt trong ngưỡng lãi khi khoản có lãi suất (S1). Chỉ gợi ý khi tên người khớp HOẶC có gợi ý MẠNH của AI.
/// Nhiều khoản cùng khớp thì trả tất cả (Q15). Xếp: tên khớp &gt; có gợi ý &gt; trả hết khoản &gt; hạn gần nhất (không hạn xếp cuối) &gt; mở sớm nhất.
/// </summary>
public static class LoanSuggestionRanker
{
    public static IReadOnlyList<LoanSuggestionDto> Rank(LoanSuggestionInput input, IEnumerable<Loan> openLoans, LoanSuggestionRules rules)
    {
        if (input.Type is not (TransactionType.Debit or TransactionType.Credit) || input.AmountCents <= 0)
        {
            return [];
        }

        var strong = input.Hint == DebtHintStrength.Strong;
        return openLoans
            .Where(l => l.Status == LoanStatus.Open && l.DeletedAt is null)
            .Where(l => LoanPolicy.TransactionTypeFor(l.Direction, LoanEntryKind.Repayment) == input.Type)
            .Select(l => (Loan: l, Remaining: l.PrincipalCents - l.RepaidCents))
            .Where(c => c.Remaining > 0 && Fits(c.Loan, c.Remaining, input.AmountCents, rules))
            .Select(c => (c.Loan, c.Remaining, Name: PersonNameMatch.InText(c.Loan.Counterparty, input.Text, rules.Honorifics)))
            .Where(c => c.Name || strong)
            .OrderByDescending(c => c.Name)
            .ThenByDescending(c => input.Hint is not null)
            .ThenByDescending(c => input.AmountCents >= c.Remaining)
            .ThenBy(c => c.Loan.DueDate ?? DateOnly.MaxValue)
            .ThenBy(c => c.Loan.StartDate)
            .ThenBy(c => c.Loan.CreatedAt)
            .Take(rules.Max)
            .Select(c => new LoanSuggestionDto(
                c.Loan.Id, c.Loan.Title, c.Loan.Counterparty, c.Loan.Direction.ToString().ToLowerInvariant(), c.Remaining, c.Loan.DueDate, c.Name,
                Math.Max(0, input.AmountCents - c.Remaining)))
            .ToList();
    }

    private static bool Fits(Loan loan, long remaining, long amount, LoanSuggestionRules rules)
        => amount <= remaining
           || (loan.InterestRatePercentYear > 0 && amount - remaining <= LoanInterest.Accrued(loan, rules.Today) + rules.InterestToleranceCents);
}
