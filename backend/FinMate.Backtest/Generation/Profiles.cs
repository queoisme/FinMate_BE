using FinMate.Domain.Enums;

namespace FinMate.Backtest.Generation;

/// <summary>
/// Rút tham số của một người gốc theo đặc tả §5.3. Mọi lần rút đi qua một luồng <c>base.params</c> theo thứ tự cố định trong mỗi hồ sơ,
/// nên tham số không phụ thuộc kịch bản.
/// </summary>
public static class Profiles
{
    private const long Thousand = 1_000;
    private const long Million = 1_000_000;
    private const double ThresholdShare = 0.20;
    private const double TraderMargin = 0.30;
    private const double BudgetChance = 0.60;

    private static readonly IReadOnlyList<(string Slug, double Share)> DefaultShares =
    [
        (Slugs.Food, 0.45), (Slugs.Transport, 0.15), (Slugs.Shopping, 0.20), (Slugs.Entertainment, 0.12), (Slugs.Health, 0.04), (Slugs.Other, 0.04),
    ];

    public static PersonaProfile Draw(string persona, Rng p) => persona switch
    {
        Persona.StableWorker => StableWorker(p),
        Persona.Freelancer => Freelancer(p),
        Persona.Student => Student(p),
        Persona.Family => Family(p),
        Persona.SmallTrader => SmallTrader(p),
        _ => throw new ArgumentOutOfRangeException(nameof(persona), persona, null),
    };

    /// <summary>Ngưỡng số dư thấp (§5.4, S2): 20% thu nhập tháng danh nghĩa, làm tròn 100.000đ.</summary>
    public static long LowBalanceThreshold(PersonaProfile profile) => Rng.RoundTo(profile.MonthlyIncomeCents * ThresholdShare, 100_000);

    private static PersonaProfile StableWorker(Rng p)
    {
        var salary = p.Money(9 * Million, 18 * Million);
        var payday = p.Chance(0.5) ? 5 : 10;
        var accounts = new[]
        {
            new AccountDef(AccountKeys.Bank, "Ngân hàng", true, Rng.RoundTo(salary * p.Uniform(0.5, 2.0), 100_000)),
            new AccountDef(AccountKeys.Cash, "Tiền mặt", true, p.Money(500_000, 2 * Million)),
        };
        var recurring = new List<RecurringDef>
        {
            Inflow("Lương công ty", salary, payday, JitterModel.Salary),
            Bill("Tiền nhà", Slugs.Housing, Rng.RoundTo(salary * p.Uniform(0.20, 0.30), 100_000), p.Int(1, 5)),
            Bill("Điện lực", Slugs.Bills, p.Money(300_000, 700_000, Thousand), p.Int(15, 20), AmountModel.Electricity),
            Bill("Nước sạch", Slugs.Bills, p.Money(80_000, 200_000, Thousand), p.Int(15, 20), AmountModel.Water),
            Bill("Internet", Slugs.Bills, p.Money(180_000, 300_000, Thousand), 10),
            Bill("Cước di động", Slugs.Bills, p.Money(100_000, 200_000, Thousand), 20),
        };
        var flexMonthly = Rng.RoundTo(salary * p.Uniform(0.35, 0.50), Thousand);
        var transfers = new[] { new OwnTransferDef("Rút tiền mặt", [6, 21], Million, 2 * Million, AccountKeys.Bank, AccountKeys.Cash) };
        return Finish(Persona.StableWorker, p, accounts, flexMonthly, DefaultShares, recurring, transfers, salary, cashShareOfFood: 0.30);
    }

    private static PersonaProfile Freelancer(Rng p)
    {
        var monthly = p.Money(12 * Million, 30 * Million);
        var accounts = new[]
        {
            new AccountDef(AccountKeys.Bank, "Ngân hàng", true, Rng.RoundTo(monthly * p.Uniform(0.5, 2.0), 100_000)),
            new AccountDef(AccountKeys.EWallet, "Ví điện tử", true, p.Money(200_000, Million)),
        };
        var recurring = new List<RecurringDef>
        {
            Bill("Tiền nhà", Slugs.Housing, Rng.RoundTo(monthly * p.Uniform(0.15, 0.25), 100_000), p.Int(1, 5)),
            Bill("Internet", Slugs.Bills, p.Money(180_000, 300_000, Thousand), 10),
            Bill("Phần mềm thiết kế", Slugs.Bills, p.Money(250_000, 500_000, Thousand), 12),
            Bill("Cước di động", Slugs.Bills, p.Money(100_000, 200_000, Thousand), 20),
        };
        var flexMonthly = Rng.RoundTo(monthly * p.Uniform(0.35, 0.50), Thousand);
        var transfers = new[] { new OwnTransferDef("Nạp ví", [1, 15], 500_000, 1_500_000, AccountKeys.Bank, AccountKeys.EWallet) };
        return Finish(Persona.Freelancer, p, accounts, flexMonthly, DefaultShares, recurring, transfers, monthly) with { ProjectIncomeMonthlyCents = monthly };
    }

