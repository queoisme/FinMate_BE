using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.MarketData;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Mục tiêu tiết kiệm theo giá vàng: gắn, lấy giá mốc, và cảnh báo khi giá làm ngày đạt lùi xa.</summary>
[Collection("Integration")]
public class GoalMarketTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const long Gold = 135_000_000;

    private readonly WebApplicationFactory<FinMate.API.Program> _factory;
    private readonly HttpClient _client;

    public GoalMarketTests(AuthApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMarketAlertTrigger>();
            services.AddSingleton<IMarketAlertTrigger>(new NoTrigger());
        }));
        _client = _factory.CreateClient();
    }

    private sealed class NoTrigger : IMarketAlertTrigger
    {
        public void Enqueue()
        {
        }
    }

    public async Task InitializeAsync()
    {
        await InDbAsync(async db =>
        {
            await db.ForecastAlerts.Where(a => a.Kind == ForecastAlertKind.GoalMarketShift).ExecuteDeleteAsync();
            await db.GoldPrices.ExecuteDeleteAsync();
            return 0;
        });
        await SetFlagAsync(true);
        await GoldPriceAsync(Gold, processed: true);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> InDbAsync<T>(Func<FinMateDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private async Task SetFlagAsync(bool enabled)
    {
        var adminId = await InDbAsync(db => db.Users.Where(u => u.RoleAssignment!.RoleId == UserRole.Admin).Select(u => u.Id).FirstAsync());
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
        var current = await config.GetActiveAsync();
        await config.SaveAsync(adminId, null, current.Version, current.Content with { Features = current.Content.Features with { GoalMarketAlerts = enabled } }, "thử cảnh báo mục tiêu");
        scope.ServiceProvider.GetRequiredService<ISystemConfigProvider>().Invalidate();
    }

    private Task GoldPriceAsync(long sell, bool processed = false) => InDbAsync(db =>
    {
        var now = DateTimeOffset.UtcNow;
        db.GoldPrices.Add(new GoldPrice
        {
            Id = Guid.NewGuid(), GoldKey = "sjc_bar", BuyCents = sell - 5_000_000, SellCents = sell, FirstSeenAt = now, Origin = FuelPriceOrigin.Sources,
            AlertsProcessedAt = processed ? now : null,
        });
        return db.SaveChangesAsync();
    });

    private async Task<(Guid UserId, string Token)> UserAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Khách", Role = UserRole.User, CreatedAt = now, UpdatedAt = now, EmailVerifiedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();

    /// <summary>Mục tiêu 100 triệu theo giá vàng, đã có 20 triệu, góp đều 5 triệu mỗi tháng trong 3 tháng qua.</summary>
    private async Task<Guid> SavingForGoldAsync(Guid userId)
    {
        var goalId = Guid.NewGuid();
        await InDbAsync(db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.SavingGoals.Add(new SavingGoal
            {
                Id = goalId, UserId = userId, Name = "Mua vàng cưới", TargetCents = 100_000_000, SavedCents = 20_000_000, InitialSavedCents = 5_000_000,
                Status = SavingGoalStatus.Active, CreatedAt = now.AddMonths(-4), UpdatedAt = now,
                MarketTopic = "gold", MarketBaselineCents = Gold, MarketBaselineAt = now.AddMonths(-4),
            });
            for (var month = 1; month <= 3; month++)
            {
                db.GoalContributions.Add(new GoalContribution { Id = Guid.NewGuid(), SavingGoalId = goalId, UserId = userId, AmountCents = 5_000_000, ContributedAt = now.AddMonths(-month), CreatedAt = now });
            }

            return db.SaveChangesAsync();
        });
        return goalId;
    }

    private async Task ProcessAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMarketAlertService>().ProcessDueAsync();
    }

    private Task<List<ForecastAlert>> AlertsAsync(Guid userId)
        => InDbAsync(db => db.ForecastAlerts.AsNoTracking().Where(a => a.UserId == userId && a.Kind == ForecastAlertKind.GoalMarketShift).ToListAsync());

    [Fact]
    public async Task TopicsListTheCurrentGoldPrice()
    {
        var (_, token) = await UserAsync();

        var topic = (await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/saving-goals/market-topics", token))).EnumerateArray().Single();

        topic.GetProperty("key").GetString().Should().Be("gold");
        topic.GetProperty("priceCents").GetInt64().Should().Be(Gold);
    }

    [Fact]
    public async Task LinkingTakesTheCurrentPriceAsBaselineAndChangingTheTargetTakesItAgain()
    {
        var (userId, token) = await UserAsync();

        var created = await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/saving-goals", token, new { name = "Mua 1 lượng vàng", targetCents = Gold, marketTopic = "gold" }));
        var goalId = created.GetProperty("id").GetGuid();
        created.GetProperty("marketTopic").GetString().Should().Be("gold");

        await GoldPriceAsync(Gold + 5_000_000, processed: true);
        (await SendAsync(HttpMethod.Patch, $"/api/v1/saving-goals/{goalId}", token, new { name = "Mua 1 lượng vàng", targetCents = Gold, deadline = (DateTimeOffset?)null }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await InDbAsync(db => db.SavingGoals.SingleAsync(g => g.Id == goalId))).MarketBaselineCents.Should().Be(Gold, "không đổi số tiền thì giữ mốc");

        await SendAsync(HttpMethod.Patch, $"/api/v1/saving-goals/{goalId}", token, new { name = "Mua 1 lượng vàng", targetCents = Gold + 5_000_000, deadline = (DateTimeOffset?)null });
        (await InDbAsync(db => db.SavingGoals.SingleAsync(g => g.Id == goalId))).MarketBaselineCents.Should().Be(Gold + 5_000_000);

        var forecast = await DataAsync(await SendAsync(HttpMethod.Get, $"/api/v1/forecast/goals/{goalId}", token));
        forecast.GetProperty("market").GetProperty("changeBps").GetInt32().Should().Be(0);

        await SendAsync(HttpMethod.Patch, $"/api/v1/saving-goals/{goalId}", token, new { name = "Mua 1 lượng vàng", targetCents = Gold, deadline = (DateTimeOffset?)null, marketTopic = "" });
        var unlinked = await InDbAsync(db => db.SavingGoals.SingleAsync(g => g.Id == goalId && g.UserId == userId));
        (unlinked.MarketTopic, unlinked.MarketBaselineCents).Should().Be((null, null));
    }

    [Fact]
    public async Task AnUnknownTopicIsRefused()
    {
        var (_, token) = await UserAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/saving-goals", token, new { name = "Mua nhà", targetCents = 1_000_000_000, marketTopic = "house" });

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await response.Content.ReadAsStringAsync()).Should().Contain(GoalMarketErrorCodes.TopicUnavailable);
    }

    [Fact]
    public async Task AGoldRiseThatPushesTheGoalBackRaisesOneAlertWithActions()
    {
        var (userId, token) = await UserAsync();
        var goalId = await SavingForGoldAsync(userId);
        await GoldPriceAsync(Gold * 110 / 100);

        await ProcessAsync();
        await ProcessAsync();

        var alert = (await AlertsAsync(userId)).Should().ContainSingle("giá đã xét thì không xét lại").Subject;
        alert.Title.Should().Be("Giá vàng tăng ảnh hưởng mục tiêu của bạn");
        alert.Body.Should().Contain("Mua vàng cưới").And.Contain("10%").And.NotContain("nên mua");
        var list = (await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/forecast/alerts", token))).ToString();
        list.Should().Contain("update_goal_target").And.Contain(goalId.ToString()).And.Contain("110000000");
    }

    [Fact]
    public async Task ASmallRiseOrTheFlagOffSendsNothingAndIsNotResentLater()
    {
        var (userId, _) = await UserAsync();
        await SavingForGoldAsync(userId);

        await GoldPriceAsync(Gold * 102 / 100);
        await ProcessAsync();
        (await AlertsAsync(userId)).Should().BeEmpty("tăng 2% dưới ngưỡng 5%");

        await SetFlagAsync(false);
        await GoldPriceAsync(Gold * 120 / 100);
        await ProcessAsync();
        await SetFlagAsync(true);
        await ProcessAsync();

        (await AlertsAsync(userId)).Should().BeEmpty("bật cờ không gửi bù giá đã xét");
    }
}
