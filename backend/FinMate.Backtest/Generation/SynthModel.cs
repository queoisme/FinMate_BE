using FinMate.Application.Common;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Generation;

/// <summary>Nhãn đáp án của một giao dịch (đặc tả §7). Chỉ để chấm điểm, không bao giờ đi vào mô hình.</summary>
public enum TxLabel
{
    Flexible,
    Recurring,
    RecurringVolatile,
    IncomeIrregular,
    Transfer,
    Loan,
    Refund,
    Fund,
    Scenario,
}

public sealed record SynthAccount(Guid Id, string Name, bool IncludeInForecast, long OpeningBalanceCents);

/// <summary>
/// Một giao dịch tổng hợp, mang đúng những trường production lưu (đặc tả §4) cộng nhãn đáp án. <see cref="Type"/> là <c>Transfer</c> cho chuyển ví
/// (có <see cref="CounterAccountId"/>) và góp quỹ nhóm (không có ví đối ứng, <see cref="FundName"/> khác null, luôn là tiền ra).
/// </summary>
public sealed record SynthTransaction(
    Guid Id,
    Guid AccountId,
    TransactionType Type,
    long AmountCents,
    DateTimeOffset At,
    string? Merchant,
    string? CategorySlug,
    TxLabel Label,
    Guid? CounterAccountId = null,
    string? FundName = null,
    TransactionRelation? Relation = null,
    Guid? RelatedTransactionId = null,
    string? RecurringName = null,
    bool InjectedAnomaly = false)
{
    public DateOnly Date => VietnamTime.DateOf(At);
}

/// <summary>Một kỳ của khoản định kỳ thật: ngày đến hạn danh nghĩa, số tiền danh nghĩa, và thanh toán (nếu có).</summary>
public sealed record TruthOccurrence(DateOnly Due, long NominalCents, Guid? TransactionId, DateOnly? PaidOn, long? PaidCents);

/// <summary>Khoản định kỳ thật (đặc tả §5.2, §7). <see cref="Volatile"/>: cờ "số tiền dao động", không có trong chế độ đáp án.</summary>
public sealed record TruthRecurring(
    string Name,
    RecurringDirection Direction,
    string CategorySlug,
    Guid AccountId,
    RecurringCadence Cadence,
    int? AnchorDay,
    bool Volatile,
    IReadOnlyList<TruthOccurrence> Occurrences);

public sealed record SynthLoanRepayment(Guid TransactionId, DateTimeOffset At, long AmountCents);

public sealed record SynthLoan(
    Guid Id,
    LoanDirection Direction,
    long PrincipalCents,
    DateOnly Start,
    DateOnly Due,
    IReadOnlyList<SynthLoanRepayment> Repayments);

public sealed record SynthBudget(string CategorySlug, long LimitCents);

/// <summary>
/// Một "thế giới": một người gốc dưới một kịch bản. <see cref="FirstDay"/> là ngày đầu có dữ liệu (K6: ngày bắt đầu dùng app).
/// <see cref="EventDays"/> là ngày sự kiện của kịch bản, để tính cửa sổ ảnh hưởng (đặc tả §6).
/// </summary>
public sealed record SynthWorld(
    string Persona,
    int SeedIndex,
    string Scenario,
    Guid UserId,
    DateOnly FirstDay,
    IReadOnlyList<SynthAccount> Accounts,
    IReadOnlyList<SynthTransaction> Transactions,
    IReadOnlyList<TruthRecurring> Recurring,
    IReadOnlyList<SynthLoan> Loans,
    IReadOnlyList<SynthBudget> Budgets,
    long MonthlyIncomeCents,
    long LowBalanceThresholdCents,
    IReadOnlyList<DateOnly> EventDays);
