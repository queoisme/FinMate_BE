using FinMate.Application.Kpi;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;

namespace FinMate.Tests.Integration.Kpi;

/// <summary>
/// Kịch bản 3 tuần cho dashboard KPI (Phase 94, §5 của kế hoạch). <see cref="Day0"/> là thứ Hai; 21 ngày VN, ba tuần trọn.
/// 20 user thường u0…u19 đăng ký ngày i (u3 lúc 00:30 giờ VN, tức 17:30 UTC hôm trước), cộng một Staff và một OpsManager có hoạt động
/// (không được tính). Số mong đợi viết tay trong <see cref="Expected"/>, không tính lại bằng code.
/// </summary>
public sealed class KpiScenario
{
    public KpiScenario(DateOnly day0)
    {
        Day0 = day0;
        Tag = Guid.NewGuid().ToString("N")[..8];
    }

    public DateOnly Day0 { get; }
    public DateOnly LastDay => Day0.AddDays(20);
    public string Tag { get; }
    public string ProPlan => $"pro_{Tag}";
    public string MaxPlan => $"max_{Tag}";
    public List<Guid> UserIds { get; } = [];
    public List<string> Emails { get; } = [];

    /// <summary>Đơn vị đồng; chỉ để quét response không chứa số tiền của từng đơn.</summary>
    public const long ProPrice = 99_017;
    public const long MaxPrice = 199_023;

    /// <summary>Theo tuần 1, 2, 3.</summary>
    public static class Expected
    {
        public static readonly long[] Signups = [7, 7, 6];
        public static readonly long[] EmailVerified = [5, 5, 5];
        /// <summary>Theo tuần đăng ký: u0, u2, u4, u6 | u8, u10, u12 | u14, u16, u18 (u19 bỏ qua).</summary>
        public static readonly long[] OnboardingCompleted = [4, 3, 3];
        public static readonly long[] MethodChosen = [1, 2, 0];
        public static readonly long[] DeletionRequests = [0, 0, 1];
        public static readonly long[] TransactingUsers = [2, 2, 2];
        public static readonly long[] TransactionsTotal = [7, 4, 2];
        public static readonly long[] NotificationsReceived = [0, 6, 0];
        public static readonly long[] PayingTotal = [1, 3, 3];
        public static readonly long[] PaidOrders = [1, 2, 0];
        public static readonly long[] Revenue = [ProPrice, ProPrice + MaxPrice, 0];
        public static readonly long[] NeedsReview = [0, 0, 1];
    }

    private DateTimeOffset At(int day, double hours = 12) => KpiPeriods.StartOfDayUtc(Day0.AddDays(day)).AddHours(hours);

    public async Task SeedAsync(FinMateDbContext db)
    {
        var method = new FinanceMethod { Id = Guid.NewGuid(), Code = $"kpi_{Tag}", Name = "Phương pháp thử", CreatedAt = At(0), UpdatedAt = At(0) };
        db.FinanceMethods.Add(method);
        var pro = Plan(ProPlan, ProPrice);
        var max = Plan(MaxPlan, MaxPrice);
        db.SubscriptionPlans.AddRange(pro, max);

        var users = Enumerable.Range(0, 20).Select(i => AddUser(db, UserRole.User, i, i == 3 ? 0.5 : 9)).ToList();
        var staff = AddUser(db, UserRole.Staff, 2, 9);
        var manager = AddUser(db, UserRole.OpsManager, 2, 9);

        SeedProfile(db, users, method);
        SeedTransactionsAndNotifications(db, users, staff);
        SeedSubscriptions(db, users, manager, pro, max);
        SeedFeatures(db, users, staff);
        await db.SaveChangesAsync();
    }

    private User AddUser(FinMateDbContext db, UserRole role, int day, double hours)
    {
        var id = Guid.NewGuid();
        var email = $"kpi{Tag}{id:N}@finmate.local";
        var user = new User
        {
            Id = id, Email = email, DisplayName = $"Người {Tag}", Role = role, CreatedAt = At(day, hours), UpdatedAt = At(day, hours),
            EmailVerifiedAt = role != UserRole.User || day % 4 != 0 ? At(day, hours) : null,
        };
        db.Users.Add(user);
        UserIds.Add(id);
        Emails.Add(email);
        return user;
    }