    private static PersonaProfile Student(Rng p)
    {
        var allowance = p.Money(3 * Million, 6 * Million);
        var weekly = p.Money(400_000, Million);
        var accounts = new[] { new AccountDef(AccountKeys.Bank, "Ngân hàng", true, Rng.RoundTo(allowance * p.Uniform(0.3, 1.5), 100_000)) };
        var recurring = new List<RecurringDef>
        {
            Inflow("Trợ cấp gia đình", allowance, p.Int(1, 5), JitterModel.Allowance),
            new("Làm thêm", RecurringDirection.Inflow, Slugs.Income, AccountKeys.Bank, RecurringCadence.Weekly, 0, DayOfWeek.Saturday, weekly,
                AmountModel.Volatile20, JitterModel.None, TxLabel.RecurringVolatile),
            Bill("Phòng trọ", Slugs.Housing, p.Money(Million, 2 * Million), p.Int(1, 5)),
            Bill("Cước di động", Slugs.Bills, p.Money(70_000, 120_000, Thousand), 20),
        };
        var income = allowance + Rng.RoundTo(4.3 * weekly, Thousand);
        var flexMonthly = Rng.RoundTo(income * p.Uniform(0.60, 0.80), Thousand);
        IReadOnlyList<(string, double)> shares =
        [
            (Slugs.Food, 0.55), (Slugs.Transport, 0.10), (Slugs.Shopping, 0.15), (Slugs.Entertainment, 0.15), (Slugs.Health, 0.02), (Slugs.Other, 0.03),
        ];
        var tuition = new[]
        {
            (new DateOnly(2025, 8, 25).AddDays(p.Int(-5, 5)), p.Money(8 * Million, 15 * Million)),
            (new DateOnly(2026, 1, 20).AddDays(p.Int(-5, 5)), p.Money(8 * Million, 15 * Million)),
        };
        return Finish(Persona.Student, p, accounts, flexMonthly, shares, recurring, [], income) with { Tuition = tuition };
    }

    private static PersonaProfile Family(Rng p)
    {
        var salary1 = p.Money(12 * Million, 25 * Million);
        var salary2 = p.Money(8 * Million, 15 * Million);
        var income = salary1 + salary2;
        var accounts = new[]
        {
            new AccountDef(AccountKeys.Bank, "Ngân hàng chính", true, Rng.RoundTo(income * p.Uniform(0.5, 1.5), 100_000)),
            new AccountDef(AccountKeys.EWallet, "Ví điện tử", true, p.Money(300_000, Million)),
            new AccountDef(AccountKeys.Savings, "Tiết kiệm", false, p.Money(20 * Million, 100 * Million)),
        };
        var recurring = new List<RecurringDef>
        {
            Inflow("Lương 1", salary1, 5, JitterModel.Salary),
            Inflow("Lương 2", salary2, 10, JitterModel.Salary),
            Bill("Trả góp nhà", Slugs.Housing, p.Money(4 * Million, 8 * Million), 15),
            Bill("Học phí con", Slugs.Education, p.Money(2 * Million, 5 * Million), 5),
            Bill("Điện lực", Slugs.Bills, p.Money(500_000, 1_200_000, Thousand), p.Int(15, 20), AmountModel.Electricity),
            Bill("Nước sạch", Slugs.Bills, p.Money(150_000, 350_000, Thousand), p.Int(15, 20), AmountModel.Water),
            Bill("Internet", Slugs.Bills, p.Money(180_000, 300_000, Thousand), 10),
            Bill("Gửi tiết kiệm", Slugs.Other, p.Money(2 * Million, 5 * Million), 6) with
            {
                Label = TxLabel.Transfer, CounterAccountKey = AccountKeys.Savings,
            },
            Bill("Quỹ gia đình", Slugs.Family, p.Money(500_000, Million), 5) with { Label = TxLabel.Fund, FundName = "Quỹ gia đình" },
        };
        var flexMonthly = Rng.RoundTo(income * p.Uniform(0.35, 0.45), Thousand);
        IReadOnlyList<(string, double)> shares =
        [
            (Slugs.Food, 0.40), (Slugs.Transport, 0.12), (Slugs.Shopping, 0.18), (Slugs.Family, 0.15), (Slugs.Entertainment, 0.08), (Slugs.Health, 0.05),
            (Slugs.Other, 0.02),
        ];
        var transfers = new[] { new OwnTransferDef("Nạp ví", [1, 15], Million, 2 * Million, AccountKeys.Bank, AccountKeys.EWallet) };
        var profile = Finish(Persona.Family, p, accounts, flexMonthly, shares, recurring, transfers, income);
        return profile with { BorrowCents = p.Money(20 * Million, 50 * Million), LendCents = p.Money(3 * Million, 8 * Million) };
    }

