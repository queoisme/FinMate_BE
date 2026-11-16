using System.Net;
using System.Net.Http.Json;
using FinMate.Application.Agent;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 83, BR-TRF-10/11: sau khi một khoản chi 500k ở VCB và một khoản thu 500k ở MoMo (đã lỡ xác nhận như chi/thu thường) được chuyển thành
/// Transfer, mọi chỗ tổng hợp coi như chưa từng có chi hay thu: ngân sách, báo cáo tháng, cơ cấu danh mục, timeline, tóm tắt ngày đã chốt, nguồn
/// của hũ, số đã chi cho /me/spendable, dòng tiền (có ví bị loại), thu nhập dự báo, bất thường, dữ liệu của Agent.
/// </summary>
[Collection("Integration")]
public class TransferAggregatesTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly TransferApi _api;

    public TransferAggregatesTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new TransferApi(factory);
    }

    private record Monthly(long TotalSpentCents, long TotalIncomeCents);
    private record BreakdownItem(Guid? CategoryId, long SpentCents);
    private record Breakdown(long TotalSpentCents, List<BreakdownItem> Items);
    private record TimelineDay(DateOnly Date, long TotalSpentCents, long TotalIncomeCents, List<RefundApi.TransactionData> Transactions);
    private record Cashflow(long StartBalanceCents);

    private async Task<T> GetAsync<T>(RefundApi.Session s, string url)
    {
        var response = await _api.Base.SendAsync(HttpMethod.Get, url, s.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<T>>(RefundApi.JsonOptions))!.Data;
    }

    private async Task<Guid> PostAsync(TransferApi.Wallets w, Guid account, Guid category, long amount, TransactionType type, DateTimeOffset at, string merchant)
    {
        var response = await _api.Base.SendAsync(HttpMethod.Post, "/api/v1/transactions", w.S.Token,
            new FinMate.API.Controllers.CreateManualTransactionRequest(account, category, amount, type, at, merchant, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
    }

    /// <summary>Người dùng đã lỡ xác nhận cả hai chiều của một lần nạp ví như chi và thu thường.</summary>
    private async Task<(TransferApi.Wallets W, Guid Debit, Guid Credit, Guid Budget)> ConfirmedBothSidesAsync(DateTimeOffset at)
    {
        var w = await _api.StartAsync();
        var budget = await _api.Base.BudgetAsync(w.S, 5_000_000, w.S.Food);
        var debit = await PostAsync(w, w.Vcb, w.S.Food, 500_000, TransactionType.Debit, at, "nap MoMo");
        var credit = await PostAsync(w, w.Momo, w.S.Income, 500_000, TransactionType.Credit, at.AddMinutes(1), "Nap tien tu Vietcombank");
        return (w, debit, credit, budget);
    }

    [Fact]
    public async Task EverythingThisMonth_SeesNoSpendingAndNoIncome()
    {
        var now = DateTimeOffset.UtcNow;
        var (w, debit, credit, budget) = await ConfirmedBothSidesAsync(now.AddHours(-1));
        (await GetAsync<Monthly>(w.S, "/api/v1/reports/monthly-summary")).TotalSpentCents.Should().Be(500_000);
        await _api.Base.WithDbAsync(async db =>
        {
            db.TransactionAnomalies.Add(new TransactionAnomaly
            {
                Id = Guid.NewGuid(), UserId = w.UserId, TransactionId = debit, Kind = AnomalyKind.CategorySpike, Severity = AnomalySeverity.Normal,
                AmountCents = 500_000, TypicalCents = 100_000, RatioPercent = 500, CreatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        (await _api.ConvertAsync(w.S, debit, w.Momo, credit)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _api.Base.PeriodAsync(budget)).SpentCents.Should().Be(0);
        var monthly = await GetAsync<Monthly>(w.S, "/api/v1/reports/monthly-summary");
        monthly.TotalSpentCents.Should().Be(0);
        monthly.TotalIncomeCents.Should().Be(0, "tiền nạp ví không phải thu nhập");
        (await GetAsync<Breakdown>(w.S, "/api/v1/reports/category-breakdown")).Items.Should().BeEmpty();
        var days = await GetAsync<List<TimelineDay>>(w.S, "/api/v1/reports/timeline");
        days.Should().OnlyContain(d => d.TotalSpentCents == 0 && d.TotalIncomeCents == 0);
        days.SelectMany(d => d.Transactions).Should().ContainSingle().Which.Id.Should().Be(debit, "Transfer vẫn hiện trong lịch sử");

        using var scope = _factory.Services.CreateScope();
        var (start, end) = VietnamTime.MonthRange(DateTimeOffset.UtcNow);
        var today = VietnamTime.Today();
        var monthFirst = new DateOnly(today.Year, today.Month, 1);
        (await scope.ServiceProvider.GetRequiredService<IReportRepository>().GetCategorySpendAsync(w.UserId, monthFirst, today))
            .Should().BeEmpty("hũ đọc cùng nguồn này");
        (await scope.ServiceProvider.GetRequiredService<IFinanceSnapshotRepository>().GetDebitTotalAsync(w.UserId, start, end))
            .Should().Be(0, "/me/spendable");
        (await scope.ServiceProvider.GetRequiredService<IForecastDataRepository>().GetTransactionsAsync(w.UserId, today.AddDays(-1), today))
            .Should().BeEmpty("thói quen chi và thu nhập dự báo không thấy chuyển khoản");
        // Agent (công cụ chi tiêu/ngân sách) đọc qua nguồn này; chuỗi theo tháng chỉ gồm tháng đã trọn nên xét chi theo danh mục của tháng này.
        (await scope.ServiceProvider.GetRequiredService<RepositoryFinancialDataSource>().GetCategorySpendAsync(w.UserId, monthFirst, today))
            .Should().BeEmpty();

        var anomaly = await _api.Base.WithDbAsync(db => db.TransactionAnomalies.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.TransactionId == debit));
        anomaly.Resolution.Should().Be(AnomalyResolution.Transfer);
    }

    [Fact]
    public async Task CashflowStartsFromTheCountedWallets_WhenTheDestinationIsExcluded()
    {
        var (w, debit, credit, _) = await ConfirmedBothSidesAsync(DateTimeOffset.UtcNow.AddHours(-1));
        await _api.Base.SendAsync(HttpMethod.Patch, $"/api/v1/financial-accounts/{w.Momo}/forecast-inclusion", w.S.Token, new { included = false });

        await _api.ConvertAsync(w.S, debit, w.Momo, credit);

        var cashflow = await GetAsync<Cashflow>(w.S, "/api/v1/forecast/cashflow");
        var counted = await _api.Base.BalanceAsync(w.Cash) + await _api.Base.BalanceAsync(w.Vcb);
        cashflow.StartBalanceCents.Should().Be(counted, "tiền đã sang MoMo (bị loại) không còn trong tập được tính");
        (await _api.Base.BalanceAsync(w.Vcb)).Should().Be(5_000_000 - 500_000);
    }

    [Fact]
    public async Task AnAlreadySummarisedDay_IsResettled()
    {
        var at = DateTimeOffset.UtcNow.AddDays(-2);
        var (w, debit, credit, _) = await ConfirmedBothSidesAsync(at);
        var date = VietnamTime.DateOf(at);
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DailySummaryJob>().SummariseUserAsync(w.UserId, date);
        }

        var before = await _api.Base.WithDbAsync(db => db.DailySummaries.AsNoTracking().SingleAsync(d => d.UserId == w.UserId && d.SummaryDate == date));
        before.TotalSpentCents.Should().Be(500_000);

        await _api.ConvertAsync(w.S, debit, w.Momo, credit);

        var after = await _api.Base.WithDbAsync(db => db.DailySummaries.AsNoTracking().SingleAsync(d => d.UserId == w.UserId && d.SummaryDate == date));
        after.TotalSpentCents.Should().Be(0);
        after.TotalIncomeCents.Should().Be(0);
    }
}
