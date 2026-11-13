using System.Net.Http.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 81, BR-RF-03/06/08, D3, D7: mọi chỗ tổng hợp báo cáo thấy cùng một con số. Bộ dữ liệu chung: chi Mua sắm 900k, hoàn 300k, lương 10tr,
/// một khoản 500k bị đảo. Kỳ vọng ở mọi nơi: chi 600k, thu 10tr, hoàn 300k, cặp đảo không có mặt.
/// </summary>
[Collection("Integration")]
public class RefundReportsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly RefundApi _api;

    public RefundReportsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new RefundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private record Monthly(long TotalSpentCents, long TotalIncomeCents, long NetCents, int TransactionCount, long RefundedCents, long? CrossPeriodRefundCents);
    private record BreakdownItem(Guid? CategoryId, string? CategorySlug, long SpentCents, int Percent);
    private record Breakdown(long TotalSpentCents, List<BreakdownItem> Items);
    private record TimelineDay(DateOnly Date, long TotalSpentCents, long TotalIncomeCents, List<RefundApi.TransactionData> Transactions);
    private record GroupReport(long TotalSpentCents, long TotalIncomeCents, long RefundedCents, List<GroupCategory> ByCategory);
    private record GroupCategory(Guid? CategoryId, long SpentCents);

    private async Task<(RefundApi.Session S, RefundApi.TransactionData Shoes)> SeedAsync(DateTimeOffset? at = null)
    {
        var s = await _api.StartAsync();
        var when = at ?? DateTimeOffset.UtcNow;
        var shoes = await _api.SpendAsync(s, 900_000, when);
        await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000, transactedAt = when });
        await _api.EarnAsync(s, 10_000_000, when, "Lương");
        var wrong = await _api.SpendAsync(s, 500_000, when, merchant: "Trừ nhầm");
        await _api.RefundAsync(s, wrong.Id, new { relation = "Reversal", transactedAt = when });
        return (s, shoes);
    }

    private async Task<T> GetAsync<T>(RefundApi.Session s, string url)
    {
        var response = await _api.SendAsync(HttpMethod.Get, url, s.Token);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<T>>(RefundApi.JsonOptions))!.Data;
    }

    [Fact]
    public async Task MonthlySummary_NetSpend_IncomeWithoutRefunds_RefundedSeparately()
    {
        var (s, _) = await SeedAsync();

        var summary = await GetAsync<Monthly>(s, "/api/v1/reports/monthly-summary");

        summary.TotalSpentCents.Should().Be(600_000);
        summary.TotalIncomeCents.Should().Be(10_000_000);
        summary.RefundedCents.Should().Be(300_000);
        summary.CrossPeriodRefundCents.Should().BeNull();
        summary.TransactionCount.Should().Be(3, "giày, khoản hoàn và lương; cặp đảo không được đếm");
    }

    [Fact]
    public async Task CategoryBreakdown_UsesTheNet()
    {
        var (s, _) = await SeedAsync();

        var breakdown = await GetAsync<Breakdown>(s, "/api/v1/reports/category-breakdown");

        breakdown.Items.Should().ContainSingle().Which.SpentCents.Should().Be(600_000);
        breakdown.Items[0].CategoryId.Should().Be(s.Shopping);
        breakdown.TotalSpentCents.Should().Be(600_000);
    }

    [Fact]
    public async Task Timeline_DayTotals_FollowTheSameRule_ButHistoryKeepsEveryRow()
    {
        var (s, _) = await SeedAsync();

        var days = await GetAsync<List<TimelineDay>>(s, "/api/v1/reports/timeline");

        var today = days.Single();
        today.TotalSpentCents.Should().Be(600_000);
        today.TotalIncomeCents.Should().Be(10_000_000);
        today.Transactions.Should().HaveCount(5, "cặp đảo vẫn hiện trong lịch sử (BR-RF-08)");
    }

    [Fact]
    public async Task CrossMonthRefund_IsExplainedAndNoCategoryGoesNegative()
    {
        var s = await _api.StartAsync();
        var shoes = await _api.SpendAsync(s, 900_000, DateTimeOffset.UtcNow.AddMonths(-1));
        await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000 });
        await _api.SpendAsync(s, 100_000, category: s.Food, merchant: "Phở");

        var summary = await GetAsync<Monthly>(s, "/api/v1/reports/monthly-summary");
        var breakdown = await GetAsync<Breakdown>(s, "/api/v1/reports/category-breakdown");

        summary.TotalSpentCents.Should().Be(0, "100k − 300k chặn ở 0 (D3)");
        summary.RefundedCents.Should().Be(300_000);
        summary.CrossPeriodRefundCents.Should().Be(300_000);
        breakdown.Items.Should().ContainSingle(i => i.CategoryId == s.Food).Which.SpentCents.Should().Be(100_000);
        breakdown.Items.Should().NotContain(i => i.CategoryId == s.Shopping);
    }

    [Fact]
    public async Task DailySummary_IsRewrittenWhenAnOlderDayIsLinked()
    {
        // D7: ngày đã chốt có một khoản thu; liên kết nó thành hoàn tiền thì dòng tóm tắt của ngày đó phải được chốt lại.
        var s = await _api.StartAsync();
        var day = DateTimeOffset.UtcNow.AddDays(-40);
        var date = VietnamTime.DateOf(day);
        var shoes = await _api.SpendAsync(s, 900_000, day);
        var income = await _api.EarnAsync(s, 300_000, day);
        var userId = await _api.WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DailySummaryJob>().SummariseUserAsync(userId, date);
        }

        var before = await _api.WithDbAsync(db => db.DailySummaries.AsNoTracking().SingleAsync(d => d.UserId == userId && d.SummaryDate == date));
        before.TotalIncomeCents.Should().Be(300_000);

        await _api.LinkAsync(s, income.Id, shoes.Id);

        var after = await _api.WithDbAsync(db => db.DailySummaries.AsNoTracking().SingleAsync(d => d.UserId == userId && d.SummaryDate == date));
        after.TotalSpentCents.Should().Be(600_000);
        after.TotalIncomeCents.Should().Be(0);
        after.TotalRefundedCents.Should().Be(300_000);
    }

    [Fact]
    public async Task SpendableSnapshot_AndMarketProfile_SeeTheNet()
    {
        var (s, shoes) = await SeedAsync();
        var fully = await _api.SpendAsync(s, 200_000, merchant: "Đổi trả");
        await _api.RefundAsync(s, fully.Id);
        var userId = await _api.WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync());
        var (start, end) = VietnamTime.MonthRange(DateTimeOffset.UtcNow);
        var today = VietnamTime.Today();

        using var scope = _factory.Services.CreateScope();
        var snapshot = await scope.ServiceProvider.GetRequiredService<IFinanceSnapshotRepository>().GetDebitTotalAsync(userId, start, end);
        var debits = await scope.ServiceProvider.GetRequiredService<IReportRepository>().GetConfirmedDebitsAsync(userId, today.AddDays(-1), today);

        snapshot.Should().Be(600_000, "chi ròng: giày 900k − 300k, khoản 200k hoàn đủ, cặp đảo 0");
        debits.Select(d => d.Id).Should().Equal(shoes.Id);
    }

    [Fact]
    public async Task GroupReport_NetsSharedRefunds()
    {
        var s = await _api.StartAsync();
        var created = await _api.SendAsync(HttpMethod.Post, "/api/v1/groups", s.Token, new { name = "Gia đình" });
        var group = (await created.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
        var shoes = await _api.SpendAsync(s, 900_000);
        await _api.SendAsync(HttpMethod.Put, $"/api/v1/transactions/{shoes.Id}/group", s.Token, new { groupId = group });
        await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000 });

        var report = await GetAsync<GroupReport>(s, $"/api/v1/groups/{group}/reports/monthly");

        report.TotalSpentCents.Should().Be(600_000);
        report.TotalIncomeCents.Should().Be(0);
        report.RefundedCents.Should().Be(300_000);
        report.ByCategory.Should().ContainSingle().Which.SpentCents.Should().Be(600_000);
    }
}
