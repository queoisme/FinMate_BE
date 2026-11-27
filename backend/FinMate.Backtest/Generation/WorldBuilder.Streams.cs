using FinMate.Domain.Enums;

namespace FinMate.Backtest.Generation;

public sealed partial class WorldBuilder
{
    private const string SalesMerchant = "Khách lẻ";

    private static readonly IReadOnlyList<string> Clients = ["Khách hàng A", "Khách hàng B", "Khách hàng C", "Khách hàng D", "Khách hàng E", "Khách hàng F"];

    /// <summary>§5.2: mỗi kỳ có luồng riêng <c>base.rec.{tên}.{ngày đến hạn}</c>; kịch bản chỉ đổi số tiền / ngày trả / bỏ kỳ, không đổi lần rút.</summary>
    private void ExpandRecurring(RecurringDef def)
    {
        var occurrences = new List<TruthOccurrence>();
        var carried = 0L;   // K3: số tiền kỳ bị bỏ, trả gộp vào kỳ sau
        foreach (var due in DueDates(def))
        {
            var r = Stream($"base.rec.{def.Name}.{Iso(due)}");
            var nominal = ScenarioAmount(def, due, BaseAmount(def, due, r));
            var payDate = PayDate(def, due, r);
            var minute = r.Int(8 * 60, 17 * 60);
            var merchant = Variant(def.Name, r);
            if (_plan.Skips.FirstOrDefault(s => s.Item == def.Name && s.Month == MonthStart(due)) is { } skip)
            {
                _eventDays.Add(due);
                carried = skip.PayBothNextMonth ? nominal : 0;
                occurrences.Add(new TruthOccurrence(due, nominal, null, null, null));
                continue;
            }

            var paid = nominal + carried;
            carried = 0;
            var id = Id($"rec:{def.Name}:{Iso(due)}");
            var tx = RecurringTransaction(def, id, paid, GeneratorSpec.At(payDate, minute), merchant);
            var inData = payDate >= GeneratorSpec.DataStart && payDate <= GeneratorSpec.DataEnd;
            if (inData)
            {
                Add(tx);
                MarkScenarioEvent(def, due, payDate);
            }

            occurrences.Add(inData ? new TruthOccurrence(due, nominal, id, payDate, paid) : new TruthOccurrence(due, nominal, null, null, null));
        }

        _recurring.Add(new TruthRecurring(
            def.Name, def.Direction, def.CategorySlug, Account(def.AccountKey), def.Cadence,
            def.Cadence == RecurringCadence.Monthly ? def.AnchorDay : null, def.Volatile, occurrences));
    }

    private SynthTransaction RecurringTransaction(RecurringDef def, Guid id, long amount, DateTimeOffset at, string merchant) => def.Label switch
    {
        TxLabel.Transfer => new SynthTransaction(
            id, Account(def.AccountKey), TransactionType.Transfer, amount, at, merchant, null, TxLabel.Transfer,
            CounterAccountId: Account(def.CounterAccountKey!), RecurringName: def.Name),
        TxLabel.Fund => new SynthTransaction(
            id, Account(def.AccountKey), TransactionType.Transfer, amount, at, merchant, null, TxLabel.Fund, FundName: def.FundName, RecurringName: def.Name),
        _ => new SynthTransaction(
            id, Account(def.AccountKey), def.Direction == RecurringDirection.Inflow ? TransactionType.Credit : TransactionType.Debit, amount, at, merchant,
            def.CategorySlug, def.Label, RecurringName: def.Name),
    };

    private static IEnumerable<DateOnly> DueDates(RecurringDef def)
    {
        if (def.Cadence == RecurringCadence.Weekly)
        {
            var first = GeneratorSpec.DataStart;
            while (first.DayOfWeek != def.Weekday)
            {
                first = first.AddDays(1);
            }

            for (var d = first; d <= GeneratorSpec.DataEnd; d = d.AddDays(7))
            {
                yield return d;
            }

            yield break;
        }

        for (var m = MonthStart(GeneratorSpec.DataStart); m <= GeneratorSpec.DataEnd; m = m.AddMonths(1))
        {
            yield return m.AddDays(Math.Min(def.AnchorDay, DateTime.DaysInMonth(m.Year, m.Month)) - 1);
        }
    }

