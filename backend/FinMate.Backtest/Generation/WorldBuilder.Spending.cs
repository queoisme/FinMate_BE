using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Generation;

public sealed partial class WorldBuilder
{
    private const double RefundChance = 0.03;
    private const double FullRefundChance = 0.60;
    private const int K2WithheldDays = 10;

    /// <summary>
    /// Tiểu thương (§5.3): mỗi ngày mở hàng Poisson(2)+1 khoản thu, 60% tiền mặt. K2: phần chuyển khoản của 10 ngày đầu tháng M dồn về một khoản
    /// ngày 10, từ M+2 đổi số tiền. K5: nghỉ mùng 1–4, doanh thu ×1,5 trong 10 ngày trước mùng 1.
    /// </summary>
    private void AddSales()
    {
        if (_persona != Persona.SmallTrader)
        {
            return;
        }

        var target = _plan.IncomeTarget == ScenarioPlan.SalesIncome;
        long withheld = 0;
        for (var day = GeneratorSpec.DataStart; day <= GeneratorSpec.DataEnd; day = day.AddDays(1))
        {
            if (day.DayOfWeek == _profile.ClosedDay || (_plan.IsTet && day >= GeneratorSpec.Tet2026 && day <= GeneratorSpec.Tet2026.AddDays(3)))
            {
                continue;
            }

            var r = Stream($"base.sales.{Iso(day)}");
            var count = r.Poisson(2) + 1;
            var median = _profile.DailyRevenueCents * (day.DayOfWeek == DayOfWeek.Sunday ? 1.3 : 1.0) / 3.0 * SalesFactor(day, target);
            for (var i = 0; i < count; i++)
            {
                var amount = r.LogNormalMoney(median, 0.5);
                var cash = r.Chance(0.6);
                var minute = r.Int(6 * 60, 21 * 60);
                if (!cash && target && IsWithheld(day))
                {
                    withheld += amount;
                    continue;
                }

                Add(new SynthTransaction(
                    Id($"sale:{Iso(day)}:{i}"), Account(cash ? AccountKeys.Cash : AccountKeys.Bank), TransactionType.Credit, amount,
                    GeneratorSpec.At(day, minute), SalesMerchant, Slugs.Income, TxLabel.IncomeIrregular));
            }

            if (target && _plan.LateMonth is { } month && day == month.AddDays(K2WithheldDays - 1) && withheld > 0)
            {
                Add(new SynthTransaction(
                    Id($"sale:withheld:{Iso(day)}"), Account(AccountKeys.Bank), TransactionType.Credit, withheld, GeneratorSpec.At(day, 20 * 60 + 30),
                    SalesMerchant, Slugs.Income, TxLabel.Scenario));
                withheld = 0;
            }
        }

        MarkIncomeTargetEvents(target);
    }

    private double SalesFactor(DateOnly day, bool target)
    {
        var factor = target ? _plan.IncomeFactorOn(day) : 1.0;
        return _plan.IsTet && day >= GeneratorSpec.Tet2026.AddDays(-10) && day < GeneratorSpec.Tet2026 ? factor * 1.5 : factor;
    }

    private bool IsWithheld(DateOnly day) => _plan.LateMonth is { } month && day >= month && day < month.AddDays(K2WithheldDays);

    /// <summary>Gia đình (§5.3): đi vay ngày 2025-03-10, trả hằng tháng ngày 20, phần còn lại trả quanh hạn; cho vay 2025-08-01, được trả 2025-11-10.</summary>
    private void AddLoans()
    {
        if (_persona != Persona.Family)
        {
            return;
        }

        var r = Stream("base.loans");
        var bank = Account(AccountKeys.Bank);
        var borrowStart = new DateOnly(2025, 3, 10);
        var borrowDue = new DateOnly(2025, 12, 15);
        Add(new SynthTransaction(Id("loan:borrow"), bank, TransactionType.Credit, _profile.BorrowCents, GeneratorSpec.At(borrowStart, 10 * 60), "Người thân",
            Slugs.Loan, TxLabel.Loan));
        var repayments = new List<SynthLoanRepayment>();
        var remaining = _profile.BorrowCents;
        for (var month = new DateOnly(2025, 4, 20); month < borrowDue && remaining > 0; month = month.AddMonths(1))
        {
            var amount = Math.Min(r.Money(2_000_000, 5_000_000), remaining);
            repayments.Add(Repay($"loan:borrow:repay:{Iso(month)}", bank, amount, GeneratorSpec.At(month, 12 * 60), "Người thân"));
            remaining -= amount;
        }

        var finalDay = borrowDue.AddDays(r.Int(0, 10));
        if (remaining > 0)
        {
            repayments.Add(Repay($"loan:borrow:repay:{Iso(finalDay)}", bank, remaining, GeneratorSpec.At(finalDay, 12 * 60), "Người thân"));
        }

        _loans.Add(new SynthLoan(Id("loan:borrow:book"), LoanDirection.Borrow, _profile.BorrowCents, borrowStart, borrowDue, repayments));

        var lendStart = new DateOnly(2025, 8, 1);
        Add(new SynthTransaction(Id("loan:lend"), bank, TransactionType.Debit, _profile.LendCents, GeneratorSpec.At(lendStart, 14 * 60), "Bạn", Slugs.Loan,
            TxLabel.Loan));
        var back = new DateOnly(2025, 11, 10);
        var returned = new SynthTransaction(
            Id("loan:lend:repay"), bank, TransactionType.Credit, _profile.LendCents, GeneratorSpec.At(back, 15 * 60), "Bạn", Slugs.Loan, TxLabel.Loan);
        Add(returned);
        _loans.Add(new SynthLoan(
            Id("loan:lend:book"), LoanDirection.Lend, _profile.LendCents, lendStart, new DateOnly(2025, 10, 1),
            [new SynthLoanRepayment(returned.Id, returned.At, returned.AmountCents)]));
    }

