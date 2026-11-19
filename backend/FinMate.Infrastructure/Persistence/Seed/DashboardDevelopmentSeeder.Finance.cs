using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Infrastructure.Persistence.Seed;

public static partial class DashboardDevelopmentSeeder
{
    private sealed record Ledger(User User, FinancialAccount Bank, FinancialAccount Cash, List<Transaction> Transactions);

    private static Dictionary<string, Ledger> AddFinances(FinMateDbContext db, Dictionary<string, User> people,
        Dictionary<string, Category> categories, Dictionary<string, ProviderConfig> providers, DateTimeOffset now)
    {
        var ledgers = new Dictionary<string, Ledger>();
        foreach (var key in new[] { "an", "binh", "chi" })
        {
            var user = people[key];
            var income = key == "chi" ? 5_000_000L : key == "binh" ? 20_000_000L : 24_000_000L;
            user.MonthlyIncomeCents = income;
            user.IncomeProfileOverride = key == "binh" ? "variable" : "stable";
            user.OnboardingStatus = OnboardingStatus.Completed;
            user.OnboardingCompletedAt = now.AddMonths(-6);
            var provider = providers["mb_bank"];
            var bank = new FinancialAccount { Id = Id($"bank:{key}"), UserId = user.Id, ProviderConfigId = provider.Id,
                AccountName = $"{Label} MB — {key}", AccountType = AccountType.Bank, PackageName = provider.PackageName,
                BalanceCents = key == "chi" ? 3_000_000 : 12_000_000, IsMonitored = false, CreatedAt = user.CreatedAt, UpdatedAt = now };
            var cash = new FinancialAccount { Id = Id($"cash:{key}"), UserId = user.Id, AccountName = $"{Label} Tiền mặt — {key}",
                AccountType = AccountType.Cash, BalanceCents = 1_000_000, IsMonitored = false, CreatedAt = user.CreatedAt, UpdatedAt = now };
            var ledger = new Ledger(user, bank, cash, []);
            BuildTransactions(ledger, key, income, categories, VietnamTime.Today());
            ApplyBalances(ledger);
            AddBudgetsAndSummaries(db, ledger, key, categories, now);
            AddGoals(db, ledger, key, now);
            AddRecurring(db, ledger, key, income, categories, now);
            AddDrafts(ledger, key, categories, now);
            db.FinancialAccounts.AddRange(bank, cash);
            db.Transactions.AddRange(ledger.Transactions);
            ledgers.Add(key, ledger);
        }
        return ledgers;
    }