    private static long BaseAmount(RecurringDef def, DateOnly due, Rng r) => def.Amount switch
    {
        AmountModel.Electricity => Rng.RoundTo(def.BaseCents * Season(due.Month) * r.Uniform(0.9, 1.1), Rng.MoneyStep),
        AmountModel.Water => Rng.RoundTo(def.BaseCents * r.Uniform(0.85, 1.15), Rng.MoneyStep),
        AmountModel.Volatile20 => Rng.RoundTo(def.BaseCents * r.Uniform(0.8, 1.2), Rng.MoneyStep),
        AmountModel.Volatile15 => Rng.RoundTo(def.BaseCents * r.Uniform(0.85, 1.15), Rng.MoneyStep),
        _ => def.BaseCents,
    };

    private static double Season(int month) => month switch
    {
        >= 5 and <= 8 => 1.4,
        4 or 9 => 1.15,
        _ => 1.0,
    };

    /// <summary>K4 (đổi giá) và K2 (đổi số tiền thu nhập từ tháng M+2).</summary>
    private long ScenarioAmount(RecurringDef def, DateOnly due, long amount)
    {
        var factor = _plan.PriceChanges.Where(p => p.Item == def.Name && due >= p.FromMonth).Select(p => p.Factor).DefaultIfEmpty(1.0).First();
        if (_plan.IncomeTarget == def.Name)
        {
            factor *= _plan.IncomeFactorOn(due);
        }

        return factor == 1.0 ? amount : Rng.RoundTo(amount * factor, Rng.MoneyStep);
    }

    private DateOnly PayDate(RecurringDef def, DateOnly due, Rng r)
    {
        var date = def.Jitter switch
        {
            JitterModel.Bill => due.AddDays(r.Int(-2, 3)),
            JitterModel.Allowance => due.AddDays(r.Int(-3, 3)),
            JitterModel.Salary => due.DayOfWeek switch
            {
                DayOfWeek.Saturday => due.AddDays(-1),
                DayOfWeek.Sunday => due.AddDays(-2),
                _ => due,
            },
            _ => due,
        };
        return _plan.IncomeTarget == def.Name && _plan.LateMonth == MonthStart(due) ? date.AddDays(_plan.LateDays) : date;
    }

    /// <summary>Ngày sự kiện của K2 (kỳ trễ, lần đầu đổi tiền) và K4 (lần trả đầu với giá mới), cho cửa sổ ảnh hưởng.</summary>
    private void MarkScenarioEvent(RecurringDef def, DateOnly due, DateOnly payDate)
    {
        if (_plan.IncomeTarget == def.Name && _plan.LateMonth is { } month)
        {
            if (MonthStart(due) == month || (due >= month.AddMonths(2) && due < month.AddMonths(3)))
            {
                _eventDays.Add(MonthStart(due) == month ? due : payDate);
            }
        }

        if (_plan.PriceChanges.Any(p => p.Item == def.Name && MonthStart(due) == p.FromMonth))
        {
            _eventDays.Add(payDate);
        }
    }

    /// <summary>Freelancer (§5.3): mỗi tháng Poisson(2,2) khoản thu dự án. K2: tháng M dời 7–14 ngày, từ M+2 đổi số tiền.</summary>
    private void AddProjectIncome()
    {
        if (_persona != Persona.Freelancer)
        {
            return;
        }

        var target = _plan.IncomeTarget == ScenarioPlan.ProjectIncome;
        for (var month = MonthStart(GeneratorSpec.DataStart); month <= GeneratorSpec.DataEnd; month = month.AddMonths(1))
        {
            var r = Stream($"base.income.{Iso(month)}");
            var count = r.Poisson(2.2);
            for (var i = 0; i < count; i++)
            {
                var date = month.AddDays(r.Int(1, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
                var amount = r.LogNormalMoney(_profile.ProjectIncomeMonthlyCents / 2.2, 0.6);
                var client = Variant(r.Pick(Clients), r);
                var minute = r.Int(8 * 60, 17 * 60);
                if (target && month == _plan.LateMonth)
                {
                    date = date.AddDays(_plan.LateDays);
                }

                if (target && _plan.IncomeFactorOn(month) != 1.0)
                {
                    amount = Rng.RoundTo(amount * _plan.IncomeFactor, Rng.MoneyStep);
                }

                Add(new SynthTransaction(
                    Id($"project:{Iso(month)}:{i}"), Account(AccountKeys.Bank), TransactionType.Credit, amount, GeneratorSpec.At(date, minute), client,
                    Slugs.Income, TxLabel.IncomeIrregular));
            }
        }

        MarkIncomeTargetEvents(target);
    }

    private void MarkIncomeTargetEvents(bool target)
    {
        if (target && _plan.LateMonth is { } month)
        {
            _eventDays.Add(month);
            _eventDays.Add(month.AddMonths(2));
        }
    }
}
