using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FinMate.Backtest.Generation;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Simulation;

/// <summary>Đồng hồ giả của backtest. Repository giả chỉ thấy giao dịch có <c>TransactedAt &lt; Now</c> (S1).</summary>
public sealed class BacktestClock
{
    public DateTimeOffset Now { get; set; }

    /// <summary>Mốc dự báo T = 00:00 giờ VN của ngày T (D6), tương đương "chỉ thấy giao dịch có ngày &lt; T".</summary>
    public void SetOrigin(DateOnly origin) => Now = GeneratorSpec.At(origin, 0);
}

/// <summary>
/// Một thế giới tổng hợp nhìn qua mắt production tại thời điểm <see cref="BacktestClock.Now"/>: giao dịch đã đổi sang <see cref="ForecastTransaction"/>
/// đúng như <c>ForecastDataRepository</c> làm, số dư từng ví dựng lại từ số dư mở đầu, và trạng thái khoản vay. Không có gì sau <c>Now</c> lọt vào.
/// </summary>
public sealed class WorldView
{
    private readonly SynthTransaction[] _all;
    private readonly DateTimeOffset[] _times;
    private readonly ForecastTransaction?[] _raw;
    private readonly Dictionary<Guid, long[]> _prefixByAccount = new();
    private readonly HashSet<Guid> _linkedLoanTransactions;
    private readonly Dictionary<Guid, SynthTransaction> _byId;

    public WorldView(SynthWorld world, BacktestClock clock)
    {
        World = world;
        Clock = clock;
        _all = world.Transactions.ToArray();
        _times = _all.Select(t => t.At).ToArray();
        _raw = _all.Select(ToRaw).ToArray();
        foreach (var account in world.Accounts)
        {
            var prefix = new long[_all.Length + 1];
            for (var i = 0; i < _all.Length; i++)
            {
                prefix[i + 1] = prefix[i] + BalanceEffects.Of(_all[i], account.Id);
            }

            _prefixByAccount[account.Id] = prefix;
        }

        _linkedLoanTransactions = world.Loans.SelectMany(l => l.Repayments.Select(r => r.TransactionId).Append(l.OpeningTransactionId)).ToHashSet();
        _byId = world.Transactions.ToDictionary(t => t.Id);
    }

    public SynthWorld World { get; }

    public BacktestClock Clock { get; }

    public Guid UserId => World.UserId;

    /// <summary>Số giao dịch có <c>At &lt; Now</c> (giao dịch đã sắp theo thời điểm).</summary>
    public int VisibleCount
    {
        get
        {
            var index = Array.BinarySearch(_times, Clock.Now);
            if (index < 0)
            {
                return ~index;
            }

            while (index > 0 && _times[index - 1] == Clock.Now)
            {
                index--;
            }

            return index;
        }
    }

    public long BalanceOf(Guid accountId)
        => World.Accounts.Single(a => a.Id == accountId).OpeningBalanceCents + _prefixByAccount[accountId][VisibleCount];

    /// <summary>Như <c>GetRawTransactionsAsync</c>: đã xác nhận, không phải Transfer, ngày trong [from, to], và đã xảy ra trước <c>Now</c>.</summary>
    public IReadOnlyList<ForecastTransaction> Raw(DateOnly from, DateOnly to)
    {
        var visible = VisibleCount;
        var result = new List<ForecastTransaction>();
        for (var i = FirstIndexOn(from); i < visible && _all[i].Date <= to; i++)
        {
            if (_raw[i] is { } row)
            {
                result.Add(row);
            }
        }

        return result;
    }

    /// <summary>Như <c>GetBoundaryTransfersAsync</c>: chuyển ví có đúng một đầu trong tập được tính, và góp quỹ từ ví được tính.</summary>
    public IReadOnlyList<ForecastTransaction> Boundary(IReadOnlySet<Guid> counted, DateOnly from, DateOnly to)
    {
        var visible = VisibleCount;
        var result = new List<ForecastTransaction>();
        for (var i = FirstIndexOn(from); i < visible && _all[i].Date <= to; i++)
        {
            if (BoundaryRow(_all[i], counted) is { } row)
            {
                result.Add(row);
            }
        }

        return result;
    }

    /// <summary>Giao dịch <c>loan</c> đã thấy từ <paramref name="since"/> mà không gắn khoản vay nào trong sổ (S5 của Phase 90).</summary>
    public int CountUnlinkedLoanTransactions(DateTimeOffset since)
    {
        var visible = VisibleCount;
        var count = 0;
        for (var i = 0; i < visible; i++)
        {
            if (_all[i].Label == TxLabel.Loan && _all[i].At >= since && !_linkedLoanTransactions.Contains(_all[i].Id))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Khoản vay trong sổ tại <c>Now</c>: chỉ khoản đã mở, số đã trả là tổng các lần trả đã xảy ra.</summary>
    public IReadOnlyList<Loan> OpenLoans()
    {
        var loans = new List<Loan>();
        foreach (var l in World.Loans)
        {
            if (!_byId.TryGetValue(l.OpeningTransactionId, out var opening) || opening.At >= Clock.Now)
            {
                continue;
            }

            var repaid = l.Repayments.Where(r => r.At < Clock.Now).Sum(r => r.AmountCents);
            if (repaid >= l.PrincipalCents)
            {
                continue;
            }

            loans.Add(new Loan
            {
                Id = l.Id, UserId = UserId, Direction = l.Direction, Title = "backtest", Counterparty = "backtest", PrincipalCents = l.PrincipalCents,
                StartDate = l.Start, DueDate = l.Due, RepaidCents = repaid, Status = LoanStatus.Open, CreatedAt = opening.At, UpdatedAt = opening.At,
            });
        }

        return loans;
    }

    private int FirstIndexOn(DateOnly from)
    {
        var start = GeneratorSpec.At(from, 0);
        var index = Array.BinarySearch(_times, start);
        if (index < 0)
        {
            return ~index;
        }

        while (index > 0 && _times[index - 1] == start)
        {
            index--;
        }

        return index;
    }

    private static ForecastTransaction? ToRaw(SynthTransaction t)
        => t.Type == TransactionType.Transfer
            ? null
            : new ForecastTransaction(
                t.Id, t.AccountId, t.CategorySlug is null ? null : GeneratorSpec.CategoryId(t.CategorySlug), t.AmountCents, t.Type, t.At, t.Date, t.Merchant,
                CounterpartyKey.From(t.Merchant), false, t.Relation, t.RelatedTransactionId, false, null, t.CategorySlug == Category.LoanSlug);

    private ForecastTransaction? BoundaryRow(SynthTransaction t, IReadOnlySet<Guid> counted)
    {
        if (t.Type != TransactionType.Transfer)
        {
            return null;
        }

        if (t.CounterAccountId is not { } counter)
        {
            return counted.Contains(t.AccountId) ? Row(t, t.AccountId, TransactionType.Debit, t.FundName) : null;
        }

        var outgoing = counted.Contains(t.AccountId);
        if (outgoing == counted.Contains(counter))
        {
            return null;
        }

        var name = t.Merchant ?? NameOf(outgoing ? counter : t.AccountId);
        return outgoing ? Row(t, t.AccountId, TransactionType.Debit, name) : Row(t, counter, TransactionType.Credit, name);
    }

    private static ForecastTransaction Row(SynthTransaction t, Guid account, TransactionType type, string? name)
        => new(t.Id, account, null, t.AmountCents, type, t.At, t.Date, name, CounterpartyKey.From(name), false);

    private string NameOf(Guid accountId) => World.Accounts.Single(a => a.Id == accountId).Name;
}
