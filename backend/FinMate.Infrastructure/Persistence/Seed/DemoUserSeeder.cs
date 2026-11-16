using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Người dùng demo cho môi trường DEV: một ví tiền mặt, sáu tháng giao dịch đã xác nhận có quy luật (lương, tiền trọ, hóa đơn, ăn uống, đi lại, mua sắm,
/// giải trí) và hai khoản định kỳ. Có vậy "mua món này có ổn không" của agent mới chạy trên dữ liệu thật thay vì trả "chưa đủ dữ liệu", mà không cần
/// đụng tới dữ liệu của người dùng thật. Email và mật khẩu nằm trong cấu hình (<c>DEV_SEED_DEMO_EMAIL</c>, <c>DEV_SEED_DEMO_PASSWORD</c>), không nằm trong mã.
/// Chỉ được gọi khi chạy ở Development (xem Program.cs); mật khẩu ở đây bỏ qua chính sách đăng ký. Dữ liệu xác định, không ngẫu nhiên: chạy lại cho cùng
/// kết quả. Chỉ TẠO khi tài khoản chưa có, không bao giờ ghi đè dữ liệu của tài khoản đã có.
/// </summary>
public static class DemoUserSeeder
{
    public const string EmailKey = "DEV_SEED_DEMO_EMAIL";
    public const string PasswordKey = "DEV_SEED_DEMO_PASSWORD";

    private const int HistoryMonths = 6;
    private const long OpeningBalanceCents = 5_000_000;
    private const long SalaryCents = 15_000_000;
    private const long RentCents = 3_500_000;
    private const string SalaryName = "Lương công ty";
    private const string RentName = "Tiền trọ";

    /// <returns>True khi vừa tạo tài khoản; false khi chưa cấu hình hoặc tài khoản đã có.</returns>
    public static async Task<bool> SeedAsync(FinMateDbContext context, IPasswordHasher passwordHasher, IConfiguration configuration, CancellationToken ct = default)
    {
        var email = configuration[EmailKey];
        var password = configuration[PasswordKey];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)
            || await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var today = VietnamTime.Today();
        var categories = await context.Categories.AsNoTracking().Where(c => c.UserId == null).ToDictionaryAsync(c => c.Slug, c => c.Id, ct);
        Guid? Category(string slug) => categories.TryGetValue(slug, out var id) ? id : null;

        var user = new User
        {
            Id = Guid.NewGuid(), Email = email, PasswordHash = passwordHasher.Hash(password), DisplayName = "Demo", Role = UserRole.User,
            EmailVerifiedAt = now, MonthlyIncomeCents = SalaryCents, CreatedAt = now, UpdatedAt = now,
        };
        var wallet = new FinancialAccount
        {
            Id = Guid.NewGuid(), UserId = user.Id, AccountType = AccountType.Cash, AccountName = "Ví demo", CreatedAt = now, UpdatedAt = now,
        };

        var transactions = new List<Transaction>();
        void Add(DateOnly day, TransactionType type, long amount, string category, string merchant)
        {
            if (day >= today)
            {
                return;   // chỉ có lịch sử tới hôm qua: hôm nay và tương lai chưa xảy ra
            }

            transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(), UserId = user.Id, FinancialAccountId = wallet.Id, CategoryId = Category(category), AmountCents = amount, TransactionType = type,
                Source = TransactionSource.Manual, Status = TransactionStatus.Confirmed, MerchantName = merchant,
                TransactedAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), VietnamTime.Offset), CreatedAt = now, UpdatedAt = now,
            });
        }

        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-HistoryMonths);
        for (var day = firstMonth; day < today; day = day.AddDays(1))
        {
            switch (day.Day)
            {
                case 3: Add(day, TransactionType.Debit, RentCents, "housing", RentName); break;
                case 5: Add(day, TransactionType.Credit, SalaryCents, "income", SalaryName); break;
                case 10: Add(day, TransactionType.Debit, 250_000, "bills", "Internet"); break;
                case 15: Add(day, TransactionType.Debit, 1_500_000, "entertainment", "Du lịch cuối tuần"); break;
                case 20: Add(day, TransactionType.Debit, 2_200_000, "shopping", "Mua sắm"); break;
            }

            Add(day, TransactionType.Debit, 45_000 + ((day.Day % 5) * 10_000), "food", "Quán cơm");
            if (day.DayOfWeek == DayOfWeek.Monday)
            {
                Add(day, TransactionType.Debit, 200_000, "transport", "Xăng xe");
            }
        }

        wallet.BalanceCents = OpeningBalanceCents + transactions.Sum(t => t.TransactionType == TransactionType.Credit ? t.AmountCents : -t.AmountCents);

        var items = new[]
        {
            Recurring(user.Id, wallet.Id, RecurringDirection.Outflow, RentName, RentCents, NextOccurrence(today, 3), Category("housing"), now),
            Recurring(user.Id, wallet.Id, RecurringDirection.Inflow, SalaryName, SalaryCents, NextOccurrence(today, 5), Category("income"), now),
        };

        context.Users.Add(user);
        context.FinancialAccounts.Add(wallet);
        context.Transactions.AddRange(transactions);
        context.RecurringItems.AddRange(items);
        await context.SaveChangesAsync(ct);
        return true;
    }

    private static DateOnly NextOccurrence(DateOnly today, int dayOfMonth)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, dayOfMonth);
        return thisMonth >= today ? thisMonth : thisMonth.AddMonths(1);
    }

    private static RecurringItem Recurring(Guid userId, Guid accountId, RecurringDirection direction, string name, long amount, DateOnly nextDue, Guid? categoryId, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(), UserId = userId, Direction = direction, Name = name, CounterpartyKey = CounterpartyKey.From(name), CategoryId = categoryId, FinancialAccountId = accountId,
            AmountCents = amount, Cadence = RecurringCadence.Monthly, NextDueDate = nextDue, Status = RecurringStatus.Active, Source = RecurringSource.Manual,
            Occurrences = HistoryMonths, CreatedAt = now, UpdatedAt = now,
        };
}
