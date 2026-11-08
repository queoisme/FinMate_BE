using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.API.Controllers.Admin;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Application.Subscriptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

public sealed class FakeSePayClient : ISePayClient
{
    public ConcurrentDictionary<string, SePayOrderInfo> Orders { get; } = new();
    public ConcurrentDictionary<string, bool> Failing { get; } = new();
    public ConcurrentDictionary<string, int> Calls { get; } = new();
    public bool Down { get; set; }

    public Task<SePayOrderInfo?> GetOrderAsync(string invoiceNumber, CancellationToken ct = default)
    {
        Calls.AddOrUpdate(invoiceNumber, 1, (_, count) => count + 1);
        if (Down || Failing.ContainsKey(invoiceNumber))
        {
            throw new FinMate.Application.Common.Exceptions.SePayUnavailableException();
        }

        return Task.FromResult(Orders.GetValueOrDefault(invoiceNumber));
    }

    public void MarkPaid(string invoice, long amount, string currency = "VND")
        => Orders[invoice] = new SePayOrderInfo(SePayOrderInfo.StatusCaptured, amount, currency, $"txn-{invoice}");
}

public sealed class RecordingPushService : IPushNotificationService
{
    public ConcurrentBag<(Guid UserId, string Body, string? Action)> Sent { get; } = new();

    public Task NotifyAsync(Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default)
    {
        Sent.Add((userId, body, data?.GetValueOrDefault("action")));
        return Task.CompletedTask;
    }
}

public sealed class UnreadableOcrClient : IOcrServiceClient
{
    public bool IsConfigured => true;

    public Task<OcrServiceResponse> AnalyzeAsync(OcrServiceRequest request, CancellationToken ct = default)
        => Task.FromResult(new OcrServiceResponse(false, null, null, null, [], null, null, false, null, null));
}

public sealed class SubscriptionApiFactory : AuthApiFactory, IAsyncLifetime
{
    public const string IpnKey = "ipn-key-for-tests";
    public const string SecretKey = "secret-key-for-tests";
    public const string WebUrl = "https://pay.finmate.test";
    public const string CheckoutUrl = "https://pay-sandbox.sepay.vn/v1/checkout/init";

    private static readonly Dictionary<string, string> Settings = new()
    {
        ["SEPAY_MERCHANT_ID"] = "MERCHANT_TEST",
        ["SEPAY_CHECKOUT_URL"] = CheckoutUrl,
        ["SEPAY_API_BASE_URL"] = "https://pgapi-sandbox.sepay.vn",
        ["SEPAY_SECRET_KEY"] = SecretKey,
        ["SEPAY_IPN_SECRET_KEY"] = IpnKey,
        ["SUBSCRIPTION_WEB_URL"] = WebUrl,
        ["SUBSCRIPTION_WEB_ORIGINS"] = WebUrl,

        // IPN và đăng nhập trong bộ test cùng một IP. Hạn mức của từng tài khoản không phải thứ đang kiểm ở đây.
        ["RATE_LIMIT_DEFAULT_PER_MINUTE"] = "100000",
    };

    public FakeSePayClient SePay { get; } = new();
    public RecordingPushService Push { get; } = new();

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        foreach (var (name, value) in Settings)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public new async Task DisposeAsync()
    {
        foreach (var name in Settings.Keys)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISePayClient>();
            services.AddSingleton<ISePayClient>(SePay);
            services.RemoveAll<IPushNotificationService>();
            services.AddSingleton<IPushNotificationService>(Push);
            services.RemoveAll<IOcrServiceClient>();
            services.AddSingleton<IOcrServiceClient, UnreadableOcrClient>();
        });
    }
}

[Collection("Integration")]
public class SubscriptionsTests : IClassFixture<SubscriptionApiFactory>, IAsyncLifetime
{
    private const string Password = "Password123!";
    private const string Ops = "/api/v1/ops/subscriptions";
    private const string Web = "/api/v1/billing/web";
    private const string Ipn = "/api/v1/webhooks/sepay/ipn";
    private const string SessionHeader = "X-Billing-Session";
    private const long MonthlyPrice = 79000;
    private const long YearlyPrice = 790000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly SubscriptionApiFactory _factory;
    private readonly HttpClient _client;