    private void SeedProfile(FinMateDbContext db, List<User> users, FinanceMethod method)
    {
        for (var i = 0; i < 20; i += 2)
        {
            users[i].OnboardingStatus = OnboardingStatus.Completed;
            users[i].OnboardingCompletedAt = At(i + 1);
        }

        users[19].OnboardingStatus = OnboardingStatus.Skipped;   // bỏ qua cũng có mốc thời gian nhưng không phải hoàn thành
        users[19].OnboardingCompletedAt = At(19, 13);

        foreach (var i in new[] { 1, 5, 9 })
        {
            db.UserMethods.Add(Method(users[i].Id, method, At(i + 2)));
        }

        var second = Method(users[1].Id, method, At(10));   // lần chọn thứ hai (phương pháp hỗ trợ) không tính
        second.Role = UserMethodRole.Supporting;
        db.UserMethods.Add(second);

        users[7].DeletedAt = At(15);
        db.DataDeletionRequests.Add(new DataDeletionRequest { Id = Guid.NewGuid(), UserId = users[7].Id, RequestedAt = At(15), ScheduledHardDeleteAt = At(45) });
    }

    private static UserMethod Method(Guid userId, FinanceMethod method, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, MethodId = method.Id, MethodVersion = 1, ChosenVia = MethodChoiceSource.Manual,
        StartedAt = at, CreatedAt = at, UpdatedAt = at, Status = UserMethodStatus.Active,
    };

    private void SeedTransactionsAndNotifications(FinMateDbContext db, List<User> users, User staff)
    {
        var wallets = new Dictionary<Guid, Guid>();
        Guid Wallet(User u)
        {
            if (!wallets.TryGetValue(u.Id, out var w))
            {
                w = Guid.NewGuid();
                db.FinancialAccounts.Add(new FinancialAccount { Id = w, UserId = u.Id, AccountType = AccountType.Cash, AccountName = "Ví", CreatedAt = At(0), UpdatedAt = At(0) });
                wallets[u.Id] = w;
            }

            return w;
        }

        Transaction Tx(User u, int day, TransactionSource source, TransactionStatus status = TransactionStatus.Confirmed, bool deleted = false, Guid? log = null)
        {
            var t = new Transaction
            {
                Id = Guid.NewGuid(), UserId = u.Id, FinancialAccountId = Wallet(u), AmountCents = 50_000, TransactionType = TransactionType.Debit,
                Source = source, Status = status, TransactedAt = At(day), CreatedAt = At(day), UpdatedAt = At(day),
                DeletedAt = deleted ? At(day, 13) : null, NotificationLogId = log,
            };
            db.Transactions.Add(t);
            return t;
        }

        // Tuần 1: u1 bốn ngày tay, u2 giọng nói + hoá đơn + tay → 7 giao dịch, manual 5, 2 người.
        foreach (var d in new[] { 2, 3, 5, 6 })
        {
            Tx(users[1], d, TransactionSource.Manual);
        }

        Tx(users[2], 4, TransactionSource.Voice);
        Tx(users[2], 4, TransactionSource.Receipt);
        Tx(users[2], 6, TransactionSource.Manual);
        Tx(staff, 4, TransactionSource.Manual);

        // Tuần 2: u3 hoá đơn, u5 tay + thông báo, u6 chỉ thông báo (không tính vào transactingUsers).
        Tx(users[3], 9, TransactionSource.Receipt);
        Tx(users[5], 9, TransactionSource.Manual);
        Tx(users[5], 9, TransactionSource.Notification, log: Log(db, users[5], 9, NotificationLogStatus.Processed));
        Tx(users[6], 10, TransactionSource.Notification, log: Log(db, users[6], 10, NotificationLogStatus.Processed));
        Log(db, users[6], 10, NotificationLogStatus.Ignored);
        Log(db, users[6], 11, NotificationLogStatus.Duplicate);
        Log(db, users[5], 11, NotificationLogStatus.Failed);
        Tx(users[5], 12, TransactionSource.Notification, deleted: true, log: Log(db, users[5], 12, NotificationLogStatus.Processed));
        Log(db, staff, 10, NotificationLogStatus.Processed);

        // Tuần 3: u8 tay (+ một nháp và một đã xoá, không tính), u10 hỗ trợ.
        Tx(users[8], 16, TransactionSource.Manual);
        Tx(users[8], 16, TransactionSource.Manual, TransactionStatus.Draft);
        Tx(users[8], 16, TransactionSource.Manual, deleted: true);
        Tx(users[10], 17, TransactionSource.Support);
    }

    private Guid Log(FinMateDbContext db, User u, int day, NotificationLogStatus status)
    {
        var id = Guid.NewGuid();
        db.NotificationLogs.Add(new NotificationLog
        {
            Id = id, UserId = u.Id, PackageName = "com.vcb", ContentHash = $"h{id:N}", ReceivedAt = At(day), Status = status, CreatedAt = At(day),
        });
        return id;
    }