    private static void BuildTransactions(Ledger ledger, string key, long income, Dictionary<string, Category> categories, DateOnly today)
    {
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-5);
        for (var day = start; day < today; day = day.AddDays(1))
        {
            if (day.Day == 5) AddTransaction(ledger, key, day, "income", TransactionType.Credit,
                key == "binh" ? income + (day.Month % 3 - 1) * 3_000_000 : income, "Thu nhập tháng", categories, false);
            if (day.Day == 3) AddTransaction(ledger, key, day, "housing", TransactionType.Debit,
                key == "chi" ? 1_500_000 : 4_500_000, "Tiền thuê nhà", categories, false);
            if (day.Day == 6) AddTransaction(ledger, key, day, "transfer", TransactionType.Transfer,
                key == "chi" ? 1_500_000 : 3_000_000, "Rút tiền mặt để chi trong tháng", categories, false);
            if (day.Day == 12) AddTransaction(ledger, key, day, "bills", TransactionType.Debit,
                key == "chi" ? 180_000 : 650_000, "Điện, nước và Internet", categories, false);
            if (day.Day == 20) AddTransaction(ledger, key, day, "shopping", TransactionType.Debit,
                key == "chi" ? 180_000 : 650_000, "Đồ dùng sinh hoạt", categories, false);
            AddTransaction(ledger, key, day, "food", TransactionType.Debit,
                (key == "chi" ? 25_000 : 45_000) + day.Day % 3 * 5_000, "Bữa trưa", categories, true);
            if (day.DayOfWeek == DayOfWeek.Monday) AddTransaction(ledger, key, day, "transport", TransactionType.Debit,
                key == "chi" ? 30_000 : 100_000, "Đi lại trong tuần", categories, true);
            if (day.DayOfWeek == DayOfWeek.Saturday) AddTransaction(ledger, key, day, "entertainment", TransactionType.Debit,
                key == "chi" ? 25_000 : 55_000, "Cà phê cuối tuần", categories, true);
        }
    }

    private static void AddTransaction(Ledger ledger, string key, DateOnly day, string category, TransactionType type,
        long amount, string description, Dictionary<string, Category> categories, bool cash)
    {
        var at = VietnamTime.DayRange(day).Start.AddHours(cash ? 12 : 8);
        ledger.Transactions.Add(new Transaction { Id = Id($"transaction:{key}:{day:yyyy-MM-dd}:{category}"),
            ClientRequestId = Id($"request:{key}:{day:yyyy-MM-dd}:{category}"), UserId = ledger.User.Id,
            FinancialAccountId = cash ? ledger.Cash.Id : ledger.Bank.Id,
            CounterAccountId = type == TransactionType.Transfer ? ledger.Cash.Id : null,
            CategoryId = type == TransactionType.Transfer ? null : categories[category].Id, AmountCents = amount,
            TransactionType = type, Source = TransactionSource.Manual, Status = TransactionStatus.Confirmed,
            MerchantName = description, Description = $"{Label} Tình huống {key}: {description.ToLowerInvariant()}",
            TransactedAt = at, CreatedAt = at, UpdatedAt = at });
    }

    private static void ApplyBalances(Ledger ledger)
    {
        foreach (var row in ledger.Transactions.OrderBy(row => row.TransactedAt).ThenBy(row => row.Id))
        {
            var account = row.FinancialAccountId == ledger.Bank.Id ? ledger.Bank : ledger.Cash;
            account.BalanceCents += row.TransactionType == TransactionType.Credit ? row.AmountCents : -row.AmountCents;
            if (row.TransactionType == TransactionType.Transfer) ledger.Cash.BalanceCents += row.AmountCents;
            row.BalanceAfterCents = account.BalanceCents;
            if (account.BalanceCents < 0 || ledger.Cash.BalanceCents < 0)
                throw new InvalidOperationException("The development ledger must never spend more than its available balance.");
        }
    }

    private static void AddBudgetsAndSummaries(FinMateDbContext db, Ledger ledger, string key,
        Dictionary<string, Category> categories, DateTimeOffset now)
    {
        var (start, end) = VietnamTime.MonthRange(now);
        var limit = key == "chi" ? 600_000L : key == "binh" ? 900_000L : 2_500_000L;
        var budget = new Budget { Id = Id($"budget:{key}"), UserId = ledger.User.Id, CategoryId = categories["food"].Id,
            LimitCents = limit, PeriodType = BudgetPeriodType.Monthly, CreatedAt = start, UpdatedAt = now };
        db.Budgets.Add(budget);
        db.BudgetPeriods.Add(new BudgetPeriod { Id = Id($"period:{key}"), BudgetId = budget.Id, PeriodStart = start,
            PeriodEnd = end, LimitCents = limit, SpentCents = ledger.Transactions.Where(row => row.TransactionType == TransactionType.Debit
                && row.CategoryId == budget.CategoryId && row.TransactedAt >= start && row.TransactedAt < end).Sum(row => row.AmountCents),
            CreatedAt = start, UpdatedAt = now });
        foreach (var day in ledger.Transactions.GroupBy(row => VietnamTime.DateOf(row.TransactedAt)))
        {
            var spending = day.Where(row => row.TransactionType == TransactionType.Debit).ToArray();
            var top = spending.GroupBy(row => row.CategoryId).OrderByDescending(group => group.Sum(row => row.AmountCents)).FirstOrDefault();
            db.DailySummaries.Add(new DailySummary { Id = Id($"summary:{key}:{day.Key:yyyy-MM-dd}"), UserId = ledger.User.Id,
                SummaryDate = day.Key, TotalSpentCents = spending.Sum(row => row.AmountCents),
                TotalIncomeCents = day.Where(row => row.TransactionType == TransactionType.Credit).Sum(row => row.AmountCents),
                TransactionCount = day.Count(row => row.TransactionType != TransactionType.Transfer),
                TopCategoryId = top?.Key, TopCategorySpentCents = top?.Sum(row => row.AmountCents) ?? 0,
                CreatedAt = VietnamTime.DayRange(day.Key).End, UpdatedAt = now });
        }
    }

    private static void AddGoals(FinMateDbContext db, Ledger ledger, string key, DateTimeOffset now)
    {
        var saved = key == "chi" ? 500_000L : 3_000_000L;
        var goal = new SavingGoal { Id = Id($"goal:{key}"), UserId = ledger.User.Id,
            Name = $"{Label} {(key == "chi" ? "Quỹ học tập" : "Quỹ dự phòng ba tháng")}", TargetCents = saved * 4,
            SavedCents = saved, Deadline = now.AddMonths(6), Status = SavingGoalStatus.Active, CreatedAt = now.AddMonths(-2), UpdatedAt = now };
        db.SavingGoals.Add(goal);
        for (var i = 0; i < 2; i++) db.GoalContributions.Add(new GoalContribution { Id = Id($"contribution:{key}:{i}"),
            SavingGoalId = goal.Id, UserId = ledger.User.Id, AmountCents = saved / 2,
            Note = $"{Label} Đánh dấu tiền để dành; tiền vẫn ở trong ví.", ContributedAt = now.AddDays(-35 + i * 20), CreatedAt = now.AddDays(-35 + i * 20) });
    }

    private static void AddRecurring(FinMateDbContext db, Ledger ledger, string key, long income,
        Dictionary<string, Category> categories, DateTimeOffset now)
    {
        var today = VietnamTime.Today();
        foreach (var (name, direction, amount, day, category) in new[] {
            ("Thu nhập tháng", RecurringDirection.Inflow, income, 5, "income"),
            ("Tiền thuê nhà", RecurringDirection.Outflow, key == "chi" ? 1_500_000L : 4_500_000L, 3, "housing") })
        {
            var due = new DateOnly(today.Year, today.Month, day);
            if (due < today) due = due.AddMonths(1);
            db.RecurringItems.Add(new RecurringItem { Id = Id($"recurring:{key}:{category}"), UserId = ledger.User.Id,
                Name = name, CounterpartyKey = CounterpartyKey.From(name), Direction = direction, AmountCents = amount,
                Cadence = RecurringCadence.Monthly, NextDueDate = due, CategoryId = categories[category].Id,
                FinancialAccountId = ledger.Bank.Id, Status = RecurringStatus.Active, Source = RecurringSource.Manual,
                Occurrences = 6, CreatedAt = now.AddMonths(-5), UpdatedAt = now });
        }
    }

    private static void AddDrafts(Ledger ledger, string key, Dictionary<string, Category> categories, DateTimeOffset now)
    {
        ledger.Transactions.Add(new Transaction { Id = Id($"draft:{key}"), UserId = ledger.User.Id,
            FinancialAccountId = ledger.Cash.Id, AmountCents = 65_000, CategoryId = categories["food"].Id,
            TransactionType = TransactionType.Debit, Source = TransactionSource.Manual, Status = TransactionStatus.Draft,
            Description = $"{Label} Bữa tối chờ xác nhận", MerchantName = "Bữa tối", TransactedAt = now.AddHours(-1),
            CreatedAt = now.AddHours(-1), UpdatedAt = now.AddHours(-1) });
    }
}
