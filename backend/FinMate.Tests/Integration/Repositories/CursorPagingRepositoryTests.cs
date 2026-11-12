using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FinMate.Tests.Integration.Repositories;

/// <summary>Một container Postgres cho cả lớp; mỗi test tự tạo người dùng/nhóm riêng nên không giẫm chân nhau.</summary>
public sealed class PagingDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("finmate_paging")
        .WithUsername("finmate")
        .WithPassword("finmate")
        .Build();

    public FinMateDbContext NewContext()
        => new(new DbContextOptionsBuilder<FinMateDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

/// <summary>
/// Chín danh sách phân trang con trỏ (keyset <c>(thời điểm, id)</c>, <c>KeysetCursor</c>) trên Postgres thật. Mỗi danh sách gieo 5 dòng, trong đó
/// 2 dòng TRÙNG mốc thời gian để đi qua nhánh phá hòa bằng <c>Id</c>, cộng 1 dòng của chủ khác. Đọc từng trang 2 dòng tới hết phải ra đúng 5 dòng,
/// không trùng, không sót, mới trước; con trỏ trang cuối là null; con trỏ hỏng trả về trang đầu thay vì lỗi.
/// Kèm job xóa nội dung thông báo quá 90 ngày (NFR-SEC-23), cũng cần Postgres thật vì chạy bằng <c>ExecuteUpdateAsync</c>.
/// </summary>
public class CursorPagingRepositoryTests : IClassFixture<PagingDbFixture>
{
    private const int PageSize = 2;

    // Mốc tròn giây: Postgres giữ tới micro giây, mốc có tick lẻ sẽ không khớp khi so bằng trong con trỏ.
    private static readonly DateTimeOffset Base = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Năm mốc thời gian, mốc thứ 2 và 3 trùng nhau.</summary>
    private static readonly DateTimeOffset[] Times =
        [Base.AddHours(-1), Base.AddHours(-2), Base.AddHours(-2), Base.AddHours(-3), Base.AddHours(-4)];

    private readonly PagingDbFixture _fixture;

    public CursorPagingRepositoryTests(PagingDbFixture fixture)
    {
        _fixture = fixture;
    }

    private delegate Task<(IEnumerable<Guid> Ids, string? Next)> PageReader(FinMateDbContext db, string? cursor, int limit);

    /// <summary>Đọc tới hết bằng con trỏ, so với thứ tự mong đợi; rồi thử con trỏ hỏng.</summary>
    private async Task AssertPagesAsync(PageReader read, IReadOnlyList<(Guid Id, DateTimeOffset At)> rows)
    {
        // Mới trước; trùng mốc thì Id lớn trước — so bằng thứ tự uuid của Postgres, chính là thứ tự câu ORDER BY dùng.
        await using var db = _fixture.NewContext();
        var expected = new List<Guid>();
        foreach (var group in rows.GroupBy(r => r.At).OrderByDescending(g => g.Key))
        {
            var ids = group.Select(r => r.Id).ToArray();
            var sorted = ids.Length == 1
                ? ids
                : await db.Database.SqlQueryRaw<Guid>(
                    "SELECT unnest(@p0::uuid[]) AS \"Value\" ORDER BY 1 DESC", new Npgsql.NpgsqlParameter("p0", ids)).ToArrayAsync();
            expected.AddRange(sorted);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var (ids, next) = await read(db, cursor, PageSize);
            var list = ids.ToList();
            list.Count.Should().BeLessThanOrEqualTo(PageSize);
            seen.AddRange(list);
            if (next is null)
            {
                break;
            }

            list.Should().HaveCount(PageSize, "chỉ trang cuối mới được ngắn hơn");
            cursor = next;
        }

        seen.Should().Equal(expected, "đủ 5 dòng của đúng chủ, không trùng, không sót, mới trước");

        var (badPage, _) = await read(db, "khong-phai-con-tro", PageSize);
        badPage.Should().Equal(expected.Take(PageSize), "con trỏ hỏng coi như không có, trả trang đầu");
    }

    private static async Task<Guid> UserAsync(FinMateDbContext db, string name = "Người dùng thử")
    {
        var id = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, id, $"{id:N}@finmate.local", name);
        return id;
    }

    private static async Task<Guid> WalletAsync(FinMateDbContext db, Guid userId)
    {
        var wallet = new FinancialAccount
        {
            Id = Guid.NewGuid(), UserId = userId, AccountType = AccountType.Cash, AccountName = "Tiền mặt",
            CreatedAt = Base, UpdatedAt = Base,
        };
        db.FinancialAccounts.Add(wallet);
        await db.SaveChangesAsync();
        return wallet.Id;
    }

    private static Transaction NewTransaction(Guid userId, Guid walletId, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = walletId, AmountCents = 50_000,
        TransactionType = TransactionType.Debit, Source = TransactionSource.Manual, Status = TransactionStatus.Confirmed,
        TransactedAt = at, CreatedAt = at, UpdatedAt = at,
    };

    private static async Task<Guid> GroupAsync(FinMateDbContext db)
    {
        var group = new Group { Id = Guid.NewGuid(), Name = "Nhà mình", CreatedAt = Base, UpdatedAt = Base };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    [Fact]
    public async Task ForecastAlerts()
    {
        await using var db = _fixture.NewContext();
        var user = await UserAsync(db);
        var other = await UserAsync(db);
        ForecastAlert Alert(Guid owner, DateTimeOffset at) => new()
        {
            Id = Guid.NewGuid(), UserId = owner, Kind = ForecastAlertKind.LowBalance, DedupKey = Guid.NewGuid().ToString(),
            Title = "Số dư thấp", Body = "-", Status = ForecastAlertStatus.Sent, ScheduledFor = at, SentAt = at, CreatedAt = at,
        };
        var rows = Times.Select(at => Alert(user, at)).ToList();
        db.ForecastAlerts.AddRange(rows);
        db.ForecastAlerts.Add(Alert(other, Base));
        await db.SaveChangesAsync();

        await AssertPagesAsync(
            async (c, cursor, limit) =>
            {
                var page = await new ForecastAlertRepository(c).ListForUserAsync(user, cursor, limit);
                return (page.Items.Select(a => a.Id), page.NextCursor);
            },
            rows.Select(r => (r.Id, r.CreatedAt)).ToList());
    }

    [Fact]
    public async Task Anomalies()
    {
        await using var db = _fixture.NewContext();
        var user = await UserAsync(db);
        var wallet = await WalletAsync(db, user);
        var other = await UserAsync(db);
        var otherWallet = await WalletAsync(db, other);
        TransactionAnomaly Anomaly(Guid owner, Transaction t, DateTimeOffset at) => new()
        {
            Id = Guid.NewGuid(), UserId = owner, TransactionId = t.Id, Kind = AnomalyKind.Duplicate, Severity = AnomalySeverity.Normal,
            AmountCents = t.AmountCents, TypicalCents = t.AmountCents, RatioPercent = 100, CreatedAt = at,
        };
        var rows = new List<TransactionAnomaly>();
        foreach (var at in Times)
        {
            var t = NewTransaction(user, wallet, at);
            db.Transactions.Add(t);
            rows.Add(Anomaly(user, t, at));
        }

        var otherTx = NewTransaction(other, otherWallet, Base);
        db.Transactions.Add(otherTx);
        db.TransactionAnomalies.AddRange(rows);
        db.TransactionAnomalies.Add(Anomaly(other, otherTx, Base));
        await db.SaveChangesAsync();

        await AssertPagesAsync(
            async (c, cursor, limit) =>
            {
                var page = await new AnomalyRepository(c).ListAsync(user, openOnly: false, cursor, limit);
                return (page.Items.Select(a => a.Id), page.NextCursor);
            },
            rows.Select(r => (r.Id, r.CreatedAt)).ToList());
    }

    [Fact]
    public async Task GroupSharedTransactions()
    {
        await using var db = _fixture.NewContext();
        var user = await UserAsync(db);
        var wallet = await WalletAsync(db, user);
        var group = await GroupAsync(db);
        var otherGroup = await GroupAsync(db);
        Transaction Shared(Guid groupId, DateTimeOffset at)
        {
            var t = NewTransaction(user, wallet, at);
            t.GroupId = groupId;
            t.SharedAt = at;
            return t;
        }

        var rows = Times.Select(at => Shared(group, at)).ToList();
        db.Transactions.AddRange(rows);
        db.Transactions.Add(Shared(otherGroup, Base.AddHours(-1)));
        await db.SaveChangesAsync();

        await AssertPagesAsync(
            async (c, cursor, limit) =>
            {
                var page = await new GroupFinanceRepository(c).ListSharedTransactionsAsync(
                    group, null, Base.AddDays(-1), Base.AddDays(1), cursor, limit);
                return (page.Items.Select(v => v.Transaction.Id), page.NextCursor);
            },
            rows.Select(r => (r.Id, r.TransactedAt)).ToList());
    }

    [Fact]
    public async Task GroupGoalContributions()
    {
        await using var db = _fixture.NewContext();
        var user = await UserAsync(db);
        var group = await GroupAsync(db);
        SavingGoal Goal() => new() { Id = Guid.NewGuid(), GroupId = group, Name = "Du lịch", TargetCents = 10_000_000, CreatedAt = Base, UpdatedAt = Base };
        var goal = Goal();
        var otherGoal = Goal();
        db.SavingGoals.AddRange(goal, otherGoal);
        GoalContribution Contribution(Guid goalId, DateTimeOffset at) => new()
        {
            Id = Guid.NewGuid(), SavingGoalId = goalId, UserId = user, AmountCents = 100_000, ContributedAt = at, CreatedAt = at,
        };
        var rows = Times.Select(at => Contribution(goal.Id, at)).ToList();
        db.GoalContributions.AddRange(rows);
        db.GoalContributions.Add(Contribution(otherGoal.Id, Base));
        await db.SaveChangesAsync();

        await AssertPagesAsync(
            async (c, cursor, limit) =>
            {
                var page = await new GroupFinanceRepository(c).ListContributionsAsync(goal.Id, cursor, limit);
                return (page.Items.Select(v => v.Contribution.Id), page.NextCursor);
            },
            rows.Select(r => (r.Id, r.ContributedAt)).ToList());
    }

    [Fact]
    public async Task GroupActivities()
    {
        await using var db = _fixture.NewContext();
        var group = await GroupAsync(db);
        var otherGroup = await GroupAsync(db);
        GroupActivity Activity(Guid groupId, DateTimeOffset at) => new()
        {
            Id = Guid.NewGuid(), GroupId = groupId, Type = GroupActivityType.GroupUpdated, CreatedAt = at,
        };
        var rows = Times.Select(at => Activity(group, at)).ToList();
        db.GroupActivities.AddRange(rows);
        db.GroupActivities.Add(Activity(otherGroup, Base));
        await db.SaveChangesAsync();

        await AssertPagesAsync(
            async (c, cursor, limit) =>
            {
                var page = await new GroupRepository(c).ListActivitiesAsync(group, cursor, limit);
                return (page.Items.Select(a => a.Id), page.NextCursor);
            },
            rows.Select(r => (r.Id, r.CreatedAt)).ToList());
    }

    [Fact]
    public async Task QrTransfers()
    {
        await using var db = _fixture.NewContext();
        var user = await UserAsync(db);
        var other = await UserAsync(db);
        QrTransferIntent Intent(Guid owner, DateTimeOffset at) => new()
        {
            Id = Guid.NewGuid(), UserId = owner, ReceiverBankBin = "970436", ReceiverAccountNumber = "0123456789",
            SenderBankBin = "970422", AmountCents = 120_000, ScannedAt = at, CreatedAt = at,
        };
        var rows = Times.Select(at => Intent(user, at)).ToList();
        db.QrTransferIntents.AddRange(rows);
        db.QrTransferIntents.Add(Intent(other, Base));
        await db.SaveChangesAsync();

        await AssertPagesAsync(
            async (c, cursor, limit) =>
            {
                var page = await new QrTransferRepository(c).ListIntentsAsync(user, null, cursor, limit);
                return (page.Items.Select(i => i.Id), page.NextCursor);
            },
            rows.Select(r => (r.Id, r.ScannedAt)).ToList());
    }

    [Fact]
    public async Task SubscriptionOrders()
    {
        await using var db = _fixture.NewContext();
        var user = await UserAsync(db);
        var other = await UserAsync(db);
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(), Code = $"test_{Guid.NewGuid():N}"[..20], Name = "Gói thử", MonthlyPriceCents = 49_000,
            IsActive = true, CreatedAt = Base, UpdatedAt = Base,
        };
        db.SubscriptionPlans.Add(plan);
        SubscriptionOrder Order(Guid owner, DateTimeOffset at) => new()
        {
            Id = Guid.NewGuid(), InvoiceNumber = $"INV{Guid.NewGuid():N}"[..20], UserId = owner, PlanId = plan.Id, PlanCode = plan.Code,
            PlanName = plan.Name, Cycle = SubscriptionCycle.Monthly, AmountCents = 49_000, Status = SubscriptionOrderStatus.Pending,
            CreatedAt = at, ExpiresAt = at.AddMinutes(30), NextCheckAt = at.AddMinutes(5),
        };
        var rows = Times.Select(at => Order(user, at)).ToList();
        db.SubscriptionOrders.AddRange(rows);
        db.SubscriptionOrders.Add(Order(other, Base));
        await db.SaveChangesAsync();

        await AssertPagesAsync(
            async (c, cursor, limit) =>
            {
                var page = await new SubscriptionRepository(c).ListUserOrdersAsync(user, cursor, limit);
                return (page.Items.Select(o => o.Id), page.NextCursor);
            },
            rows.Select(r => (r.Id, r.CreatedAt)).ToList());
    }

    private static SupportTicket Ticket(Guid requester, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(), RequesterUserId = requester, Subject = "Không thấy giao dịch",
        FirstResponseDueAt = at.AddHours(4), ResolveDueAt = at.AddDays(2), CreatedAt = at, UpdatedAt = at,
    };

    [Fact]
    public async Task SupportTickets()
    {
        await using var db = _fixture.NewContext();
        var user = await UserAsync(db);
        var other = await UserAsync(db);
        var rows = Times.Select(at => Ticket(user, at)).ToList();
        db.SupportTickets.AddRange(rows);
        db.SupportTickets.Add(Ticket(other, Base));
        await db.SaveChangesAsync();

        await AssertPagesAsync(
            async (c, cursor, limit) =>
            {
                var page = await new SupportRepository(c).ListForRequesterAsync(user, cursor, limit);
                return (page.Items.Select(t => t.Id), page.NextCursor);
            },
            rows.Select(r => (r.Id, r.CreatedAt)).ToList());
    }

    [Fact]
    public async Task SupportAccessLogs()
    {
        await using var db = _fixture.NewContext();
        var user = await UserAsync(db);
        var other = await UserAsync(db);
        async Task<(Guid Ticket, Guid Grant)> GrantAsync(Guid customer)
        {
            var ticket = Ticket(customer, Base.AddDays(-1));
            var grant = new SupportAccessGrant
            {
                Id = Guid.NewGuid(), TicketId = ticket.Id, Scopes = SupportAccessScope.Transactions, Reason = "Kiểm tra giao dịch",
                Status = SupportAccessStatus.Approved, CreatedAt = ticket.CreatedAt, RequestExpiresAt = ticket.CreatedAt.AddDays(1),
            };
            db.SupportTickets.Add(ticket);
            db.SupportAccessGrants.Add(grant);
            await db.SaveChangesAsync();
            return (ticket.Id, grant.Id);
        }

        var mine = await GrantAsync(user);
        var theirs = await GrantAsync(other);
        SupportDataAccessLog Log(Guid customer, (Guid Ticket, Guid Grant) g, DateTimeOffset at) => new()
        {
            Id = Guid.NewGuid(), TicketId = g.Ticket, GrantId = g.Grant, CustomerUserId = customer,
            Scope = SupportAccessScope.Transactions, CreatedAt = at,
        };
        var rows = Times.Select(at => Log(user, mine, at)).ToList();
        db.SupportDataAccessLogs.AddRange(rows);
        db.SupportDataAccessLogs.Add(Log(other, theirs, Base));
        await db.SaveChangesAsync();

        await AssertPagesAsync(
            async (c, cursor, limit) =>
            {
                var page = await new SupportRepository(c).ListAccessLogsAsync(user, cursor, limit);
                return (page.Items.Select(l => l.Id), page.NextCursor);
            },
            rows.Select(r => (r.Id, r.CreatedAt)).ToList());
    }

    // ---- NFR-SEC-23: nội dung gốc thông báo chỉ giữ 90 ngày ----

    [Fact]
    public async Task ThePurgeJobErasesOnlyTheBodyOfLogsOlderThanNinetyDays()
    {
        await using var db = _fixture.NewContext();
        var user = await UserAsync(db);
        var now = new DateTimeOffset(2026, 11, 12, 20, 15, 0, TimeSpan.Zero);
        NotificationLog Log(int daysAgo, string? body) => new()
        {
            Id = Guid.NewGuid(), UserId = user, PackageName = "com.mbmobile", NotificationTitle = "MB Bank", NotificationBody = body,
            ContentHash = Guid.NewGuid().ToString("N"), ReceivedAt = now.AddDays(-daysAgo), Status = NotificationLogStatus.Processed,
            CreatedAt = now.AddDays(-daysAgo),
        };
        var old = Log(91, "TK 123: -75,000VND tai Highlands");
        var recent = Log(89, "TK 123: -30,000VND tai Grab");
        var alreadyEmpty = Log(200, null);
        db.NotificationLogs.AddRange(old, recent, alreadyEmpty);
        await db.SaveChangesAsync();

        // Đếm trên đúng người dùng này: các test khác trong lớp không tạo log, nhưng không dựa vào điều đó để đọc kết quả.
        await using (var jobDb = _fixture.NewContext())
        {
            await new NotificationBodyPurgeJob(new NotificationLogRepository(jobDb), NullLogger<NotificationBodyPurgeJob>.Instance)
                .RunAsync(now, CancellationToken.None);
        }

        await using var check = _fixture.NewContext();
        var after = await check.NotificationLogs.AsNoTracking().Where(l => l.UserId == user).ToDictionaryAsync(l => l.Id);
        after[old.Id].NotificationBody.Should().BeNull();
        after[old.Id].NotificationTitle.Should().Be("MB Bank", "chỉ xóa nội dung, tiêu đề giữ nguyên");
        after[old.Id].ContentHash.Should().Be(old.ContentHash, "dedup vẫn cần hash");
        after[old.Id].Status.Should().Be(NotificationLogStatus.Processed);
        after[recent.Id].NotificationBody.Should().Be("TK 123: -30,000VND tai Grab");
        after[alreadyEmpty.Id].NotificationBody.Should().BeNull();
    }

    [Fact]
    public async Task ThePurgeCountsOnlyRowsThatStillHadABody()
    {
        await using var db = _fixture.NewContext();
        var user = await UserAsync(db);
        var cutoff = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);   // trước mọi dòng do test khác tạo
        NotificationLog Log(string? body) => new()
        {
            Id = Guid.NewGuid(), UserId = user, PackageName = "com.mbmobile", NotificationBody = body,
            ContentHash = Guid.NewGuid().ToString("N"), ReceivedAt = cutoff.AddDays(-1), Status = NotificationLogStatus.Processed,
            CreatedAt = cutoff.AddDays(-1),
        };
        db.NotificationLogs.AddRange(Log("có nội dung"), Log("có nội dung"), Log(null));
        await db.SaveChangesAsync();

        await using var jobDb = _fixture.NewContext();
        var repo = new NotificationLogRepository(jobDb);
        (await repo.PurgeBodiesOlderThanAsync(cutoff)).Should().Be(2);
        (await repo.PurgeBodiesOlderThanAsync(cutoff)).Should().Be(0, "chạy lại không đụng dòng đã xóa");
    }
}
