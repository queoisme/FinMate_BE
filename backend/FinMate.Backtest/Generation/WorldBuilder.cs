using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Generation;

/// <summary>
/// Dựng một <see cref="SynthWorld"/> cho (hồ sơ, hạt giống, kịch bản) theo đặc tả bộ sinh. Mọi quá trình ngẫu nhiên lấy luồng riêng theo tên và
/// theo ngày/kỳ, nên lớp phủ kịch bản chỉ đổi đúng phần nó chạm tới (§2, §6).
/// </summary>
public sealed partial class WorldBuilder
{
    private const double VariantChance = 0.20;
    private const double PaydayIncomeShare = 0.30;
    private const int PaydayDays = 5;
    private const double DaysPerMonth = 30.4;

    private readonly ulong _rootSeed;
    private readonly string _persona;
    private readonly int _seedIndex;
    private readonly Guid _userId;
    private readonly PersonaProfile _profile;
    private readonly ScenarioPlan _plan;
    private readonly Dictionary<string, SynthAccount> _accounts = new();
    private readonly List<SynthTransaction> _transactions = [];
    private readonly List<TruthRecurring> _recurring = [];
    private readonly List<SynthLoan> _loans = [];
    private readonly List<DateOnly> _eventDays = [];

    private WorldBuilder(ulong rootSeed, string persona, int seedIndex, string scenario)
    {
        _rootSeed = rootSeed;
        _persona = persona;
        _seedIndex = seedIndex;
        _userId = GeneratorSpec.UserId(persona, seedIndex);
        _profile = Profiles.Draw(persona, Stream("base.params"));
        _plan = ScenarioPlans.Draw(scenario, _profile, Stream($"scenario.{ScenarioPlans.StreamCode(scenario)}"));
    }

    public static SynthWorld Build(string persona, int seedIndex, string scenario, ulong rootSeed = GeneratorSpec.DefaultRootSeed)
        => new WorldBuilder(rootSeed, persona, seedIndex, scenario).Run();

    /// <summary>Tham số của người gốc, không phụ thuộc kịch bản (để test và báo cáo).</summary>
    public static PersonaProfile ProfileOf(string persona, int seedIndex, ulong rootSeed = GeneratorSpec.DefaultRootSeed)
        => new WorldBuilder(rootSeed, persona, seedIndex, ScenarioCodes.K0)._profile;

    private SynthWorld Run()
    {
        foreach (var a in _profile.Accounts)
        {
            _accounts[a.Key] = new SynthAccount(Id($"account:{a.Key}"), a.Name, a.IncludeInForecast, a.OpeningCents);
        }

        foreach (var def in _profile.Recurring)
        {
            ExpandRecurring(def);
        }

        AddProjectIncome();
        AddSales();
        AddLoans();
        AddTuition();
        AddScenarioExtras();
        AddOwnTransfers();
        AddFlexible(PaydayDates());
        AddCashDeposits();
        return Finish();
    }

    private SynthWorld Finish()
    {
        var accounts = _profile.Accounts.Select(a => _accounts[a.Key]).ToList();
        var transactions = _transactions.OrderBy(t => t.At).ThenBy(t => t.Id).ToList();
        var loans = _loans.ToList();
        var firstDay = GeneratorSpec.DataStart;
        if (_plan.StartDay is { } start)
        {
            accounts = OpeningAt(accounts, transactions, start);
            transactions = transactions.Where(t => t.Date >= start).ToList();
            loans = loans.Where(l => l.Start >= start).ToList();
            firstDay = start;
        }

        return new SynthWorld(
            _persona, _seedIndex, _plan.Code, _userId, firstDay, accounts, transactions, _recurring.ToList(), loans,
            _profile.Budgets, _profile.MonthlyIncomeCents, Profiles.LowBalanceThreshold(_profile), _eventDays.Distinct().Order().ToList());
    }

    /// <summary>K6: số dư mở đầu của từng ví tại ngày bắt đầu = số dư của thế giới đầy đủ ngay trước ngày đó.</summary>
    private static List<SynthAccount> OpeningAt(IReadOnlyList<SynthAccount> accounts, IReadOnlyList<SynthTransaction> transactions, DateOnly start)
    {
        var before = transactions.Where(t => t.Date < start).ToList();
        return accounts.Select(a => a with { OpeningBalanceCents = a.OpeningBalanceCents + BalanceEffects.Sum(before, a.Id) }).ToList();
    }

    private void AddTuition()
    {
        foreach (var (date, cents) in _profile.Tuition)
        {
            Add(new SynthTransaction(
                Id($"tuition:{Iso(date)}"), Account(AccountKeys.Bank), TransactionType.Debit, cents, GeneratorSpec.At(date, 9 * 60), "Học phí",
                Slugs.Education, TxLabel.Flexible));
        }
    }

