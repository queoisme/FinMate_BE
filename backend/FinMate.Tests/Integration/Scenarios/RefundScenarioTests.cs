using System.Net;
using System.Net.Http.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Scenarios;

/// <summary>
/// Kịch bản đầu-cuối bắt buộc của Phase 81: ngân sách Mua sắm 1.000.000đ, mua giày 900.000đ (cảnh báo T2), rồi tiền về từ thông báo.
/// Lặp lại với hoàn đủ, hoàn một phần 300.000đ và đảo giao dịch.
/// </summary>
[Collection("Integration")]
public class RefundScenarioTests : IClassFixture<RefundApiFactory>
{
    private readonly RefundApiFactory _factory;
    private readonly RefundApi _api;

    public RefundScenarioTests(RefundApiFactory factory)
    {
        _factory = factory;
        _api = new RefundApi(factory);
    }

    private record Analysis(Guid? DraftTransactionId, string? RelationHint, List<RefundApi.SuggestionData>? SuggestedRelatedTransactions);
    private record Monthly(long TotalSpentCents, long TotalIncomeCents, long RefundedCents, int TransactionCount);

    private sealed record Setup(RefundApi.Session S, Guid UserId, Guid Budget, RefundApi.TransactionData Shoes, long StartBalance);

    private async Task<Setup> BuyShoesAsync()
    {
        var s = await RefundNotificationTests.WalletSessionAsync(_api, _factory);
        var userId = await _api.WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync());
        var start = await _api.BalanceAsync(s.Account);
        var budget = await _api.BudgetAsync(s, 1_000_000);

        var shoes = await _api.SpendAsync(s, 900_000, merchant: "Shopee");