    private SynthLoanRepayment Repay(string key, Guid account, long amount, DateTimeOffset at, string merchant)
    {
        var tx = new SynthTransaction(Id(key), account, TransactionType.Debit, amount, at, merchant, Slugs.Loan, TxLabel.Loan);
        Add(tx);
        return new SynthLoanRepayment(tx.Id, at, amount);
    }

    /// <summary>K1 (chi đột xuất), K5 (thưởng Tết, lì xì).</summary>
    private void AddScenarioExtras()
    {
        foreach (var o in _plan.OneOffs)
        {
            Add(new SynthTransaction(
                Id($"k1:{Iso(o.Date)}"), Account(AccountKeys.Bank), TransactionType.Debit, o.Cents, GeneratorSpec.At(o.Date, o.MinuteOfDay), o.Merchant,
                o.Slug, TxLabel.Scenario, InjectedAnomaly: true));
            _eventDays.Add(o.Date);
        }

        foreach (var (item, daysBefore) in _plan.TetBonuses)
        {
            var def = _profile.Recurring.First(r => r.Name == item);
            var date = GeneratorSpec.Tet2026.AddDays(-daysBefore);
            Add(new SynthTransaction(
                Id($"k5:bonus:{item}"), Account(def.AccountKey), TransactionType.Credit, def.BaseCents, GeneratorSpec.At(date, 10 * 60),
                $"Thưởng Tết {item}", Slugs.Income, TxLabel.Scenario));
        }

        if (_plan.LixiDay is { } lixiDay)
        {
            Add(new SynthTransaction(
                Id("k5:lixi"), Account(AccountKeys.Bank), TransactionType.Credit, _plan.LixiCents, GeneratorSpec.At(lixiDay, 11 * 60), "Lì xì", Slugs.Income,
                TxLabel.Scenario));
        }

        if (_plan.IsTet)
        {
            for (var d = ScenarioPlan.TetSpendStart; d <= ScenarioPlan.TetSpendEnd; d = d.AddDays(1))
            {
                _eventDays.Add(d);
            }
        }
    }

    /// <summary>§5.1: chi linh hoạt theo (ngày, danh mục), luồng <c>base.flex.{danh mục}.{ngày}</c>.</summary>
    private void AddFlexible(IReadOnlySet<DateOnly> paydays)
    {
        for (var day = GeneratorSpec.DataStart; day <= GeneratorSpec.DataEnd; day = day.AddDays(1))
        {
            var lambdaFactor = WeekdayFactor(day.DayOfWeek) * (paydays.Contains(day) ? 1 + _profile.PaydayBoost : 1.0) * _plan.FlexLambdaFactor(day);
            var drift = Math.Pow(1 + _profile.MonthlyDrift, ((day.Year - 2025) * 12) + day.Month - 1);
            foreach (var category in _profile.Flex)
            {
                AddFlexibleDay(day, category, lambdaFactor, drift);
            }
        }
    }

    private void AddFlexibleDay(DateOnly day, FlexCategory category, double lambdaFactor, double drift)
    {
        var r = Stream($"base.flex.{category.Slug}.{Iso(day)}");
        var median = _profile.FlexMonthlyCents * category.Share / (DaysPerMonth * category.Lambda * Math.Exp(category.Sigma * category.Sigma / 2)) * drift;
        var count = r.Poisson(category.Lambda * lambdaFactor);
        for (var i = 0; i < count; i++)
        {
            var amount = r.LogNormalMoney(median, category.Sigma);
            var merchant = Variant(r.Pick(category.Merchants), r);
            var minute = r.Int(7 * 60, 22 * 60);
            var cash = category.Slug == Slugs.Food && _profile.CashShareOfFood > 0 && r.Chance(_profile.CashShareOfFood);
            var tx = new SynthTransaction(
                Id($"flex:{category.Slug}:{Iso(day)}:{i}"), Account(cash ? AccountKeys.Cash : AccountKeys.Bank), TransactionType.Debit, amount,
                GeneratorSpec.At(day, minute), merchant, category.Slug, TxLabel.Flexible);
            Add(tx);
            if (category.Slug == Slugs.Shopping && r.Chance(RefundChance))
            {
                AddRefund(tx, r);
            }
        }
    }

    private void AddRefund(SynthTransaction original, Rng r)
    {
        var after = r.Int(3, 10);
        var full = r.Chance(FullRefundChance);
        var share = r.Uniform(0.2, 0.7);
        var amount = full ? original.AmountCents : Math.Max(Rng.MoneyStep, Rng.RoundTo(original.AmountCents * share, Rng.MoneyStep));
        Add(new SynthTransaction(
            Id($"refund:{original.Id}"), original.AccountId, TransactionType.Credit, amount, original.At.AddDays(after), original.Merchant,
            original.CategorySlug, TxLabel.Refund, Relation: TransactionRelation.Refund, RelatedTransactionId: original.Id));
    }

    private static double WeekdayFactor(DayOfWeek day) => day switch
    {
        DayOfWeek.Friday => 1.1,
        DayOfWeek.Saturday => 1.3,
        DayOfWeek.Sunday => 1.2,
        _ => 0.9,
    };
}