    private void AddOwnTransfers()
    {
        foreach (var def in _profile.OwnTransfers)
        {
            for (var month = MonthStart(GeneratorSpec.DataStart); month <= GeneratorSpec.DataEnd; month = month.AddMonths(1))
            {
                foreach (var day in def.Days)
                {
                    var date = month.AddDays(day - 1);
                    var r = Stream($"base.own.{def.Name}.{Iso(date)}");
                    Add(new SynthTransaction(
                        Id($"own:{def.Name}:{Iso(date)}"), Account(def.From), TransactionType.Transfer, r.Money(def.MinCents, def.MaxCents),
                        GeneratorSpec.At(date, 12 * 60), def.Name, null, TxLabel.Transfer, CounterAccountId: Account(def.To)));
                }
            }
        }
    }

    /// <summary>Tiểu thương (§5.3): mỗi thứ Bảy 20:00 nộp 70% số tiền mặt vượt 2 triệu vào ngân hàng. Không ngẫu nhiên.</summary>
    private void AddCashDeposits()
    {
        if (_persona != Persona.SmallTrader)
        {
            return;
        }

        var cash = _accounts[AccountKeys.Cash];
        var flows = _transactions.Where(t => t.AccountId == cash.Id || t.CounterAccountId == cash.Id).OrderBy(t => t.At).ThenBy(t => t.Id).ToList();
        var balance = cash.OpeningBalanceCents;
        var next = 0;
        for (var day = GeneratorSpec.DataStart; day <= GeneratorSpec.DataEnd; day = day.AddDays(1))
        {
            if (day.DayOfWeek != DayOfWeek.Saturday)
            {
                continue;
            }

            var at = GeneratorSpec.At(day, 20 * 60);
            for (; next < flows.Count && flows[next].At < at; next++)
            {
                balance += BalanceEffects.Of(flows[next], cash.Id);
            }

            var deposit = Rng.RoundTo(0.7 * (balance - 2_000_000), Rng.MoneyStep);
            if (deposit > 0)
            {
                Add(new SynthTransaction(
                    Id($"deposit:{Iso(day)}"), cash.Id, TransactionType.Transfer, deposit, at, "Nộp tiền", null, TxLabel.Transfer,
                    CounterAccountId: Account(AccountKeys.Bank)));
                balance -= deposit;
            }
        }
    }

    private IReadOnlySet<DateOnly> PaydayDates()
    {
        var floor = _profile.MonthlyIncomeCents * PaydayIncomeShare;
        var dates = new HashSet<DateOnly>();
        foreach (var t in _transactions.Where(t => t.Type == TransactionType.Credit && t.Label is TxLabel.Recurring or TxLabel.IncomeIrregular or TxLabel.Scenario))
        {
            if (t.AmountCents >= floor)
            {
                for (var i = 0; i < PaydayDays; i++)
                {
                    dates.Add(t.Date.AddDays(i));
                }
            }
        }

        return dates;
    }

    private Rng Stream(string name) => Rng.ForStream($"{_rootSeed}|{_persona}|{_seedIndex}|{name}");

    private Guid Id(string key) => DeterministicGuid.V5(GeneratorSpec.IdNamespace, $"{_userId}|{key}");

    private Guid Account(string key) => _accounts[key].Id;

    private void Add(SynthTransaction transaction)
    {
        if (transaction.Date >= GeneratorSpec.DataStart && transaction.Date <= GeneratorSpec.DataEnd)
        {
            _transactions.Add(transaction);
        }
    }

    /// <summary>§4: 20% xác suất viết khác kiểu (hoa, thêm khoảng trắng, bỏ dấu); cả ba cho cùng <see cref="CounterpartyKey"/>.</summary>
    private static string Variant(string name, Rng r)
    {
        if (!r.Chance(VariantChance))
        {
            return name;
        }

        return r.Int(0, 2) switch
        {
            0 => name.ToUpperInvariant(),
            1 => name.Replace(" ", "  ", StringComparison.Ordinal),
            _ => new string(name.ToLowerInvariant().Select(CounterpartyKey.FoldChar).ToArray()),
        };
    }

    private static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Ảnh hưởng của một giao dịch tổng hợp lên số dư một ví (§4): chi trừ, thu cộng, chuyển ví trừ nguồn cộng đích, góp quỹ trừ nguồn.</summary>
public static class BalanceEffects
{
    public static long Of(SynthTransaction t, Guid accountId) => t.Type switch
    {
        TransactionType.Debit => t.AccountId == accountId ? -t.AmountCents : 0,
        TransactionType.Credit => t.AccountId == accountId ? t.AmountCents : 0,
        _ => (t.AccountId == accountId ? -t.AmountCents : 0) + (t.CounterAccountId == accountId ? t.AmountCents : 0),
    };

    public static long Sum(IEnumerable<SynthTransaction> transactions, Guid accountId) => transactions.Sum(t => Of(t, accountId));
}
