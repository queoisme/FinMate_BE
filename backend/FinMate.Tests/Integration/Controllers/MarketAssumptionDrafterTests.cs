using FinMate.Application.Agent;
using FinMate.Application.Economics;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Giả định nháp soạn từ giá vàng: chỉ khi đủ một năm lịch sử, không bao giờ tự duyệt, không soạn trùng.</summary>
[Collection("Integration")]
public class MarketAssumptionDrafterTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Key = "gold_price_growth";

    public async Task InitializeAsync() => await InDbAsync(async db =>
    {
        await db.GoldPrices.ExecuteDeleteAsync();
        await db.EconomicAssumptions.Where(a => a.Key == Key).ExecuteDeleteAsync();
        return 0;
    });

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> InDbAsync<T>(Func<FinMateDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private Task PriceAsync(long sell, int daysAgo) => InDbAsync(db =>
    {
        db.GoldPrices.Add(new GoldPrice
        {
            Id = Guid.NewGuid(), GoldKey = "sjc_bar", BuyCents = sell - 5_000_000, SellCents = sell, FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
            Origin = FuelPriceOrigin.Sources, SourceDomains = ["24h.com.vn", "cafef.vn"], AlertsProcessedAt = DateTimeOffset.UtcNow,
        });
        return db.SaveChangesAsync();
    });

    private async Task<int> DraftAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MarketAssumptionDrafter>().RunAsync();
    }

    private Task<List<EconomicAssumption>> AssumptionsAsync() => InDbAsync(db => db.EconomicAssumptions.AsNoTracking().Where(a => a.Key == Key).ToListAsync());

    [Fact]
    public void TheRateIsTheChangeOverTheWindowPerYear()
    {
        MarketAssumptionDrafter.AnnualRateBps(100_000_000, 112_000_000, 365).Should().Be(1200);
        MarketAssumptionDrafter.AnnualRateBps(100_000_000, 106_000_000, 730).Should().Be(300);
    }

    [Fact]
    public async Task LessThanAYearOfHistoryDraftsNothing()
    {
        await PriceAsync(120_000_000, daysAgo: 200);
        await PriceAsync(135_000_000, daysAgo: 0);

        (await DraftAsync()).Should().Be(0);
        (await AssumptionsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AYearOfHistoryDraftsOnceAndNeverApprovesItself()
    {
        await PriceAsync(120_000_000, daysAgo: 400);
        await PriceAsync(135_000_000, daysAgo: 0);

        (await DraftAsync()).Should().Be(1);
        (await DraftAsync()).Should().Be(0, "đã có nháp đúng số này");

        var draft = (await AssumptionsAsync()).Should().ContainSingle().Subject;
        draft.Status.Should().Be(AssumptionStatus.Draft);
        draft.RateBps.Should().Be(1250);
        draft.SourceUrl.Should().Be("https://24h.com.vn/");
        draft.CreatedBy.Should().Be(MarketAssumptionDrafter.SystemActor);
    }

    [Fact]
    public async Task ANewNumberReplacesTheOldSystemDraftButASmallMoveFromTheApprovedOneDoesNot()
    {
        await PriceAsync(120_000_000, daysAgo: 400);
        await PriceAsync(135_000_000, daysAgo: 1);
        await DraftAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var draft = (await AssumptionsAsync()).Single();
            await scope.ServiceProvider.GetRequiredService<IAssumptionService>().ApproveAsync(draft.Id, MarketAssumptionDrafter.SystemActor, null);
        }

        await PriceAsync(136_000_000, daysAgo: 0);
        (await DraftAsync()).Should().Be(0, "lệch 0,83 điểm %, dưới ngưỡng 2 điểm");

        await PriceAsync(150_000_000, daysAgo: 0);
        (await DraftAsync()).Should().Be(1);
        await PriceAsync(155_000_000, daysAgo: 0);
        (await DraftAsync()).Should().Be(1);

        var all = await AssumptionsAsync();
        all.Count(a => a.Status == AssumptionStatus.Draft).Should().Be(1, "nháp cũ của hệ thống bị gỡ");
        all.Single(a => a.Status == AssumptionStatus.Approved).RateBps.Should().Be(1250, "không tự áp");
    }
}
