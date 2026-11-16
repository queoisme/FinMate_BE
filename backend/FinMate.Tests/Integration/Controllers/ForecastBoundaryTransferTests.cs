using System.Net;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 83, BR-TRF-11, S3, S4: chuyển khoản vượt ranh giới tập ví được tính là tiền ra/vào của tập đó, nên vào số thực tế của độ chính xác dự báo
/// và bộ khoản định kỳ, nhưng không bao giờ vào thói quen chi. Chuyển khoản trong tập được tính thì không vào đâu. Chuyển đổi một lần trả của khoản
/// định kỳ thành Transfer trong tập được tính thì hỏi "còn dùng không?"; vượt ranh giới thì giữ khớp.
/// </summary>
[Collection("Integration")]
public class ForecastBoundaryTransferTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly TransferApi _api;
    private readonly ITestOutputHelper _output;

    public ForecastBoundaryTransferTests(AuthApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _api = new TransferApi(factory);
        _output = output;
    }

    /// <summary>Ví tiết kiệm bị loại khỏi dự báo (FR-FCST-07).</summary>
    private async Task<Guid> ExcludedSavingsAsync(TransferApi.Wallets w)
    {
        var savings = await _api.AccountAsync(w.S, "Tiết kiệm", AccountType.Cash, null, 0);
        var response = await _api.Base.SendAsync(HttpMethod.Patch, $"/api/v1/financial-accounts/{savings}/forecast-inclusion", w.S.Token, new { included = false });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return savings;
    }

    private Task ExcludeAsync(TransferApi.Wallets w, Guid account)
        => _api.Base.SendAsync(HttpMethod.Patch, $"/api/v1/financial-accounts/{account}/forecast-inclusion", w.S.Token, new { included = false });

    [Fact]
    public async Task ForecastActuals_SeeMoneyLeavingTheCountedWallets_ButNotMovesInsideThem()
    {
        var w = await _api.StartAsync();
        var savings = await ExcludedSavingsAsync(w);
        await ExcludeAsync(w, w.Momo);
        for (var daysAgo = 1; daysAgo <= 10; daysAgo++)
        {
            await _api.Base.SpendAsync(w.S, 50_000, DateTimeOffset.UtcNow.AddDays(-daysAgo), w.S.Food, "Quán cơm");
        }

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IForecastAccuracyService>();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var today = VietnamTime.Today();
        await service.CaptureAsync(today);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE forecast_snapshots SET origin_date = origin_date - 20, horizon_date = horizon_date - 20 WHERE user_id = {0}", w.UserId);
        var start = (await db.ForecastSnapshots.AsNoTracking().Where(s => s.UserId == w.UserId).OrderBy(s => s.HorizonDays).FirstAsync()).StartBalanceCents;

        // Trong khoảng của mốc 7 ngày (−20..−13): 300k ra ví tiết kiệm (bị loại), 1tr qua lại giữa hai ví được tính.
        await _api.ManualTransferAsync(w, w.Cash, savings, 300_000, DateTimeOffset.UtcNow.AddDays(-18));
        await _api.ManualTransferAsync(w, w.Cash, w.Vcb, 1_000_000, DateTimeOffset.UtcNow.AddDays(-17));
        db.ChangeTracker.Clear();

        await service.FillActualsAsync(today);

        var actual = (await db.ForecastSnapshots.AsNoTracking().Where(s => s.UserId == w.UserId).OrderBy(s => s.HorizonDays).FirstAsync()).ActualCents;
        actual.Should().Be(start - 300_000, "tiền ra ví bị loại làm số dư tập được tính giảm thật; chuyển nội bộ không đổi gì");
        _output.WriteLine($"S4 số thực tế mốc 7 ngày: trước sửa {start} (bỏ sót chuyển khoản), sau sửa {actual}; lệch được sửa = 300000");
    }

    [Fact]
    public async Task AMonthlyTransferToAnExcludedWallet_IsDetectedAsARecurringOutflow_ButHabitsNeverSeeIt()
    {
        var w = await _api.StartAsync(vcbBalance: 20_000_000);
        var savings = await ExcludedSavingsAsync(w);
        foreach (var daysAgo in new[] { 80, 50, 20 })
        {
            await _api.ManualTransferAsync(w, w.Vcb, savings, 2_000_000, DateTimeOffset.UtcNow.AddDays(-daysAgo));
            await _api.ManualTransferAsync(w, w.Vcb, w.Momo, 700_000, DateTimeOffset.UtcNow.AddDays(-daysAgo));   // trong tập được tính
        }

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRecurringDetectionService>().RunForUserAsync(w.UserId);
        var items = await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().RecurringItems.AsNoTracking()
            .Where(i => i.UserId == w.UserId).ToListAsync();
        var today = VietnamTime.Today();
        var habits = await scope.ServiceProvider.GetRequiredService<IForecastDataRepository>().GetTransactionsAsync(w.UserId, today.AddDays(-90), today);

        items.Should().ContainSingle().Which.Should().Match<RecurringItem>(i =>
            i.Direction == RecurringDirection.Outflow && i.AmountCents == 2_000_000 && i.CounterpartyKey == "tiet kiem");
        habits.Should().BeEmpty("chuyển khoản không bao giờ là thói quen chi");
    }

    private async Task<(TransferApi.Wallets W, Guid Debit, Guid ItemId)> PaidBillAsync()
    {
        var w = await _api.StartAsync();
        var at = DateTimeOffset.UtcNow.AddDays(-1);
        var debit = await _api.DebitAsync(w, 500_000, status: TransactionStatus.Confirmed, at: at, description: "MOMO");
        await _api.Base.WithDbAsync(async db =>
        {
            var t = await db.Transactions.SingleAsync(x => x.Id == debit);
            t.MerchantName = "MOMO";
            return await db.SaveChangesAsync();
        });
        var itemId = Guid.NewGuid();
        await _api.Base.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.RecurringItems.Add(new RecurringItem
            {
                Id = itemId, UserId = w.UserId, Direction = RecurringDirection.Outflow, Name = "Nạp MoMo hằng tháng", CounterpartyKey = "momo",
                AmountCents = 500_000, Cadence = RecurringCadence.Monthly, NextDueDate = VietnamTime.DateOf(at).AddMonths(1), Status = RecurringStatus.Active,
                Source = RecurringSource.Detected, Occurrences = 3, LastSeenAt = at, LastSeenAmountCents = 500_000, CreatedAt = now, UpdatedAt = now,
            });
            return await db.SaveChangesAsync();
        });
        return (w, debit, itemId);
    }

    private Task<List<ForecastAlert>> AlertsAsync(Guid userId)
        => _api.Base.WithDbAsync(db => db.ForecastAlerts.AsNoTracking().Where(a => a.UserId == userId && a.Kind == ForecastAlertKind.BillStatusCheck).ToListAsync());

    [Fact]
    public async Task S3_ConvertingAPaidBillIntoATransferInsideTheCountedWallets_AsksStillUsing_WithoutPausing()
    {
        var (w, debit, itemId) = await PaidBillAsync();

        (await _api.ConvertAsync(w.S, debit, w.Momo)).StatusCode.Should().Be(HttpStatusCode.OK);

        var alert = (await AlertsAsync(w.UserId)).Should().ContainSingle().Subject;
        alert.DedupKey.Should().Be($"bill_status_check:{itemId}:transfer:{debit}");
        var item = await _api.Base.WithDbAsync(db => db.RecurringItems.AsNoTracking().SingleAsync(i => i.Id == itemId));
        item.Status.Should().Be(RecurringStatus.Active, "không tự tạm dừng, không huỷ");
    }

    [Fact]
    public async Task S3_AcrossTheBoundary_TheMatchIsKept_AndNothingIsAsked()
    {
        var (w, debit, itemId) = await PaidBillAsync();
        await ExcludeAsync(w, w.Momo);

        await _api.ConvertAsync(w.S, debit, w.Momo);

        (await AlertsAsync(w.UserId)).Should().BeEmpty();
        var item = await _api.Base.WithDbAsync(db => db.RecurringItems.AsNoTracking().SingleAsync(i => i.Id == itemId));
        item.Status.Should().Be(RecurringStatus.Active);
        item.LastSeenAt.Should().NotBeNull();
    }
}