    public SubscriptionsTests(SubscriptionApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _factory.SePay.Down = false;
        await _factory.SetFeaturesAsync(subscriptions: true);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record Account(Guid Id, string Email, string Token);
    private record Envelope<T>(T Data);
    private record TokenData(string AccessToken);
    private record IdData(Guid Id);
    private record ErrorData(string Code);
    private record ErrorBody(ErrorData Error);
    private record CheckoutData(string ActionUrl, Dictionary<string, string> Fields, string InvoiceNumber, long AmountCents);
    private record LinkData(string Url);
    private record SessionData(string SessionToken);
    private record OrderData(string InvoiceNumber, string Status);
    private record FeatureData(string Key, bool Enabled, int? MonthlyLimit, int Used);
    private record PeriodData(string PlanCode, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
    private record MineData(bool Enabled, string PlanCode, bool IsFree, DateTimeOffset? ExpiresAt, List<PeriodData> Upcoming, List<FeatureData> Features);
    private record PlanData(Guid Id, string Code, long? MonthlyPriceCents, bool IsActive, bool IsDefaultFree, List<FeatureData> Features);

    private static HttpRequestMessage Request(HttpMethod method, string url, string? token = null, object? payload = null, string? session = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (session is not null)
        {
            request.Headers.Add(SessionHeader, session);
        }

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: Json);
        }

        return request;
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri?.AbsolutePath}: {body}");
        return JsonSerializer.Deserialize<Envelope<T>>(body, Json)!.Data;
    }

    private async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => JsonSerializer.Deserialize<ErrorBody>(await response.Content.ReadAsStringAsync(), Json)!.Error.Code;

    private async Task<string> LoginAsync(string email, string password)
        => (await ReadAsync<TokenData>(await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password)))).AccessToken;

    private async Task<Account> RegisterAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, "Sub Test"));
        var token = await LoginAsync(email, Password);
        var id = (await ReadAsync<IdData>(await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", token)))).Id;
        return new Account(id, email, token);
    }

    private Task<HttpResponseMessage> SetRoleAsync(string adminToken, Guid userId, UserRole role)
        => _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/users/{userId}/role", adminToken, new SetUserRoleRequest(role, "kiểm thử")));

    private async Task<Account> AccountWithRoleAsync(UserRole role)
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var account = await RegisterAsync();
        (await SetRoleAsync(admin, account.Id, role)).StatusCode.Should().Be(HttpStatusCode.OK);
        return account with { Token = await LoginAsync(account.Email, Password) };
    }

    private Task<Account> ManagerAsync() => AccountWithRoleAsync(UserRole.OpsManager);

    private async Task<PlanData> CreatePlanAsync(string token, long? monthly = MonthlyPrice, long? yearly = YearlyPrice, int? chatLimit = null)
    {
        var code = $"plus_{Guid.NewGuid():N}"[..20];
        var plan = await ReadAsync<PlanData>(await _client.SendAsync(Request(HttpMethod.Post, $"{Ops}/plans", token, new { code, name = "Plus", monthlyPriceCents = monthly, yearlyPriceCents = yearly })));
        await ReadAsync<PlanData>(await _client.SendAsync(Request(HttpMethod.Put, $"{Ops}/plans/{plan.Id}/features", token, new[]
        {
            new { key = SubscriptionFeatures.AiChat, enabled = true, monthlyLimit = chatLimit },
            new { key = SubscriptionFeatures.VoiceInput, enabled = true, monthlyLimit = (int?)null },
        })));
        return await ReadAsync<PlanData>(await _client.SendAsync(Request(HttpMethod.Patch, $"{Ops}/plans/{plan.Id}", token, new { isActive = true })));
    }

    private async Task<string> WebSessionAsync(string token)
    {
        var link = await ReadAsync<LinkData>(await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/subscriptions/web-session", token, new { })));
        var code = new Uri(link.Url).Query.TrimStart('?').Split('&').Single(p => p.StartsWith("code=", StringComparison.Ordinal))["code=".Length..];
        return (await ReadAsync<SessionData>(await _client.PostAsJsonAsync($"{Web}/session", new { code }))).SessionToken;
    }

    private async Task<CheckoutData> CheckoutAsync(Account user, Guid planId, SubscriptionCycle cycle = SubscriptionCycle.Monthly)
    {
        var session = await WebSessionAsync(user.Token);
        return await ReadAsync<CheckoutData>(await _client.SendAsync(Request(HttpMethod.Post, $"{Web}/checkout", payload: new { planId, cycle }, session: session)));
    }

    private Task<HttpResponseMessage> SendIpnAsync(string invoice, string type = "ORDER_PAID", string key = SubscriptionApiFactory.IpnKey)
    {
        var request = Request(HttpMethod.Post, Ipn, payload: new { notification_type = type, order = new { order_invoice_number = invoice, order_amount = "1.00" } });
        request.Headers.Add("X-Secret-Key", key);
        return _client.SendAsync(request);
    }

    private async Task<(Account User, PlanData Plan, CheckoutData Order)> PaidOrderAsync(string managerToken, Account? user = null, PlanData? plan = null)
    {
        user ??= await RegisterAsync();
        plan ??= await CreatePlanAsync(managerToken);
        var order = await CheckoutAsync(user, plan.Id);
        _factory.SePay.MarkPaid(order.InvoiceNumber, order.AmountCents);
        (await SendIpnAsync(order.InvoiceNumber)).StatusCode.Should().Be(HttpStatusCode.OK);
        return (user, plan, order);
    }

    private async Task<MineData> MineAsync(string token)
        => await ReadAsync<MineData>(await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/subscriptions/me", token)));

    private async Task WithDbAsync(Func<FinMateDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private async Task<T> FromDbAsync<T>(Func<FinMateDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private Task<SubscriptionOrder> OrderAsync(string invoice)
        => FromDbAsync(db => db.SubscriptionOrders.AsNoTracking().SingleAsync(o => o.InvoiceNumber == invoice));

    private Task<List<UserSubscriptionPeriod>> PeriodsAsync(Guid userId)
        => FromDbAsync(db => db.UserSubscriptionPeriods.AsNoTracking().Where(p => p.UserId == userId).OrderBy(p => p.StartsAt).ToListAsync());

    private Task<HttpResponseMessage> AnalyzeAsync(string token)
        => _client.SendAsync(Request(HttpMethod.Post, "/api/v1/chat/analyze", token, new { text = "cafe 30k" }));

    // ---------- Quyền của Manager ----------

    [Fact]
    public async Task OnlyManagerAndAdminCanManagePlans()
    {
        (await _client.GetAsync($"{Ops}/plans")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var user = await RegisterAsync();
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/plans", user.Token))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var staff = await AccountWithRoleAsync(UserRole.Staff);
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/plans", staff.Token))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var manager = await ManagerAsync();
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/plans", manager.Token))).StatusCode.Should().Be(HttpStatusCode.OK);

        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/plans", admin))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADemotedManagerLosesAccessImmediately_EvenWithAStillValidToken()
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var manager = await ManagerAsync();
        (await SetRoleAsync(admin, manager.Id, UserRole.User)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/plans", manager.Token))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ManagerCanCreateEditPriceAssignFeaturesAndDelete_AndEachWriteIsAudited()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token, chatLimit: 100);
        plan.Features.Single(f => f.Key == SubscriptionFeatures.AiChat).MonthlyLimit.Should().Be(100);
        plan.Features.Single(f => f.Key == SubscriptionFeatures.ReceiptScan).Enabled.Should().BeFalse();

        var edited = await ReadAsync<PlanData>(await _client.SendAsync(Request(HttpMethod.Patch, $"{Ops}/plans/{plan.Id}", manager.Token, new { monthlyPriceCents = 99000 })));
        edited.MonthlyPriceCents.Should().Be(99000);

        (await _client.SendAsync(Request(HttpMethod.Delete, $"{Ops}/plans/{plan.Id}", manager.Token))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await FromDbAsync(db => db.SubscriptionPlans.AsNoTracking().SingleAsync(p => p.Id == plan.Id));
        stored.IsActive.Should().BeFalse("xóa gói không được xóa vật lý");

        var events = await FromDbAsync(db => db.AuditLogs.AsNoTracking().Where(a => a.UserId == manager.Id).Select(a => a.EventType).ToListAsync());
        events.Should().Contain(["Ops.SubscriptionPlan.Created", "Ops.SubscriptionPlan.Updated", "Ops.SubscriptionPlan.FeaturesUpdated", "Ops.SubscriptionPlan.Deleted"]);

        var publicPlans = await ReadAsync<List<PlanData>>(await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/subscriptions/plans", manager.Token)));
        publicPlans.Should().NotContain(p => p.Id == plan.Id);
    }

    [Fact]
    public async Task PlanRulesAreEnforced()
    {
        var manager = await ManagerAsync();
        var plans = await ReadAsync<List<PlanData>>(await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/plans", manager.Token)));
        var free = plans.Single(p => p.IsDefaultFree);

        (await _client.SendAsync(Request(HttpMethod.Delete, $"{Ops}/plans/{free.Id}", manager.Token))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await _client.SendAsync(Request(HttpMethod.Patch, $"{Ops}/plans/{free.Id}", manager.Token, new { monthlyPriceCents = 1000 }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var noPrice = await _client.SendAsync(Request(HttpMethod.Post, $"{Ops}/plans", manager.Token, new { code = "no_price", name = "X" }));
        (await ErrorCodeAsync(noPrice)).Should().Be("SUBSCRIPTION_PRICE_REQUIRED");

        var badCode = await _client.SendAsync(Request(HttpMethod.Post, $"{Ops}/plans", manager.Token, new { code = "Có Dấu", name = "X", monthlyPriceCents = 1000 }));
        badCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var plan = await CreatePlanAsync(manager.Token);
        var duplicate = await _client.SendAsync(Request(HttpMethod.Post, $"{Ops}/plans", manager.Token, new { code = plan.Code, name = "X", monthlyPriceCents = 1000 }));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var unknown = await _client.SendAsync(Request(HttpMethod.Put, $"{Ops}/plans/{plan.Id}/features", manager.Token, new[] { new { key = "khong_co", enabled = true, monthlyLimit = (int?)null } }));
        (await ErrorCodeAsync(unknown)).Should().Be("SUBSCRIPTION_UNKNOWN_FEATURE");
    }

    [Fact]
    public async Task ANewPlanIsNotOnSale_UntilItHasAFeature()
    {
        var manager = await ManagerAsync();
        var created = await ReadAsync<PlanData>(await _client.SendAsync(Request(HttpMethod.Post, $"{Ops}/plans", manager.Token, new { code = $"p_{Guid.NewGuid():N}"[..20], name = "Rong", monthlyPriceCents = 1000 })));
        created.IsActive.Should().BeFalse();

        var activate = await _client.SendAsync(Request(HttpMethod.Patch, $"{Ops}/plans/{created.Id}", manager.Token, new { isActive = true }));
        (await ErrorCodeAsync(activate)).Should().Be("SUBSCRIPTION_PLAN_HAS_NO_FEATURES");

        var onSale = await CreatePlanAsync(manager.Token);
        var emptied = await _client.SendAsync(Request(HttpMethod.Put, $"{Ops}/plans/{onSale.Id}/features", manager.Token, Array.Empty<object>()));
        (await ErrorCodeAsync(emptied)).Should().Be("SUBSCRIPTION_PLAN_HAS_NO_FEATURES");
    }

    [Fact]
    public async Task OrderListForManagers_CarriesNoUserId()
    {
        var manager = await ManagerAsync();
        var (user, _, order) = await PaidOrderAsync(manager.Token);

        var body = await (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/orders?status=Paid&limit=100", manager.Token))).Content.ReadAsStringAsync();

        body.Should().Contain(order.InvoiceNumber).And.NotContain(user.Id.ToString()).And.NotContain("userId");
    }

    // ---------- Chặn chức năng ----------

    [Fact]
    public async Task WithTheFlagOff_NothingIsBlockedOrCounted_AndNoNewOrder()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var session = await WebSessionAsync(user.Token);

        await _factory.SetFeaturesAsync(subscriptions: false);

        await AnalyzeAsync(user.Token);
        var usage = await FromDbAsync(db => db.SubscriptionUsages.AsNoTracking().CountAsync(u => u.UserId == user.Id));
        usage.Should().Be(0);

        var checkout = await _client.SendAsync(Request(HttpMethod.Post, $"{Web}/checkout", payload: new { planId = plan.Id, cycle = "Monthly" }, session: session));
        (await ErrorCodeAsync(checkout)).Should().Be("SUBSCRIPTION_DISABLED");
    }

    [Fact]
    public async Task FreeLimitBlocksTheCallAfterItIsUsedUp()
    {
        var user = await RegisterAsync();
        await WithDbAsync(db => db.SubscriptionUsages.AddAsync(new SubscriptionUsage
        {
            UserId = user.Id,
            FeatureKey = SubscriptionFeatures.AiChat,
            Month = CurrentMonth(),
            Used = 1_000_000,
        }).AsTask().ContinueWith(_ => db.SaveChangesAsync()).Unwrap());

        (await ErrorCodeAsync(await AnalyzeAsync(user.Token))).Should().Be("SUBSCRIPTION_LIMIT_REACHED");
    }

    [Fact]
    public async Task AFailedAiCall_GivesTheTurnBack()
    {
        var user = await RegisterAsync();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "file", "a.m4a");
        var request = Request(HttpMethod.Post, "/api/v1/chat/analyze-voice", user.Token);
        request.Content = form;

        var response = await _client.SendAsync(request); // STT không có trong bộ test nên lỗi
        response.IsSuccessStatusCode.Should().BeFalse();

        var used = await FromDbAsync(db => db.SubscriptionUsages.AsNoTracking().Where(u => u.UserId == user.Id).SumAsync(u => u.Used));
        used.Should().Be(0);
    }

    [Fact]
    public async Task AnUnreadableReceipt_GivesTheTurnBack()
    {
        var user = await RegisterAsync();
        using var form = new MultipartFormDataContent();
        var image = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "file", "a.jpg");
        var request = Request(HttpMethod.Post, "/api/v1/transactions/scan-receipt", user.Token);
        request.Content = form;

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await FromDbAsync(db => db.SubscriptionUsages.AsNoTracking().Where(u => u.UserId == user.Id).SumAsync(u => u.Used))).Should().Be(0);
    }

    [Fact]
    public async Task RetryingASavedTransaction_IsFreeOnce_EvenAtTheLimit_ButNotForever()
    {
        var user = await RegisterAsync();
        var account = await ReadAsync<IdData>(await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/financial-accounts", user.Token, new { accountName = "Tien mat", accountType = "Cash", initialBalanceCents = 0 })));
        var clientRequestId = Guid.NewGuid();
        (await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", user.Token, new
        {
            financialAccountId = account.Id,
            amountCents = 30000,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            clientRequestId,
        }))).IsSuccessStatusCode.Should().BeTrue();
        await WithDbAsync(async db =>
        {
            db.SubscriptionUsages.Add(new SubscriptionUsage { UserId = user.Id, FeatureKey = SubscriptionFeatures.AiChat, Month = CurrentMonth(), Used = 1_000_000 });
            await db.SaveChangesAsync();
        });

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISubscriptionEntitlementService>();

        (await service.ReserveAsync(user.Id, SubscriptionFeatures.AiChat, clientRequestId)).Should().BeNull();

        var second = () => service.ReserveAsync(user.Id, SubscriptionFeatures.AiChat, clientRequestId);
        (await second.Should().ThrowAsync<FinMate.Application.Common.Exceptions.BusinessRuleException>()).Which.ErrorCode.Should().Be("SUBSCRIPTION_LIMIT_REACHED");

        var unknownId = () => service.ReserveAsync(user.Id, SubscriptionFeatures.AiChat, Guid.NewGuid());
        await unknownId.Should().ThrowAsync<FinMate.Application.Common.Exceptions.BusinessRuleException>();
    }

    [Fact]
    public async Task ParallelRequests_CannotExceedTheLimit()
    {
        var user = await RegisterAsync();
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token, chatLimit: 3);
        await PaidOrderAsync(manager.Token, user, plan);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ISubscriptionEntitlementService>();
            try
            {
                await service.ReserveAsync(user.Id, SubscriptionFeatures.AiChat);
                return true;
            }
            catch (FinMate.Application.Common.Exceptions.BusinessRuleException)
            {
                return false;
            }
        }));

        attempts.Count(ok => ok).Should().Be(3);
    }

    [Fact]
    public async Task AFeatureNotInThePlan_IsLocked()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var (user, _, _) = await PaidOrderAsync(manager.Token, plan: plan);

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISubscriptionEntitlementService>();
        var act = () => service.ReserveAsync(user.Id, SubscriptionFeatures.ReceiptScan);
        (await act.Should().ThrowAsync<FinMate.Application.Common.Exceptions.BusinessRuleException>()).Which.ErrorCode.Should().Be("SUBSCRIPTION_FEATURE_LOCKED");
    }

    // ---------- Website ----------

    [Fact]
    public async Task HandoffCodeWorksOnce_AndCarriesNoJwt()
    {
        var user = await RegisterAsync();
        var link = await ReadAsync<LinkData>(await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/subscriptions/web-session", user.Token, new { })));
        link.Url.Should().StartWith($"{SubscriptionApiFactory.WebUrl}/subscribe?code=").And.NotContain(user.Token);

        var code = new Uri(link.Url).Query["?code=".Length..];
        (await _client.PostAsJsonAsync($"{Web}/session", new { code })).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await _client.PostAsJsonAsync($"{Web}/session", new { code });
        (await ErrorCodeAsync(again)).Should().Be("SUBSCRIPTION_INVALID_WEB_CODE");
    }

    [Fact]
    public async Task BillingSession_OpensOnlyTheBillingApi_AndJwtDoesNotOpenIt()
    {
        var user = await RegisterAsync();
        var session = await WebSessionAsync(user.Token);

        (await _client.SendAsync(Request(HttpMethod.Get, $"{Web}/plans", session: session))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/transactions", session: session))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", session))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Web}/plans", user.Token))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Web}/plans", session: "gia-mao"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Checkout_SignsTheFormWithTheRealPriceInDong_NotDividedBy100()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();

        var form = await CheckoutAsync(user, plan.Id, SubscriptionCycle.Yearly);

        form.ActionUrl.Should().Be(SubscriptionApiFactory.CheckoutUrl);
        form.Fields["order_amount"].Should().Be("790000");
        form.Fields["currency"].Should().Be("VND");
        form.Fields["merchant"].Should().Be("MERCHANT_TEST");
        form.Fields["order_invoice_number"].Should().Be(form.InvoiceNumber);
        form.Fields.Should().NotContainKey("customer_id");
        form.Fields["success_url"].Should().StartWith($"{SubscriptionApiFactory.WebUrl}/subscribe/result?invoice={form.InvoiceNumber}");

        var signed = new Dictionary<string, string>(form.Fields);
        signed.Remove(SePaySignature.FieldName);
        form.Fields[SePaySignature.FieldName].Should().Be(SePaySignature.Sign(signed, SubscriptionApiFactory.SecretKey));
        JsonSerializer.Serialize(form).Should().NotContain(SubscriptionApiFactory.SecretKey);

        (await OrderAsync(form.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Pending);
        (await PeriodsAsync(user.Id)).Should().BeEmpty("tạo đơn chưa cấp quyền lợi gì");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckoutRefusesPlansNotOnSale(bool freePlan)
    {
        var manager = await ManagerAsync();
        var user = await RegisterAsync();
        Guid planId;
        if (freePlan)
        {
            planId = (await ReadAsync<List<PlanData>>(await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/plans", manager.Token)))).Single(p => p.IsDefaultFree).Id;
        }
        else
        {
            var plan = await CreatePlanAsync(manager.Token);
            await _client.SendAsync(Request(HttpMethod.Delete, $"{Ops}/plans/{plan.Id}", manager.Token));
            planId = plan.Id;
        }

        var session = await WebSessionAsync(user.Token);
        var response = await _client.SendAsync(Request(HttpMethod.Post, $"{Web}/checkout", payload: new { planId, cycle = "Monthly" }, session: session));
        (await ErrorCodeAsync(response)).Should().Be("SUBSCRIPTION_PLAN_NOT_AVAILABLE");
    }

    [Fact]
    public async Task TheWebsiteCanSeeWhenANewPurchaseWouldStart()
    {
        var manager = await ManagerAsync();
        var (user, _, _) = await PaidOrderAsync(manager.Token);
        var session = await WebSessionAsync(user.Token);

        var mine = await ReadAsync<JsonElement>(await _client.SendAsync(Request(HttpMethod.Get, $"{Web}/me", session: session)));

        mine.GetProperty("nextPurchaseStartsAt").GetDateTimeOffset().Should().Be(mine.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("""{"planId":"00000000-0000-0000-0000-000000000001","cycle":7}""")]
    [InlineData("""{"planId":"00000000-0000-0000-0000-000000000001"}""")]
    public async Task CheckoutRejectsAMissingOrUnknownCycle(string body)
    {
        var user = await RegisterAsync();
        var session = await WebSessionAsync(user.Token);
        var request = Request(HttpMethod.Post, $"{Web}/checkout", session: session);
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnotherUsersOrderIsNotVisible()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var owner = await RegisterAsync();
        var order = await CheckoutAsync(owner, plan.Id);

        var other = await RegisterAsync();
        var session = await WebSessionAsync(other.Token);
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Web}/orders/{order.InvoiceNumber}", session: session))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- IPN và ghi nhận tiền ----------

    [Fact]
    public async Task Ipn_WithAWrongKey_IsRejectedAndGrantsNothing()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        _factory.SePay.MarkPaid(order.InvoiceNumber, order.AmountCents);

        (await SendIpnAsync(order.InvoiceNumber, key: "sai")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await OrderAsync(order.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Pending);
    }

    [Fact]
    public async Task Ipn_ThatClaimsPaid_GrantsNothingWhenSePaySaysOtherwise()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);

        (await SendIpnAsync(order.InvoiceNumber)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await OrderAsync(order.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Pending);
        (await PeriodsAsync(user.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task PaidIpn_ActivatesThePlan_AndAnswersWhatSePayExpects()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        _factory.SePay.MarkPaid(order.InvoiceNumber, order.AmountCents);

        var response = await SendIpnAsync(order.InvoiceNumber);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"success":true}""");
        var mine = await MineAsync(user.Token);
        mine.PlanCode.Should().Be(plan.Code);
        mine.IsFree.Should().BeFalse();
        (await OrderAsync(order.InvoiceNumber)).SepayTransactionId.Should().Be($"txn-{order.InvoiceNumber}");
        _factory.Push.Sent.Should().Contain(p => p.UserId == user.Id && p.Action == "subscription_activated" && !p.Body.Contains(order.AmountCents.ToString()));
    }

    [Fact]
    public async Task RepeatedIpn_DoesNotGrantTwice()
    {
        var manager = await ManagerAsync();
        var (user, _, order) = await PaidOrderAsync(manager.Token);

        (await SendIpnAsync(order.InvoiceNumber)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendIpnAsync(order.InvoiceNumber)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PeriodsAsync(user.Id)).Should().HaveCount(1);
    }

    [Theory]
    [InlineData(1, "VND")]
    [InlineData(MonthlyPrice, "USD")]
    public async Task AmountOrCurrencyMismatch_GoesToReview_WithoutGranting(long paid, string currency)
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        _factory.SePay.MarkPaid(order.InvoiceNumber, paid, currency);

        (await SendIpnAsync(order.InvoiceNumber)).StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await OrderAsync(order.InvoiceNumber);
        stored.Status.Should().Be(SubscriptionOrderStatus.NeedsReview);
        stored.ReviewReason.Should().Be("amount_mismatch");
        (await PeriodsAsync(user.Id)).Should().BeEmpty();

        var page = await ReadAsync<JsonElement>(await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/orders?status=NeedsReview", manager.Token)));
        page.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("invoiceNumber").GetString()).Should().Contain(order.InvoiceNumber);
    }

    [Fact]
    public async Task UnknownInvoice_IsAcknowledgedWithoutSideEffects()
        => (await SendIpnAsync("KHONG_CO")).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task WhenSePayIsDown_IpnFails_ThenReconcileGrantsLater()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        _factory.SePay.MarkPaid(order.InvoiceNumber, order.AmountCents);

        _factory.SePay.Down = true;
        (await SendIpnAsync(order.InvoiceNumber)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await PeriodsAsync(user.Id)).Should().BeEmpty();

        _factory.SePay.Down = false;
        await ReconcileAsync(DateTimeOffset.UtcNow.AddMinutes(11));

        (await PeriodsAsync(user.Id)).Should().HaveCount(1);
        (await OrderAsync(order.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Paid);
    }

    [Fact]
    public async Task Reconcile_ExpiresOverdueUnpaidOrders_ButNotWhileSePayIsDown()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        var later = DateTimeOffset.UtcNow.AddHours(25);

        _factory.SePay.Down = true;
        await ReconcileAsync(later);
        (await OrderAsync(order.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Pending);

        _factory.SePay.Down = false;
        await ReconcileAsync(later.AddMinutes(11));
        (await OrderAsync(order.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Expired);
    }

    [Fact]
    public async Task MoneyArrivingForAnExpiredOrder_IsStillHonoured()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        await ReconcileAsync(DateTimeOffset.UtcNow.AddHours(25));

        _factory.SePay.MarkPaid(order.InvoiceNumber, order.AmountCents);
        (await SendIpnAsync(order.InvoiceNumber)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await OrderAsync(order.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Paid);
        (await PeriodsAsync(user.Id)).Should().HaveCount(1);
    }

    [Fact]
    public async Task MoneyArrivingForAnExpiredOrder_IsFoundByReconcile_WhenTheIpnIsLost()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        await ReconcileAsync(DateTimeOffset.UtcNow.AddHours(25));
        (await OrderAsync(order.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Expired);

        _factory.SePay.MarkPaid(order.InvoiceNumber, order.AmountCents);
        await ReconcileAsync(DateTimeOffset.UtcNow.AddHours(32));

        (await OrderAsync(order.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Paid);
    }

    [Theory]
    [InlineData("/api/v1/billing/web/plans", SubscriptionApiFactory.WebUrl, true)]
    [InlineData("/api/v1/transactions", SubscriptionApiFactory.WebUrl, false)]
    [InlineData("/api/v1/ops/me", SubscriptionApiFactory.WebUrl, false)]
    [InlineData("/api/v1/billing/web/plans", "https://evil.test", false)]
    public async Task TheWebsiteOriginCanCallOnlyTheBillingApi(string path, string origin, bool allowed)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().Be(allowed);
    }

    [Fact]
    public async Task WithTheFlagOff_PaidMoneyIsStillRecorded()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        _factory.SePay.MarkPaid(order.InvoiceNumber, order.AmountCents);

        await _factory.SetFeaturesAsync(subscriptions: false);
        (await SendIpnAsync(order.InvoiceNumber)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PeriodsAsync(user.Id)).Should().HaveCount(1);
    }

    [Fact]
    public async Task DuringMaintenance_IpnStillGetsThrough_WhileOtherApisAreBlocked()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        _factory.SePay.MarkPaid(order.InvoiceNumber, order.AmountCents);

        await SetMaintenanceAsync(true);
        try
        {
            (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/subscriptions/me", user.Token))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await SendIpnAsync(order.InvoiceNumber)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await SetMaintenanceAsync(false);
        }

        (await PeriodsAsync(user.Id)).Should().HaveCount(1);
    }

    private async Task SetMaintenanceAsync(bool enabled)
    {
        const string url = "/api/v1/admin/system-config";
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var current = await ReadAsync<JsonElement>(await _client.SendAsync(Request(HttpMethod.Get, url, admin)));
        var saved = await _client.SendAsync(Request(HttpMethod.Put, url, admin, new
        {
            expectedVersion = current.GetProperty("version").GetInt32(),
            content = new { maintenance = new { enabled, message = "Bao tri" } },
            note = "kiem thu",
        }));
        saved.EnsureSuccessStatusCode();
    }

    // ---------- Vòng đời ----------

    [Fact]
    public async Task TwoOrdersSettlingAtOnce_DoNotOverlap()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var first = await CheckoutAsync(user, plan.Id);
        var second = await CheckoutAsync(user, plan.Id);
        _factory.SePay.MarkPaid(first.InvoiceNumber, first.AmountCents);
        _factory.SePay.MarkPaid(second.InvoiceNumber, second.AmountCents);

        await Task.WhenAll(SendIpnAsync(first.InvoiceNumber), SendIpnAsync(second.InvoiceNumber));

        var periods = await PeriodsAsync(user.Id);
        periods.Should().HaveCount(2, "trả hai lần thì được hai kỳ, khách không mất tiền");
        periods[1].StartsAt.Should().Be(periods[0].EndsAt);
    }

    [Fact]
    public async Task BuyingAnotherPlanWhileActive_StartsAfterTheCurrentOneEnds()
    {
        var manager = await ManagerAsync();
        var (user, plan, _) = await PaidOrderAsync(manager.Token);
        var other = await CreatePlanAsync(manager.Token);

        await PaidOrderAsync(manager.Token, user, other);

        var mine = await MineAsync(user.Token);
        mine.PlanCode.Should().Be(plan.Code, "gói cũ vẫn chạy tới hết kỳ");
        mine.Upcoming.Should().ContainSingle().Which.PlanCode.Should().Be(other.Code);
        mine.Upcoming[0].StartsAt.Should().Be(mine.ExpiresAt!.Value);
    }

    [Fact]
    public async Task ExpiredPlan_FallsBackToFree_AndDataIsKept()
    {
        var manager = await ManagerAsync();
        var (user, _, _) = await PaidOrderAsync(manager.Token);

        await WithDbAsync(db => db.UserSubscriptionPeriods.Where(p => p.UserId == user.Id).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.StartsAt, p => p.StartsAt.AddMonths(-2)).SetProperty(p => p.EndsAt, p => p.EndsAt.AddMonths(-2))));

        var mine = await MineAsync(user.Token);
        mine.IsFree.Should().BeTrue();
        mine.PlanCode.Should().Be("free");
        mine.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task ManagerEditingAPlan_DoesNotChangeWhatAPayingCustomerBought()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token, chatLimit: 500);
        var (user, _, _) = await PaidOrderAsync(manager.Token, plan: plan);

        await _client.SendAsync(Request(HttpMethod.Put, $"{Ops}/plans/{plan.Id}/features", manager.Token, new[]
        {
            new { key = SubscriptionFeatures.AiChat, enabled = false, monthlyLimit = (int?)null },
            new { key = SubscriptionFeatures.VoiceInput, enabled = true, monthlyLimit = (int?)null },
        }));
        await _client.SendAsync(Request(HttpMethod.Delete, $"{Ops}/plans/{plan.Id}", manager.Token));

        var chat = (await MineAsync(user.Token)).Features.Single(f => f.Key == SubscriptionFeatures.AiChat);
        chat.Enabled.Should().BeTrue();
        chat.MonthlyLimit.Should().Be(500);
    }

    [Fact]
    public async Task ChangingThePriceMidCheckout_KeepsThePriceTheCustomerSaw()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);

        await _client.SendAsync(Request(HttpMethod.Patch, $"{Ops}/plans/{plan.Id}", manager.Token, new { monthlyPriceCents = 1 }));
        _factory.SePay.MarkPaid(order.InvoiceNumber, MonthlyPrice);
        await SendIpnAsync(order.InvoiceNumber);

        (await OrderAsync(order.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Paid);
    }

    [Fact]
    public async Task Void_RemovesThePeriod_AndMovesLaterOnesUp()
    {
        var manager = await ManagerAsync();
        var (user, plan, first) = await PaidOrderAsync(manager.Token);
        var (_, _, second) = await PaidOrderAsync(manager.Token, user, plan);
        var firstPeriod = (await PeriodsAsync(user.Id))[0];

        (await SendIpnAsync(first.InvoiceNumber, "TRANSACTION_VOID")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await OrderAsync(first.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Voided);
        var secondPeriod = (await PeriodsAsync(user.Id)).Single(p => p.VoidedAt == null);
        secondPeriod.OrderId.Should().Be((await OrderAsync(second.InvoiceNumber)).Id);
        secondPeriod.StartsAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        secondPeriod.StartsAt.Should().BeBefore(firstPeriod.EndsAt);
        (await MineAsync(user.Token)).IsFree.Should().BeFalse("kỳ thứ hai đã dời lên chạy ngay");
    }

    [Fact]
    public async Task VoidForAnOrderNotYetRecorded_GoesToReview_AndReconcileCannotGrantIt()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);

        (await SendIpnAsync(order.InvoiceNumber, "TRANSACTION_VOID")).StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await OrderAsync(order.InvoiceNumber);
        stored.Status.Should().Be(SubscriptionOrderStatus.NeedsReview);
        stored.ReviewReason.Should().Be("void_before_payment");

        _factory.SePay.MarkPaid(order.InvoiceNumber, order.AmountCents);
        await ReconcileAsync(DateTimeOffset.UtcNow.AddMinutes(11));
        (await PeriodsAsync(user.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task VoidOfAnotherAttempt_DoesNotCancelThePaidOrder()
    {
        var manager = await ManagerAsync();
        var (user, _, order) = await PaidOrderAsync(manager.Token);

        var request = Request(HttpMethod.Post, Ipn, payload: new { notification_type = "TRANSACTION_VOID", order = new { order_invoice_number = order.InvoiceNumber }, transaction = new { id = "lan-thu-khac" } });
        request.Headers.Add("X-Secret-Key", SubscriptionApiFactory.IpnKey);
        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await OrderAsync(order.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Paid);
        (await MineAsync(user.Token)).IsFree.Should().BeFalse();
    }

    [Fact]
    public async Task OneOrderSePayAlwaysFailsOn_DoesNotBlockTheOthers()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var poison = await CheckoutAsync(user, plan.Id);
        var healthy = await CheckoutAsync(user, plan.Id);
        _factory.SePay.Failing[poison.InvoiceNumber] = true;
        _factory.SePay.MarkPaid(healthy.InvoiceNumber, healthy.AmountCents);

        await ReconcileAsync(DateTimeOffset.UtcNow.AddMinutes(11));

        (await OrderAsync(poison.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Pending);
        (await OrderAsync(healthy.InvoiceNumber)).Status.Should().Be(SubscriptionOrderStatus.Paid);
    }

    [Fact]
    public async Task AbandonedOrders_AreCheckedLessOftenAsTheyAge()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        int Calls() => _factory.SePay.Calls.GetValueOrDefault(order.InvoiceNumber);
        var start = DateTimeOffset.UtcNow;

        await ReconcileAsync(start.AddMinutes(5));
        Calls().Should().Be(0, "IPN còn đang có cơ hội về");

        await ReconcileAsync(start.AddMinutes(11));
        Calls().Should().Be(1);
        await ReconcileAsync(start.AddMinutes(15));
        Calls().Should().Be(1, "chưa tới lượt");

        await ReconcileAsync(start.AddHours(2));
        Calls().Should().Be(2);
        await ReconcileAsync(start.AddHours(2).AddMinutes(30));
        Calls().Should().Be(2, "đơn quá một giờ chỉ hỏi mỗi giờ");
    }

    [Fact]
    public async Task HardDeletingAUser_KeepsTheOrderWithoutTheUser_AndLatePaymentGoesToReview()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var (user, _, paid) = await PaidOrderAsync(manager.Token, plan: plan);
        var pending = await CheckoutAsync(user, plan.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserHardDeleter>().HardDeleteAsync(user.Id);
        }

        var kept = await OrderAsync(paid.InvoiceNumber);
        kept.UserId.Should().BeNull();
        kept.AmountCents.Should().Be(MonthlyPrice);
        (await PeriodsAsync(user.Id)).Should().BeEmpty();

        _factory.SePay.MarkPaid(pending.InvoiceNumber, pending.AmountCents);
        (await SendIpnAsync(pending.InvoiceNumber)).StatusCode.Should().Be(HttpStatusCode.OK);
        var late = await OrderAsync(pending.InvoiceNumber);
        late.Status.Should().Be(SubscriptionOrderStatus.NeedsReview);
        late.ReviewReason.Should().Be("user_missing");
    }

    // ---------- Nhắc và giám sát ----------

    [Fact]
    public async Task Reminders_FireAt7DaysAnd1Day_OnceEach_AndNotWhenRenewed()
    {
        var manager = await ManagerAsync();
        var (user, plan, _) = await PaidOrderAsync(manager.Token);
        var (renewing, _, _) = await PaidOrderAsync(manager.Token, plan: plan);
        await PaidOrderAsync(manager.Token, renewing, plan);
        var end = (await PeriodsAsync(user.Id))[0].EndsAt;
        var renewedEnd = (await PeriodsAsync(renewing.Id))[0].EndsAt;

        int Count() => _factory.Push.Sent.Count(p => p.UserId == user.Id && p.Action == "subscription_expiring");

        await RemindAsync(end.AddDays(-10));
        Count().Should().Be(0);

        await RemindAsync(end.AddDays(-6));
        Count().Should().Be(1);
        await RemindAsync(end.AddDays(-5));
        Count().Should().Be(1, "không nhắc lại trong cùng mốc");

        await RemindAsync(end.AddHours(-12));
        Count().Should().Be(2);
        await RemindAsync(end.AddHours(-6));
        Count().Should().Be(2);

        await RemindAsync(renewedEnd.AddDays(-3));
        _factory.Push.Sent.Should().NotContain(p => p.UserId == renewing.Id && p.Action == "subscription_expiring");
    }

    [Fact]
    public async Task ReviewOrder_OpensOneIncident_WithNoMoneyOrUserInIt_AndDoesNotReopen()
    {
        var manager = await ManagerAsync();
        var plan = await CreatePlanAsync(manager.Token);
        var user = await RegisterAsync();
        var order = await CheckoutAsync(user, plan.Id);
        _factory.SePay.MarkPaid(order.InvoiceNumber, 1);
        await SendIpnAsync(order.InvoiceNumber);
        var fingerprint = $"subscription_review:{(await OrderAsync(order.InvoiceNumber)).Id}";

        await RunReviewMonitorAsync();
        var incident = await FromDbAsync(db => db.OpsIncidents.AsNoTracking().SingleAsync(i => i.Fingerprint == fingerprint));
        incident.Source.Should().Be(IncidentSource.Payment);
        incident.Title.Should().NotContain("79000");
        incident.TechnicalDetail.Should().NotContain(user.Id.ToString()).And.NotContain("1000");

        await WithDbAsync(db => db.OpsIncidents.Where(i => i.Fingerprint == fingerprint).ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, IncidentStatus.Resolved)));
        await RunReviewMonitorAsync();
        (await FromDbAsync(db => db.OpsIncidents.CountAsync(i => i.Fingerprint == fingerprint))).Should().Be(1);
    }

    private static DateOnly CurrentMonth()
    {
        var (year, month) = FinMate.Application.Common.VietnamTime.YearMonthOf(DateTimeOffset.UtcNow);
        return new DateOnly(year, month, 1);
    }

    private async Task ReconcileAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISubscriptionSettlementService>().ReconcileAsync(now);
    }

    private async Task RemindAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISubscriptionSettlementService>().SendExpiryRemindersAsync(now);
    }

    private async Task RunReviewMonitorAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetServices<IMonitorSource>().OfType<SubscriptionReviewMonitorSource>().Single();
        await source.RunAsync(new FinMate.Application.Admin.SystemConfig.MonitoringSettings(), DateTimeOffset.UtcNow);
    }
}
