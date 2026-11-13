using System.Net;
using System.Net.Http.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 82, BR-DUP-11/13 và D6–D10: sau khi gộp, mọi chỗ tổng hợp chỉ thấy MỘT khoản: báo cáo tháng, cơ cấu danh mục, timeline, ngân sách
/// cá nhân và nhóm (kể cả khi survivor đổi tháng), tóm tắt ngày đã chốt, dữ liệu dự báo, bất thường; và mọi tham chiếu tới bản bị gộp đi theo survivor.
/// </summary>
[Collection("Integration")]
public class MergeAggregatesTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const long Price = 50_000;
    private readonly AuthApiFactory _factory;
    private readonly DuplicateApi _api;

    public MergeAggregatesTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new DuplicateApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private record Monthly(long TotalSpentCents, long TotalIncomeCents, int TransactionCount);
    private record BreakdownItem(Guid? CategoryId, long SpentCents);
    private record Breakdown(long TotalSpentCents, List<BreakdownItem> Items);
    private record TimelineDay(DateOnly Date, long TotalSpentCents, List<RefundApi.TransactionData> Transactions);
    private record GroupBudget(Guid Id, long SpentCents);

    private async Task<T> GetAsync<T>(RefundApi.Session s, string url)
    {
        var response = await _api.Base.SendAsync(HttpMethod.Get, url, s.Token);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<T>>(RefundApi.JsonOptions))!.Data;
    }

    private Task<Guid> UserIdAsync(RefundApi.Session s)
        => _api.Base.WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync());

    /// <summary>Khoản chat đã xác nhận ở <paramref name="chatAt"/>, rồi nháp thông báo cùng số tiền ở <paramref name="bankAt"/>, trỏ tới khoản chat.</summary>
    private async Task<(Guid Chat, Guid Bank)> ChatThenBankAsync(RefundApi.Session s, DateTimeOffset chatAt, DateTimeOffset bankAt, Guid? bankAccount = null)
    {
        var chat = await _api.Base.SpendAsync(s, Price, chatAt, s.Food, "trà sữa");
        var bank = await _api.InsertAsync(s, Price, bankAt, account: bankAccount, category: s.Food, duplicateOf: chat.Id, duplicateStatus: DuplicateStatus.Suspected);
        return (chat.Id, bank);
    }

    [Fact]
    public async Task Reports_SeeOneTransaction()
    {
        var s = await _api.Base.StartAsync();
        var (chat, bank) = await ChatThenBankAsync(s, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(-1));
        await _api.Base.ConfirmAsync(s, bank);   // người dùng lỡ xác nhận cả hai: báo cáo đang đếm hai lần
        (await GetAsync<Monthly>(s, "/api/v1/reports/monthly-summary")).TotalSpentCents.Should().Be(2 * Price);

        (await _api.MergeAsync(s, bank, chat)).StatusCode.Should().Be(HttpStatusCode.OK);

        var monthly = await GetAsync<Monthly>(s, "/api/v1/reports/monthly-summary");
        monthly.TotalSpentCents.Should().Be(Price);
        monthly.TransactionCount.Should().Be(1);
        var breakdown = await GetAsync<Breakdown>(s, "/api/v1/reports/category-breakdown");
        breakdown.Items.Should().ContainSingle().Which.SpentCents.Should().Be(Price);
        var days = await GetAsync<List<TimelineDay>>(s, "/api/v1/reports/timeline");
        days.SelectMany(d => d.Transactions).Should().ContainSingle().Which.Id.Should().Be(chat);
    }

    [Fact]
    public async Task SurvivorMovingToAnotherMonth_RecomputesBothBudgetPeriods_AndBothDailySummaries()
    {
        var s = await _api.Base.StartAsync();
        var budget = await _api.Base.BudgetAsync(s, 1_000_000, s.Food);
        var chatAt = DateTimeOffset.UtcNow.AddDays(-40);
        var bankAt = DateTimeOffset.UtcNow.AddDays(-1);
        var (chat, bank) = await ChatThenBankAsync(s, chatAt, bankAt);
        var userId = await UserIdAsync(s);
        var oldDay = VietnamTime.DateOf(chatAt);
        var newDay = VietnamTime.DateOf(bankAt);
        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<DailySummaryJob>();
            await job.SummariseUserAsync(userId, oldDay);
            await job.SummariseUserAsync(userId, newDay);
        }

        (await _api.MergeAsync(s, bank, chat)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _api.Base.PeriodAsync(budget, chatAt)).SpentCents.Should().Be(0, "kỳ cũ được tính lại, không chặn ở 0 rồi lệch");
        (await _api.Base.PeriodAsync(budget, bankAt)).SpentCents.Should().Be(Price);
        var summaries = await _api.Base.WithDbAsync(db => db.DailySummaries.AsNoTracking()
            .Where(d => d.UserId == userId && (d.SummaryDate == oldDay || d.SummaryDate == newDay)).ToListAsync());
        summaries.Single(d => d.SummaryDate == oldDay).TotalSpentCents.Should().Be(0);
        summaries.Single(d => d.SummaryDate == newDay).TotalSpentCents.Should().Be(Price);
    }

    [Fact]
    public async Task SharedSurvivorMovingIntoThisMonth_CountsInTheGroupBudgetOnce()
    {
        var s = await _api.Base.StartAsync();
        var created = await _api.Base.SendAsync(HttpMethod.Post, "/api/v1/groups", s.Token, new { name = "Gia đình" });
        var group = (await created.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
        var budgetResponse = await _api.Base.SendAsync(HttpMethod.Post, $"/api/v1/groups/{group}/budgets", s.Token, new { categoryId = s.Food, limitCents = 1_000_000L });
        var groupBudget = (await budgetResponse.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
        var (chat, bank) = await ChatThenBankAsync(s, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMinutes(-1));
        (await _api.Base.SendAsync(HttpMethod.Put, $"/api/v1/transactions/{chat}/group", s.Token, new { groupId = group })).IsSuccessStatusCode.Should().BeTrue();
        (await GetAsync<List<GroupBudget>>(s, $"/api/v1/groups/{group}/budgets")).Single(b => b.Id == groupBudget).SpentCents.Should().Be(0);

        (await _api.MergeAsync(s, bank, chat)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetAsync<List<GroupBudget>>(s, $"/api/v1/groups/{group}/budgets")).Single(b => b.Id == groupBudget).SpentCents.Should().Be(Price);
    }

    [Fact]
    public async Task ForecastHabits_SeeTheSurvivorOnly()
    {
        var s = await _api.Base.StartAsync();
        var (chat, bank) = await ChatThenBankAsync(s, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(-1));
        await _api.Base.ConfirmAsync(s, bank);
        await _api.MergeAsync(s, bank, chat);
        var userId = await UserIdAsync(s);
        var today = VietnamTime.Today();

        using var scope = _factory.Services.CreateScope();
        var habits = await scope.ServiceProvider.GetRequiredService<IForecastDataRepository>().GetTransactionsAsync(userId, today.AddDays(-1), today);

        habits.Select(h => h.Id).Should().Equal(chat);
    }

    [Fact]
    public async Task TheLosersAnomaly_IsRemoved()
    {
        var s = await _api.Base.StartAsync();
        var older = await _api.Base.SpendAsync(s, 300_000, DateTimeOffset.UtcNow.AddMinutes(-5), s.Food, "Điện máy X");
        var newer = await _api.Base.SpendAsync(s, 300_000, DateTimeOffset.UtcNow.AddMinutes(-3), s.Food, "Điện máy X");
        var userId = await UserIdAsync(s);
        await _api.Base.WithDbAsync(async db =>
        {
            db.TransactionAnomalies.Add(new TransactionAnomaly
            {
                Id = Guid.NewGuid(), UserId = userId, TransactionId = newer.Id, CategoryId = s.Food, Kind = AnomalyKind.Duplicate,
                Severity = AnomalySeverity.High, AmountCents = 300_000, TypicalCents = 300_000, RatioPercent = 100, CreatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        (await _api.MergeAsync(s, newer.Id, older.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _api.Base.WithDbAsync(db => db.TransactionAnomalies.IgnoreQueryFilters().CountAsync(a => a.TransactionId == newer.Id)))
            .Should().Be(0);
    }

    [Fact]
    public async Task QrIntentReceiptLinkAndDraftRefunds_FollowTheSurvivor()
    {
        var s = await _api.Base.StartAsync();
        var older = await _api.Base.SpendAsync(s, 300_000, DateTimeOffset.UtcNow.AddMinutes(-5), s.Shopping);
        var newer = await _api.Base.SpendAsync(s, 300_000, DateTimeOffset.UtcNow.AddMinutes(-3), s.Shopping);
        var userId = await UserIdAsync(s);
        var scan = Guid.NewGuid();
        var draftRefund = Guid.NewGuid();
        await _api.Base.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.QrTransferIntents.Add(new QrTransferIntent
            {
                Id = Guid.NewGuid(), UserId = userId, ReceiverBankBin = "970436", ReceiverAccountNumber = "0123", SenderBankBin = "970436",
                AmountCents = 300_000, Status = QrTransferStatus.Matched, MatchedTransactionId = newer.Id, MatchedAt = now, ScannedAt = now, CreatedAt = now,
            });
            db.ReceiptScans.Add(new ReceiptScan { Id = scan, UserId = userId, OcrResult = "success", AmountCents = 300_000, CategorySlug = "shopping", CreatedAt = now });
            db.Transactions.Add(new Transaction
            {
                Id = draftRefund, UserId = userId, FinancialAccountId = s.Account, CategoryId = s.Shopping, AmountCents = 100_000,
                TransactionType = TransactionType.Credit, Source = TransactionSource.Notification, Status = TransactionStatus.Draft,
                Relation = TransactionRelation.Refund, RelatedTransactionId = newer.Id, TransactedAt = now, CreatedAt = now, UpdatedAt = now,
            });
            return await db.SaveChangesAsync();
        });
        await _api.Base.WithDbAsync(db => new ReceiptScanRepository(db).LinkToTransactionAsync(scan, userId, newer.Id, "shopping"));

        (await _api.MergeAsync(s, newer.Id, older.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var intent = await _api.Base.WithDbAsync(db => db.QrTransferIntents.AsNoTracking().SingleAsync(i => i.UserId == userId));
        intent.MatchedTransactionId.Should().Be(older.Id);
        intent.Status.Should().Be(QrTransferStatus.Matched);
        (await _api.Base.WithDbAsync(db => db.ReceiptScanLinks.AsNoTracking().SingleAsync(l => l.ScanId == scan))).TransactionId.Should().Be(older.Id);
        (await _api.Base.WithDbAsync(db => db.Transactions.AsNoTracking().SingleAsync(t => t.Id == draftRefund))).RelatedTransactionId.Should().Be(older.Id);
    }
}
