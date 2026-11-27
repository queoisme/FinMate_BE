using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Kpi;

/// <summary>
/// <c>GET /ops/kpi/summary</c> qua HTTP: quyền <c>ViewProductKpi</c> (không phải <c>ViewDashboard</c>), 2FA nội bộ, khoảng ngày,
/// và kịch bản §5 trên một khoảng đầu năm 2025 mà không test nào khác ghi dữ liệu vào.
/// </summary>
[Collection("Integration")]
public class OpsKpiControllerTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Summary = "/api/v1/ops/kpi/summary";
    private const string RolePermissions = "/api/v1/admin/role-permissions";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static KpiScenario? _scenario;

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public OpsKpiControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private KpiScenario Scenario => _scenario!;

    private string Window => $"{Summary}?from={Scenario.Day0:yyyy-MM-dd}&to={Scenario.LastDay:yyyy-MM-dd}";

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_scenario is null)
            {
                var scenario = new KpiScenario(new DateOnly(2025, 2, 3));   // thứ Hai
                using var scope = _factory.Services.CreateScope();
                await scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
                _scenario = scenario;
            }
        }
        finally
        {
            SeedLock.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> TokenAsync(UserRole role, bool opsVerified = false, WebApplicationFactory<Program>? factory = null)
    {
        using var scope = (factory ?? _factory).Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"kpiapi{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user, opsVerified);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? payload = null, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return (client ?? _client).SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    private async Task WithStaffCapabilitiesAsync(IEnumerable<string> extra, Func<Task> body)
    {
        var admin = await TokenAsync(UserRole.Admin);
        var defaults = OpsPermissions.Names(OpsPermissions.For(UserRole.Staff));
        var set = await SendAsync(HttpMethod.Put, $"{RolePermissions}/Staff", admin, new { capabilities = defaults.Concat(extra).ToList(), reason = "kiểm thử KPI" });
        set.StatusCode.Should().Be(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
        try
        {
            await body();
        }
        finally
        {
            await SendAsync(HttpMethod.Put, $"{RolePermissions}/Staff", admin, new { capabilities = defaults, reason = "trả lại mặc định" });
        }
    }

    [Theory]
    [InlineData(UserRole.OpsManager)]
    [InlineData(UserRole.Admin)]
    public async Task ManagerAndAdmin_GetTheWeeklyScenario(UserRole role)
    {
        var data = await DataAsync(await SendAsync(HttpMethod.Get, $"{Window}&granularity=week", await TokenAsync(role)));

        var periods = data.GetProperty("periods").EnumerateArray().ToList();
        periods.Select(p => p.GetProperty("signups").GetInt64()).Should().Equal(KpiScenario.Expected.Signups);
        periods.Select(p => p.GetProperty("transactingUsers").GetInt64()).Should().Equal(KpiScenario.Expected.TransactingUsers);
        periods.Select(p => p.GetProperty("subscriptions").GetProperty("revenueCents").GetInt64()).Should().Equal(KpiScenario.Expected.Revenue);
        periods.Select(p => p.GetProperty("subscriptions").GetProperty("payingTotal").GetInt64()).Should().Equal(KpiScenario.Expected.PayingTotal);
        periods[0].GetProperty("transactions").GetProperty("bySource").GetProperty("manual").GetInt64().Should().Be(5);
        periods[0].GetProperty("transactions").GetProperty("bySource").GetProperty("voice").GetString().Should().Be("<5");
        periods[1].GetProperty("subscriptions").GetProperty("payingByPlan").GetProperty(Scenario.MaxPlan).GetString().Should().Be("<5");
        periods[1].GetProperty("notifications").GetProperty("conversionRate").GetDouble().Should().Be(0.5);
    }

    [Theory]
    [InlineData("day")]
    [InlineData("week")]
    [InlineData("month")]
    public async Task NoResponseCarriesAUserIdEmailOrASingleOrderAmount(string granularity)
    {
        var response = await SendAsync(HttpMethod.Get, $"{Window}&granularity={granularity}", await TokenAsync(UserRole.Admin));
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);

        foreach (var id in Scenario.UserIds)
        {
            text.Should().NotContainEquivalentOf(id.ToString());
            text.Should().NotContainEquivalentOf(id.ToString("N"));
        }

        foreach (var email in Scenario.Emails)
        {
            text.Should().NotContainEquivalentOf(email);
        }

        text.Should().NotContain("amountCents").And.NotContain("userId");
        if (granularity != "day")
        {
            // Đơn gói max chỉ có một, trong tuần 2 cùng một đơn pro: theo tuần/tháng chỉ thấy tổng, không thấy giá của đơn đó.
            text.Should().NotContain(KpiScenario.MaxPrice.ToString());
        }
    }

    [Fact]
    public async Task StaffIsRefused_EvenWithViewDashboard_ButNotOnceGrantedViewProductKpi()
    {
        var staff = await TokenAsync(UserRole.Staff);
        (await SendAsync(HttpMethod.Get, Window, staff)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await WithStaffCapabilitiesAsync(["ViewDashboard"], async () =>
            (await SendAsync(HttpMethod.Get, Window, staff)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "ViewDashboard là dashboard ticket, không mở KPI"));

        await WithStaffCapabilitiesAsync(["ViewProductKpi"], async () =>
            (await SendAsync(HttpMethod.Get, Window, staff)).StatusCode.Should().Be(HttpStatusCode.OK, "Admin cấp được quyền này như mọi quyền khác"));
    }

    [Fact]
    public async Task OrdinaryUserIsRefused()
        => (await SendAsync(HttpMethod.Get, Window, await TokenAsync(UserRole.User))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task WithTwoFactorOn_AnUnverifiedTokenIsRefused_AndAVerifiedOneIsServed()
    {
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<OpsAuthOptions>();
            services.AddSingleton(new OpsAuthOptions(true));
        }));
        var client = factory.CreateClient();

        (await SendAsync(HttpMethod.Get, Window, await TokenAsync(UserRole.OpsManager, false, factory), client: client))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, Window, await TokenAsync(UserRole.OpsManager, true, factory), client: client))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("from=2025-01-01&to=2026-01-02")]          // 367 ngày
    [InlineData("from=2025-02-10&to=2025-02-01")]          // from sau to
    [InlineData("from=2025-02-01&to=2099-01-01")]          // to sau hôm nay
    [InlineData("from=2025-02-01&to=2025-02-10&granularity=year")]
    [InlineData("to=2025-02-10")]
    public async Task BadRanges_Get422(string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"{Summary}?{query}", await TokenAsync(UserRole.Admin));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("KPI_INVALID_RANGE");
    }

    [Fact]
    public async Task ExactlyA366DayRangeIsAccepted()
    {
        var to = Scenario.LastDay;
        var response = await SendAsync(HttpMethod.Get, $"{Summary}?from={to.AddDays(-365):yyyy-MM-dd}&to={to:yyyy-MM-dd}&granularity=month", await TokenAsync(UserRole.Admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