    private SubscriptionPlan Plan(string code, long price) => new()
    {
        Id = Guid.NewGuid(), Code = code, Name = code, MonthlyPriceCents = price, IsActive = true, CreatedAt = At(0), UpdatedAt = At(0),
    };

    private void SeedSubscriptions(FinMateDbContext db, List<User> users, User manager, SubscriptionPlan pro, SubscriptionPlan max)
    {
        Paid(db, users[1], pro, 5, withPeriod: true);
        Paid(db, users[2], pro, 8, withPeriod: true);
        Paid(db, users[3], max, 12, withPeriod: true);
        Paid(db, users[7], pro, -10, withPeriod: true, days: 90);   // đang chờ xoá: không tính vào người trả phí
        Paid(db, manager, pro, 10, withPeriod: true);              // tài khoản nội bộ: không tính
        Order(db, users[4], pro, SubscriptionOrderStatus.Voided, 13, paid: true);
        Order(db, users[9], pro, SubscriptionOrderStatus.NeedsReview, 14, paid: false);
    }

    private void Paid(FinMateDbContext db, User u, SubscriptionPlan plan, int day, bool withPeriod, int days = 30)
    {
        var order = Order(db, u, plan, SubscriptionOrderStatus.Paid, day, paid: true);
        if (withPeriod)
        {
            db.UserSubscriptionPeriods.Add(new UserSubscriptionPeriod
            {
                Id = Guid.NewGuid(), UserId = u.Id, OrderId = order.Id, PlanCode = plan.Code, PlanName = plan.Name,
                StartsAt = At(day), EndsAt = At(day + days), CreatedAt = At(day),
            });
        }
    }

    private SubscriptionOrder Order(FinMateDbContext db, User u, SubscriptionPlan plan, SubscriptionOrderStatus status, int day, bool paid)
    {
        var order = new SubscriptionOrder
        {
            Id = Guid.NewGuid(), InvoiceNumber = $"INV{Guid.NewGuid():N}"[..20], UserId = u.Id, PlanId = plan.Id, PlanCode = plan.Code, PlanName = plan.Name,
            Cycle = SubscriptionCycle.Monthly, AmountCents = plan.MonthlyPriceCents!.Value, Status = status,
            CreatedAt = At(day, 10), ExpiresAt = At(day, 11), NextCheckAt = At(day, 10.5), PaidAt = paid ? At(day) : null,
            VoidedAt = status == SubscriptionOrderStatus.Voided ? At(day, 14) : null,
        };
        db.SubscriptionOrders.Add(order);
        return order;
    }

    /// <summary>Ảnh chụp tính năng: ngân sách 1 (u1), mục tiêu 2 (u1 đang làm, u4 nháp), sổ vay 1 (u5 còn nợ), nhóm 0.</summary>
    private void SeedFeatures(FinMateDbContext db, List<User> users, User staff)
    {
        db.Budgets.Add(Budget(users[1].Id, deleted: false));
        db.Budgets.Add(Budget(users[2].Id, deleted: true));
        db.Budgets.Add(Budget(users[7].Id, deleted: false));   // chờ xoá
        db.Budgets.Add(Budget(staff.Id, deleted: false));      // nội bộ
        db.SavingGoals.Add(Goal(users[1].Id, SavingGoalStatus.Active));
        db.SavingGoals.Add(Goal(users[3].Id, SavingGoalStatus.Completed));
        db.SavingGoals.Add(Goal(users[4].Id, SavingGoalStatus.Draft));
        db.Loans.Add(Loan(users[5].Id, LoanStatus.Open));
        db.Loans.Add(Loan(users[6].Id, LoanStatus.Settled));
    }

    private Budget Budget(Guid userId, bool deleted) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, LimitCents = 1_000_000, CreatedAt = At(1), UpdatedAt = At(1), DeletedAt = deleted ? At(2) : null,
    };

    private SavingGoal Goal(Guid userId, SavingGoalStatus status) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Name = "Mục tiêu", TargetCents = 1_000_000, Status = status, CreatedAt = At(1), UpdatedAt = At(1),
        CompletedAt = status == SavingGoalStatus.Completed ? At(3) : null,
    };

    private Loan Loan(Guid userId, LoanStatus status) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Direction = LoanDirection.Borrow, Title = "Vay", Counterparty = "Bạn", PrincipalCents = 500_000,
        StartDate = Day0, Status = status, RepaidCents = status == LoanStatus.Settled ? 500_000 : 0, SettledAt = status == LoanStatus.Settled ? At(5) : null,
        CreatedAt = At(1), UpdatedAt = At(1),
    };
}
