using FinMate.Domain.Enums;

namespace FinMate.Backtest.Generation;

public static class AccountKeys
{
    public const string Bank = "bank";
    public const string Cash = "cash";
    public const string EWallet = "ewallet";
    public const string Savings = "savings";
}

/// <summary>Cách tính số tiền mỗi kỳ của một khoản định kỳ (đặc tả §5.2, §5.3).</summary>
public enum AmountModel
{
    Fixed,
    Electricity,
    Water,

    /// <summary>Gốc × U[0,8; 1,2] (làm thêm của sinh viên).</summary>
    Volatile20,

    /// <summary>Gốc × U[0,85; 1,15] (nhập hàng của tiểu thương).</summary>
    Volatile15,
}

/// <summary>Độ lệch ngày trả so với ngày đến hạn (§5.2).</summary>
public enum JitterModel
{
    /// <summary>{−2, …, +3} ngày.</summary>
    Bill,

    /// <summary>Không lệch; T7, CN dời về T6 trước đó.</summary>
    Salary,

    /// <summary>{−3, …, +3} ngày (trợ cấp sinh viên).</summary>
    Allowance,

    /// <summary>Đúng ngày.</summary>
    None,
}

/// <summary>Định nghĩa một khoản định kỳ thật. <see cref="Label"/> là <c>Recurring</c>, <c>RecurringVolatile</c>, <c>Transfer</c> (sang ví bị loại) hoặc <c>Fund</c>.</summary>
public sealed record RecurringDef(
    string Name,
    RecurringDirection Direction,
    string CategorySlug,
    string AccountKey,
    RecurringCadence Cadence,
    int AnchorDay,
    DayOfWeek Weekday,
    long BaseCents,
    AmountModel Amount,
    JitterModel Jitter,
    TxLabel Label,
    string? CounterAccountKey = null,
    string? FundName = null)
{
    public bool Volatile => Label == TxLabel.RecurringVolatile;
}

/// <summary>Chuyển giữa hai ví cùng được tính, theo ngày trong tháng (rút tiền mặt, nạp ví điện tử). Không phải khoản định kỳ của đáp án.</summary>
public sealed record OwnTransferDef(string Name, IReadOnlyList<int> Days, long MinCents, long MaxCents, string From, string To);

public sealed record FlexCategory(string Slug, double Share, double Lambda, double Sigma, IReadOnlyList<string> Merchants);

public sealed record AccountDef(string Key, string Name, bool IncludeInForecast, long OpeningCents);

/// <summary>Tham số của một người gốc, rút từ luồng <c>base.params</c> theo thứ tự cố định, không phụ thuộc kịch bản.</summary>
public sealed record PersonaProfile(
    string Persona,
    IReadOnlyList<AccountDef> Accounts,
    long FlexMonthlyCents,
    IReadOnlyList<FlexCategory> Flex,
    double CashShareOfFood,
    double PaydayBoost,
    double MonthlyDrift,
    IReadOnlyList<RecurringDef> Recurring,
    IReadOnlyList<OwnTransferDef> OwnTransfers,
    long MonthlyIncomeCents,
    bool HasBudgets,
    IReadOnlyList<SynthBudget> Budgets)
{
    /// <summary>Freelancer: thu nhập danh nghĩa tháng M.</summary>
    public long ProjectIncomeMonthlyCents { get; init; }

    /// <summary>Tiểu thương: doanh thu danh nghĩa ngày R, ngày nghỉ trong tuần, tỉ lệ nhập hàng.</summary>
    public long DailyRevenueCents { get; init; }

    public DayOfWeek ClosedDay { get; init; }

    /// <summary>Sinh viên: hai khoản học phí (ngày, số tiền).</summary>
    public IReadOnlyList<(DateOnly Date, long Cents)> Tuition { get; init; } = [];

    /// <summary>Gia đình: đi vay và cho vay (§5.3).</summary>
    public long BorrowCents { get; init; }

    public long LendCents { get; init; }
}