    private static PersonaProfile SmallTrader(Rng p)
    {
        var revenue = p.Money(800_000, 2_500_000);
        var closed = (DayOfWeek)p.Int(0, 6);
        var stockRatio = p.Uniform(0.55, 0.70);
        var accounts = new[]
        {
            new AccountDef(AccountKeys.Bank, "Ngân hàng", true, Rng.RoundTo(revenue * p.Uniform(5, 20), 100_000)),
            new AccountDef(AccountKeys.Cash, "Tiền mặt", true, p.Money(Million, 3 * Million)),
        };
        var recurring = new List<RecurringDef>
        {
            new("Đại lý", RecurringDirection.Outflow, Slugs.Other, AccountKeys.Bank, RecurringCadence.Weekly, 0, DayOfWeek.Monday,
                Rng.RoundTo(revenue * 7 * stockRatio, Thousand), AmountModel.Volatile15, JitterModel.None, TxLabel.RecurringVolatile),
            Bill("Thuê sạp", Slugs.Housing, p.Money(1_500_000, 4 * Million), p.Int(1, 5)),
            Bill("Điện lực", Slugs.Bills, p.Money(300_000, 700_000, Thousand), p.Int(15, 20), AmountModel.Electricity),
            Bill("Cước di động", Slugs.Bills, p.Money(100_000, 200_000, Thousand), 20),
        };
        var flexMonthly = Rng.RoundTo(revenue * 30 * p.Uniform(0.15, 0.25), Thousand);
        var income = Rng.RoundTo(revenue * 30 * TraderMargin, Thousand);
        var profile = Finish(Persona.SmallTrader, p, accounts, flexMonthly, DefaultShares, recurring, [], income);
        return profile with { DailyRevenueCents = revenue, ClosedDay = closed };
    }

    /// <summary>Phần chung cuối mỗi hồ sơ: hiệu ứng ngày lương, trôi mức chi, ngân sách (§5.1, §5.4). Thứ tự rút cố định.</summary>
    private static PersonaProfile Finish(
        string persona, Rng p, IReadOnlyList<AccountDef> accounts, long flexMonthly, IReadOnlyList<(string Slug, double Share)> shares,
        IReadOnlyList<RecurringDef> recurring, IReadOnlyList<OwnTransferDef> transfers, long monthlyIncome, double cashShareOfFood = 0)
    {
        var payday = p.Uniform(0.10, 0.30);
        var drift = p.Uniform(-0.01, 0.02);
        var flex = shares.Select(s => FlexCategories.For(s.Slug, s.Share)).ToList();
        var hasBudgets = p.Chance(BudgetChance);
        var budgets = flex.Select(c => new SynthBudget(c.Slug, Rng.RoundTo(flexMonthly * c.Share * p.Uniform(0.8, 1.3), 100_000))).ToList();
        return new PersonaProfile(
            persona, accounts, flexMonthly, flex, cashShareOfFood, payday, drift, recurring, transfers, monthlyIncome, hasBudgets,
            hasBudgets ? budgets : []);
    }

    private static RecurringDef Inflow(string name, long amount, int day, JitterModel jitter)
        => new(name, RecurringDirection.Inflow, Slugs.Income, AccountKeys.Bank, RecurringCadence.Monthly, day, default, amount, AmountModel.Fixed, jitter,
            TxLabel.Recurring);

    private static RecurringDef Bill(string name, string slug, long amount, int day, AmountModel model = AmountModel.Fixed)
        => new(name, RecurringDirection.Outflow, slug, AccountKeys.Bank, RecurringCadence.Monthly, day, default, amount, model, JitterModel.Bill,
            TxLabel.Recurring);
}

/// <summary>λ, σ và tên nơi chi mặc định của từng danh mục chi linh hoạt (§5.1). Tên có dấu là tên chuẩn; biến thể sinh lúc tạo giao dịch.</summary>
public static class FlexCategories
{
    private static readonly Dictionary<string, (double Lambda, double Sigma, string[] Merchants)> Defaults = new()
    {
        [Slugs.Food] = (1.6, 0.5, ["Quán cơm", "Cà phê", "Bún phở", "Siêu thị mini", "Trà sữa"]),
        [Slugs.Transport] = (0.5, 0.6, ["Grab", "Xăng dầu", "Gửi xe"]),
        [Slugs.Shopping] = (0.25, 0.9, ["Shopee", "Siêu thị", "Cửa hàng quần áo"]),
        [Slugs.Entertainment] = (0.2, 0.8, ["Rạp phim", "Karaoke", "Trò chơi"]),
        [Slugs.Health] = (0.05, 1.0, ["Nhà thuốc", "Phòng khám"]),
        [Slugs.Family] = (0.15, 0.8, ["Đồ dùng trẻ em", "Sữa bột"]),
        [Slugs.Other] = (0.15, 1.0, ["Cắt tóc", "Giặt ủi", "Tạp hóa"]),
    };

    public static FlexCategory For(string slug, double share)
    {
        var d = Defaults[slug];
        return new FlexCategory(slug, share, d.Lambda, d.Sigma, d.Merchants);
    }
}