        var period = await _api.PeriodAsync(budget);
        period.SpentCents.Should().Be(900_000);
        period.AlertT2SentAt.Should().NotBeNull("mua giày 900k/1tr chạm ngưỡng T2");
        PushesFor(userId).Should().ContainSingle("đúng một cảnh báo T2");
        return new Setup(s, userId, budget, shoes, start);
    }

    private List<(Guid UserId, string Body, string? Action)> PushesFor(Guid userId)
        => _factory.Push.Sent.Where(p => p.UserId == userId).ToList();

    private async Task<Analysis> NotifyAsync(Setup x, long amount, string hint)
    {
        var response = await RefundNotificationTests.NotifyAsync(_api, x.S, amount, hint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<Analysis>>(RefundApi.JsonOptions))!.Data;
    }

    private async Task<Monthly> MonthlyAsync(Setup x)
    {
        var response = await _api.SendAsync(HttpMethod.Get, "/api/v1/reports/monthly-summary", x.S.Token);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<Monthly>>(RefundApi.JsonOptions))!.Data;
    }

    private async Task<IReadOnlyList<FinMate.Application.Forecasting.ForecastTransaction>> HabitsAsync(Setup x)
    {
        using var scope = _factory.Services.CreateScope();
        var today = VietnamTime.Today();
        return await scope.ServiceProvider.GetRequiredService<IForecastDataRepository>().GetTransactionsAsync(x.UserId, today.AddDays(-1), today);
    }

    private Task<Guid> SeedAnomalyAsync(Setup x)
        => _api.WithDbAsync(async db =>
        {
            var anomaly = new TransactionAnomaly
            {
                Id = Guid.NewGuid(), UserId = x.UserId, TransactionId = x.Shoes.Id, Kind = AnomalyKind.CategorySpike, Severity = AnomalySeverity.High,
                AmountCents = 900_000, TypicalCents = 150_000, RatioPercent = 600, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.TransactionAnomalies.Add(anomaly);
            await db.SaveChangesAsync();
            return anomaly.Id;
        });

    [Fact]
    public async Task FullRefundFromANotification()
    {
        var x = await BuyShoesAsync();
        var anomaly = await SeedAnomalyAsync(x);
        var incomeBefore = (await MonthlyAsync(x)).TotalIncomeCents;

        var analysis = await NotifyAsync(x, 900_000, "refund");

        analysis.RelationHint.Should().Be("Refund");
        analysis.SuggestedRelatedTransactions!.First().TransactionId.Should().Be(x.Shoes.Id, "gợi ý đầu tiên là đúng đôi giày");
        PushesFor(x.UserId).Should().ContainSingle(p => p.Action == "choose_refund_link");
        var pushesBeforeLink = PushesFor(x.UserId).Count;

        var confirmed = await _api.ConfirmAsync(x.S, analysis.DraftTransactionId!.Value, new { relatedTransactionId = x.Shoes.Id });
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);

        (await _api.BalanceAsync(x.S.Account)).Should().Be(x.StartBalance, "số dư đúng: trừ 900k rồi nhận lại 900k");
        (await _api.PeriodAsync(x.Budget)).SpentCents.Should().Be(0);
        var monthly = await MonthlyAsync(x);
        monthly.TotalIncomeCents.Should().Be(incomeBefore, "tổng thu tháng không đổi");
        monthly.RefundedCents.Should().Be(900_000);
        monthly.TotalSpentCents.Should().Be(0);
        PushesFor(x.UserId).Should().HaveCount(pushesBeforeLink, "không có push cảnh báo mới");
        (await HabitsAsync(x)).Should().NotContain(t => t.Id == x.Shoes.Id, "khoản giày bị loại khỏi baseline dự báo");
        (await _api.WithDbAsync(db => db.TransactionAnomalies.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == anomaly)))
            .Resolution.Should().Be(AnomalyResolution.Refunded, "và khỏi phát hiện bất thường");
    }

    [Fact]
    public async Task PartialRefundOf300k()
    {
        var x = await BuyShoesAsync();
        var anomaly = await SeedAnomalyAsync(x);
        var incomeBefore = (await MonthlyAsync(x)).TotalIncomeCents;

        var analysis = await NotifyAsync(x, 300_000, "refund");
        analysis.SuggestedRelatedTransactions!.First().TransactionId.Should().Be(x.Shoes.Id);
        var pushesBeforeLink = PushesFor(x.UserId).Count;
        await _api.ConfirmAsync(x.S, analysis.DraftTransactionId!.Value, new { relatedTransactionId = x.Shoes.Id });

        (await _api.BalanceAsync(x.S.Account)).Should().Be(x.StartBalance - 600_000);
        var period = await _api.PeriodAsync(x.Budget);
        period.SpentCents.Should().Be(600_000);
        period.AlertT2SentAt.Should().NotBeNull("cờ T2 giữ nguyên (BR-RF-05)");
        var monthly = await MonthlyAsync(x);
        monthly.TotalIncomeCents.Should().Be(incomeBefore);
        monthly.RefundedCents.Should().Be(300_000);
        monthly.TotalSpentCents.Should().Be(600_000);
        PushesFor(x.UserId).Should().HaveCount(pushesBeforeLink);
        (await HabitsAsync(x)).Single(t => t.Id == x.Shoes.Id).AmountCents.Should().Be(600_000, "baseline thấy số ròng");
        (await _api.WithDbAsync(db => db.TransactionAnomalies.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == anomaly)))
            .Resolution.Should().BeNull("hoàn một phần không đóng đánh dấu bất thường");
    }

    [Fact]
    public async Task ReversalFromANotification()
    {
        var x = await BuyShoesAsync();
        var anomaly = await SeedAnomalyAsync(x);
        var before = await MonthlyAsync(x);

        var analysis = await NotifyAsync(x, 900_000, "reversal");
        analysis.RelationHint.Should().Be("Reversal");
        analysis.SuggestedRelatedTransactions!.First().TransactionId.Should().Be(x.Shoes.Id);
        var pushesBeforeLink = PushesFor(x.UserId).Count;
        await _api.ConfirmAsync(x.S, analysis.DraftTransactionId!.Value, new { relatedTransactionId = x.Shoes.Id, relation = "Reversal" });

        (await _api.BalanceAsync(x.S.Account)).Should().Be(x.StartBalance);
        (await _api.PeriodAsync(x.Budget)).SpentCents.Should().Be(0);
        (await _api.GetAsync(x.S, x.Shoes.Id)).IsReversed.Should().BeTrue();

        var monthly = await MonthlyAsync(x);
        monthly.TotalSpentCents.Should().Be(0, "cặp đảo bị loại khỏi mọi tổng hợp");
        monthly.TotalIncomeCents.Should().Be(before.TotalIncomeCents);
        monthly.RefundedCents.Should().Be(0, "đảo không phải hoàn tiền");
        monthly.TransactionCount.Should().Be(before.TransactionCount - 1, "giày không còn được đếm, lệnh đảo cũng không");

        var list = await _api.SendAsync(HttpMethod.Get, "/api/v1/transactions", x.S.Token);
        var items = (await list.Content.ReadFromJsonAsync<RefundApi.Envelope<List<RefundApi.TransactionData>>>(RefundApi.JsonOptions))!.Data;
        items.Should().Contain(i => i.Id == x.Shoes.Id).And.Contain(i => i.Relation == "Reversal", "cả cặp vẫn hiện trong lịch sử");

        PushesFor(x.UserId).Should().HaveCount(pushesBeforeLink);
        (await HabitsAsync(x)).Should().NotContain(t => t.Id == x.Shoes.Id);
        (await _api.WithDbAsync(db => db.TransactionAnomalies.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == anomaly)))
            .Resolution.Should().Be(AnomalyResolution.Reversed);
    }
}
