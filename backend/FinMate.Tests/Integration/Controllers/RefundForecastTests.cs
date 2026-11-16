using System.Net;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 81, Task 9: dữ liệu dự báo (thói quen và thô), R1 (khoản định kỳ trở về chưa trả khi bị đảo), R2 (bất thường tự đóng).</summary>
[Collection("Integration")]
public class RefundForecastTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly RefundApi _api;

    public RefundForecastTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new RefundApi(factory);
    }

    private Task<Guid> UserIdAsync(RefundApi.Session s)
        => _api.WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync());

    [Fact]
    public async Task ForecastData_HabitsAreNetted_RawKeepsEveryRowForBalances()
    {
        var s = await _api.StartAsync();
        var shoes = await _api.SpendAsync(s, 900_000);
        await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000 });
        var returned = await _api.SpendAsync(s, 400_000, merchant: "Đổi trả");
        await _api.RefundAsync(s, returned.Id);
        var userId = await UserIdAsync(s);
        var today = VietnamTime.Today();

        using var scope = _factory.Services.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IForecastDataRepository>();
        var habits = await data.GetTransactionsAsync(userId, today.AddDays(-1), today);
        var raw = await data.GetRawTransactionsAsync(userId, today.AddDays(-1), today);

        habits.Should().ContainSingle().Which.AmountCents.Should().Be(600_000);
        raw.Should().HaveCount(4);
        raw.Sum(t => t.Type == TransactionType.Credit ? t.AmountCents : -t.AmountCents).Should().Be(-600_000, "số dư vẫn tính mọi khoản tiền vào");
    }

    private async Task<Guid> SeedAnomalyAsync(Guid userId, Guid transactionId)
        => await _api.WithDbAsync(async db =>
        {
            var anomaly = new TransactionAnomaly
            {
                Id = Guid.NewGuid(), UserId = userId, TransactionId = transactionId, Kind = AnomalyKind.CategorySpike, Severity = AnomalySeverity.High,
                AmountCents = 900_000, TypicalCents = 100_000, RatioPercent = 900, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.TransactionAnomalies.Add(anomaly);
            await db.SaveChangesAsync();
            return anomaly.Id;
        });

    private Task<TransactionAnomaly> AnomalyAsync(Guid id)
        => _api.WithDbAsync(db => db.TransactionAnomalies.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == id));

    [Fact]
    public async Task R2_FullRefundClosesTheOpenAnomaly_PartialDoesNot()
    {
        var s = await _api.StartAsync();
        var shoes = await _api.SpendAsync(s, 900_000);
        var anomaly = await SeedAnomalyAsync(await UserIdAsync(s), shoes.Id);

        await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000 });
        (await AnomalyAsync(anomaly)).Resolution.Should().BeNull();

        await _api.RefundAsync(s, shoes.Id);
        var closed = await AnomalyAsync(anomaly);
        closed.Resolution.Should().Be(AnomalyResolution.Refunded);
        closed.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task R2_ReversalClosesTheOpenAnomaly_AndClientsCannotSendTheSystemReasons()
    {
        var s = await _api.StartAsync();
        var wrong = await _api.SpendAsync(s, 900_000);
        var anomaly = await SeedAnomalyAsync(await UserIdAsync(s), wrong.Id);
        var other = await _api.SpendAsync(s, 800_000, merchant: "Khác");
        var otherAnomaly = await SeedAnomalyAsync(await UserIdAsync(s), other.Id);

        await _api.RefundAsync(s, wrong.Id, new { relation = "Reversal" });
        (await AnomalyAsync(anomaly)).Resolution.Should().Be(AnomalyResolution.Reversed);

        var response = await _api.SendAsync(HttpMethod.Post, $"/api/v1/forecast/anomalies/{otherAnomaly}/feedback", s.Token, new { resolution = "refunded" });
        ((int)response.StatusCode).Should().BeInRange(400, 422);
        (await AnomalyAsync(otherAnomaly)).Resolution.Should().BeNull();
    }

    private async Task<(RefundApi.Session S, RefundApi.TransactionData Previous, RefundApi.TransactionData Payment, Guid Item, DateOnly Next)> BillAsync()
    {
        var s = await _api.StartAsync();
        var userId = await UserIdAsync(s);
        var previous = await _api.SpendAsync(s, 200_000, DateTimeOffset.UtcNow.AddMonths(-1), merchant: "VNPT Internet");
        var payment = await _api.SpendAsync(s, 200_000, merchant: "VNPT Internet");
        var next = VietnamTime.DateOf(payment.TransactedAt).AddMonths(1);
        var itemId = await _api.WithDbAsync(async db =>
        {
            var paidAt = await db.Transactions.Where(t => t.Id == payment.Id).Select(t => t.TransactedAt).SingleAsync();
            var item = new RecurringItem
            {
                Id = Guid.NewGuid(), UserId = userId, Direction = RecurringDirection.Outflow, Name = "Internet",
                CounterpartyKey = FinMate.Application.Forecasting.CounterpartyKey.From("VNPT Internet"),
                AmountCents = 200_000, Cadence = RecurringCadence.Monthly, NextDueDate = next, Status = RecurringStatus.Active,
                Source = RecurringSource.Manual, Occurrences = 2, LastSeenAt = paidAt, LastSeenAmountCents = 200_000,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.RecurringItems.Add(item);
            await db.SaveChangesAsync();
            return item.Id;
        });
        return (s, previous, payment, itemId, next);
    }

    private Task<RecurringItem> ItemAsync(Guid id) => _api.WithDbAsync(db => db.RecurringItems.AsNoTracking().SingleAsync(r => r.Id == id));

    [Fact]
    public async Task R1_ReversingTheBillPayment_PutsTheRecurringItemBackToUnpaid()
    {
        var (s, previous, payment, itemId, next) = await BillAsync();

        (await _api.RefundAsync(s, payment.Id, new { relation = "Reversal" })).StatusCode.Should().Be(HttpStatusCode.Created);

        var item = await ItemAsync(itemId);
        item.NextDueDate.Should().Be(next.AddMonths(-1));
        item.Occurrences.Should().Be(1);
        item.LastSeenAt.Should().BeCloseTo(previous.TransactedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task R1_RefundingTheBill_EvenFully_KeepsItPaid()
    {
        var (s, _, payment, itemId, next) = await BillAsync();

        await _api.RefundAsync(s, payment.Id);

        (await ItemAsync(itemId)).NextDueDate.Should().Be(next, "hoàn tiền không đổi trạng thái đã trả của hoá đơn");
    }
}
