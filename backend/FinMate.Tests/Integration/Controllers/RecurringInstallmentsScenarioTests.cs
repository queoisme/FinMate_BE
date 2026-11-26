using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 92 — kịch bản đầu-cuối bắt buộc (a)–(j) của kế hoạch, qua API thật trên Postgres. Job Hangfire được ghi lại thay vì chạy nền, để kiểm
/// rằng xác nhận giao dịch xếp đúng một lượt tracker theo user (S3) và chạy lượt đó ngay trong test, không cần job đêm.
/// Kỳ dự báo tối đa được nâng lên 200 ngày cho riêng factory này, vì kế hoạch đo "cashflow 6 tháng tới" (mặc định 90 ngày).
/// </summary>
[Collection("Integration")]
public class RecurringInstallmentsScenarioTests : IClassFixture<AuthApiFactory>
{
    private const long Installment = 1_200_000;
    private const string Lender = "FE Credit";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RecordingJobClient _jobs = new();
    private readonly WebApplicationFactory<FinMate.API.Program> _factory;
    private readonly HttpClient _client;

    public RecurringInstallmentsScenarioTests(AuthApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IBackgroundJobClient>();
            s.AddSingleton<IBackgroundJobClient>(_jobs);
        }));
        _client = _factory.CreateClient();
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record IdData(Guid Id);
    private record ItemData(Guid Id, string Status, DateOnly NextDueDate, long AmountCents, int PaidOccurrences, int? RemainingOccurrences,
        long? RemainingAmountCents, bool SettledEarly);
    private record PaymentData(Guid Id, Guid? TransactionId);
    private record EventData(string Label, long AmountCents);
    private record DayData(DateOnly Date, List<EventData> Events);
    private record CashflowData(List<DayData> Days);
    private record LineData(DateOnly Date, string Label, long AmountCents);
    private record SpendableData(DateOnly PeriodEnd, long UpcomingBillsCents, List<LineData> Bills);
    private record AlertData(string Kind, string Title, JsonElement Data);

    private static DateOnly Today => VietnamTime.Today();
    private static DateOnly SixMonths => Today.AddMonths(6);

    // ---------- hạ tầng ----------

    private async Task<(string Token, Guid UserId, Guid Wallet)> NewUserAsync()
    {
        await EnsureLongHorizonAsync();
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Scenario Test"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        var token = (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
        var userId = (await SendAsync<IdData>(HttpMethod.Get, "/api/v1/users/me", token)).Id;
        var wallet = (await SendAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token,
            new { accountName = "VCB", accountType = "Cash", initialBalanceCents = 100_000_000 }, HttpStatusCode.Created)).Id;
        return (token, userId, wallet);
    }

    private async Task EnsureLongHorizonAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
        var current = await config.GetActiveAsync();
        if (current.Content.Forecast.MaxHorizonDays >= 200)
        {
            return;
        }

        var adminId = await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().Users
            .Where(u => u.RoleAssignment!.RoleId == UserRole.Admin).Select(u => u.Id).FirstAsync();
        await config.SaveAsync(adminId, null, current.Version,
            current.Content with { Forecast = current.Content.Forecast with { MaxHorizonDays = 200 } }, "Phase 92: cashflow 6 tháng");
        scope.ServiceProvider.GetRequiredService<ISystemConfigProvider>().Invalidate();
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, string token, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await SendRawAsync(method, url, token, body);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Wrapper<T>>(JsonOptions))!.Data;
    }

    private Task<ItemData> CreateAsync(string token, DateOnly due, int? total = 12, int prepaid = 9, string name = "Trả góp điện thoại",
        string counterparty = Lender, long amount = Installment, int? anchor = null)
        => SendAsync<ItemData>(HttpMethod.Post, "/api/v1/recurring-items", token, new
        {
            direction = "Outflow", name, counterpartyName = counterparty, amountCents = amount, cadence = "Monthly", nextDueDate = due,
            totalOccurrences = total, prepaidOccurrences = total is null ? (int?)null : prepaid, anchorDay = anchor,
        }, HttpStatusCode.Created);

    private async Task<Guid> PayAsync(string token, Guid wallet, DateOnly on, long amount = Installment, string merchant = Lender)
        => (await SendAsync<IdData>(HttpMethod.Post, "/api/v1/transactions", token, new
        {
            financialAccountId = wallet, amountCents = amount, transactionType = "Debit", merchantName = merchant,
            transactedAt = new DateTimeOffset(on.ToDateTime(new TimeOnly(10, 0)), VietnamTime.Offset),
        }, HttpStatusCode.Created)).Id;

    private async Task<ItemData> ItemAsync(string token, Guid id)
    {
        var all = new List<ItemData>();
        foreach (var status in new[] { "Active", "Completed", "Paused" })
        {
            all.AddRange(await SendAsync<List<ItemData>>(HttpMethod.Get, $"/api/v1/recurring-items?status={status}", token));
        }

        return all.Single(i => i.Id == id);
    }

    private async Task<List<(DateOnly Date, long Amount)>> CashflowEventsAsync(string token, string label, DateOnly? to = null)
        => (await SendAsync<CashflowData>(HttpMethod.Get, $"/api/v1/forecast/cashflow?to={(to ?? SixMonths):yyyy-MM-dd}", token)).Days
            .SelectMany(d => d.Events.Where(e => e.Label == label).Select(e => (d.Date, e.AmountCents)))
            .ToList();

    /// <summary>S3: chạy đúng các lượt tracker đã được xếp hàng cho user (như Hangfire sẽ chạy), rồi xoá bản ghi.</summary>
    private async Task RunQueuedTrackingAsync(Guid userId, int expected = 1)
    {
        var queued = _jobs.TakeTracking(userId);
        queued.Should().Be(expected, "mỗi đợt giao dịch chỉ xếp một lượt tracker cho user (chống trùng bằng dấu theo user)");
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RecurringTrackingJob>().RunAsync(userId);
    }

    private async Task<List<AlertData>> RunDailyAlertsAsync(string token, Guid userId)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IForecastAlertService>().RunDailyAsync(userId);
        }

        return await SendAsync<List<AlertData>>(HttpMethod.Get, "/api/v1/forecast/alerts?limit=50", token);
    }

    private static bool IsAbout(AlertData alert, Guid itemId)
        => alert.Data.TryGetProperty("recurringItemId", out var id) && id.GetString() == itemId.ToString();

    private static DateOnly NextDay(int day)
    {
        var candidate = new DateOnly(Today.Year, Today.Month, Math.Min(day, DateTime.DaysInMonth(Today.Year, Today.Month)));
        return candidate >= Today ? candidate : candidate.AddMonths(1);
    }

    // ---------- kịch bản ----------

    [Fact]
    public async Task A_TwelveInstallmentsNinePrepaid_OnlyThreeLeftInSixMonths()
    {
        var (token, _, _) = await NewUserAsync();
        var due = NextDay(15);

        var item = await CreateAsync(token, due, anchor: 15);

        item.RemainingAmountCents.Should().Be(3_600_000);
        var events = await CashflowEventsAsync(token, "Trả góp điện thoại");
        events.Should().Equal((due, -Installment), (due.AddMonths(1), -Installment), (due.AddMonths(2), -Installment));

        var spendable = await SendAsync<SpendableData>(HttpMethod.Get, "/api/v1/forecast/spendable", token);
        var inPeriod = new[] { due, due.AddMonths(1), due.AddMonths(2) }.Count(d => d <= spendable.PeriodEnd);
        spendable.Bills.Count(b => b.Label == "Trả góp điện thoại").Should().Be(inPeriod);
        spendable.UpcomingBillsCents.Should().Be(inPeriod * Installment);
    }

    [Fact]
    public async Task B_And_E_ThreeNotificationsComplete_ThenDeletingTheLastReopens()
    {
        var (token, userId, wallet) = await NewUserAsync();
        var k10 = Today.AddDays(-70);
        var item = await CreateAsync(token, k10, anchor: k10.Day);
        var payments = new List<Guid>();
        foreach (var due in new[] { k10, k10.AddMonths(1), k10.AddMonths(2) })
        {
            payments.Add(await PayAsync(token, wallet, due));
        }

        await RunQueuedTrackingAsync(userId);   // S3: không chờ job 01:30

        var done = await ItemAsync(token, item.Id);
        done.Status.Should().Be("completed");
        done.PaidOccurrences.Should().Be(12);
        done.RemainingOccurrences.Should().Be(0);
        (await CashflowEventsAsync(token, "Trả góp điện thoại")).Should().BeEmpty();
        (await RunDailyAlertsAsync(token, userId)).Where(a => IsAbout(a, item.Id)).Should().BeEmpty("không còn nhắc hạn");

        // (e) xoá giao dịch kỳ 12
        (await SendRawAsync(HttpMethod.Delete, $"/api/v1/transactions/{payments[^1]}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var reopened = await ItemAsync(token, item.Id);
        reopened.Status.Should().Be("active");
        reopened.PaidOccurrences.Should().Be(11);
        (await CashflowEventsAsync(token, "Trả góp điện thoại")).Should().ContainSingle("dự báo có lại một kỳ (quá hạn → ngày mai, S1)")
            .Which.Should().Be((Today.AddDays(1), -Installment));
    }

    [Fact]
    public async Task C_SubscriptionWithoutAnEnd_SixInSixMonths_AndThePriceStillUpdates()
    {
        var (token, userId, wallet) = await NewUserAsync();
        await CreateAsync(token, Today.AddDays(5), total: null, name: "Netflix", counterparty: "Netflix", amount: 260_000);
        (await CashflowEventsAsync(token, "Netflix")).Should().HaveCount(6).And.OnlyContain(e => e.Amount == -260_000);

        // Thuê bao tự phát hiện (luật đổi giá cũ: khoản phát hiện theo số tiền thật mới nhất), rồi trả giá mới đúng kỳ.
        foreach (var daysAgo in new[] { 88, 58, 28 })
        {
            await PayAsync(token, wallet, Today.AddDays(-daysAgo), 260_000, "Spotify");
        }

        await RunQueuedTrackingAsync(userId);   // user đã có Netflix nên ba khoản chi trên xếp đúng một lượt; chạy nó để gỡ dấu chống trùng

        using (var scope = _factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IRecurringDetectionService>().RunForUserAsync(userId)).Should().Be(1);
        }

        var spotify = (await SendAsync<List<ItemData>>(HttpMethod.Get, "/api/v1/recurring-items", token)).Single(i => i.NextDueDate != Today.AddDays(5));
        await PayAsync(token, wallet, Today, 280_000, "Spotify");
        await RunQueuedTrackingAsync(userId);

        var updated = await ItemAsync(token, spotify.Id);
        updated.AmountCents.Should().Be(280_000, "đổi giá vẫn tự cập nhật như cũ");
        var spotifyEvents = (await SendAsync<CashflowData>(HttpMethod.Get, $"/api/v1/forecast/cashflow?to={updated.NextDueDate.AddMonths(5):yyyy-MM-dd}", token)).Days
            .SelectMany(d => d.Events.Where(e => e.AmountCents == -280_000)).ToList();
        spotifyEvents.Should().HaveCount(6);
    }

    [Fact]
    public async Task D_DueOnThe31st_FollowsTheAnchorThroughShortMonths()
    {
        var (token, _, _) = await NewUserAsync();
        var first = Enumerable.Range(0, 70).Select(Today.AddDays).First(d => d.Day == 31);
        static DateOnly Clamp(DateOnly month) => new(month.Year, month.Month, Math.Min(31, DateTime.DaysInMonth(month.Year, month.Month)));
        var expected = Enumerable.Range(0, 4).Select(i => Clamp(new DateOnly(first.Year, first.Month, 1).AddMonths(i))).ToList();

        await CreateAsync(token, first, total: 4, prepaid: 0, name: "Ngày 31", counterparty: "Ngay 31");

        (await CashflowEventsAsync(token, "Ngày 31", expected[^1])).Select(e => e.Date).Should().Equal(expected);
    }

    [Fact]
    public async Task F_CashInstallmentMarkedByHand_MovesToNextMonth()
    {
        var (token, _, _) = await NewUserAsync();
        var item = await CreateAsync(token, NextDay(15), prepaid: 10);

        var result = await SendAsync<JsonElement>(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/payments", token, new { }, HttpStatusCode.Created);

        var after = await ItemAsync(token, item.Id);
        after.PaidOccurrences.Should().Be(11);
        after.NextDueDate.Should().Be(item.NextDueDate.AddMonths(1));
        result.GetProperty("payment").GetProperty("transactionId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task G_TwoIdenticalPaymentsInOnePeriod_OnlyOneCounts()
    {
        var (token, userId, wallet) = await NewUserAsync();
        var due = Today.AddDays(-5);
        var item = await CreateAsync(token, due, prepaid: 0, anchor: due.Day);
        await PayAsync(token, wallet, due.AddDays(-1));
        await PayAsync(token, wallet, due);

        await RunQueuedTrackingAsync(userId);

        (await ItemAsync(token, item.Id)).PaidOccurrences.Should().Be(1);
        (await SendAsync<List<PaymentData>>(HttpMethod.Get, $"/api/v1/recurring-items/{item.Id}/payments", token)).Should().ContainSingle();
    }

    [Fact]
    public async Task H_SettlingSixOfTwelve_RemovesItFromTheForecast()
    {
        var (token, _, _) = await NewUserAsync();
        var item = await CreateAsync(token, NextDay(15), prepaid: 6);

        var settled = await SendAsync<ItemData>(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/settle", token, new { });

        settled.Status.Should().Be("completed");
        settled.SettledEarly.Should().BeTrue();
        (await CashflowEventsAsync(token, "Trả góp điện thoại")).Should().BeEmpty();
    }

    [Fact]
    public async Task I_AfterCompletion_ANewInstallmentWithTheSameLender_Is201()
    {
        var (token, userId, wallet) = await NewUserAsync();
        var due = Today.AddDays(-10);
        var item = await CreateAsync(token, due, total: 12, prepaid: 11, anchor: due.Day);
        await PayAsync(token, wallet, due);
        await RunQueuedTrackingAsync(userId);
        (await ItemAsync(token, item.Id)).Status.Should().Be("completed");

        var next = await CreateAsync(token, NextDay(20), total: 6, prepaid: 0, name: "Trả góp laptop");

        next.Id.Should().NotBe(item.Id);
        next.Status.Should().Be("active");
    }

    [Fact]
    public async Task J_ThreeLeft_FirstOverdue20Days_AllThreeStay_NotPaused()
    {
        var (token, userId, _) = await NewUserAsync();
        var due = Today.AddDays(-20);
        var item = await CreateAsync(token, due, anchor: due.Day);

        (await CashflowEventsAsync(token, "Trả góp điện thoại")).Should().Equal(
            (Today.AddDays(1), -Installment), (due.AddMonths(1), -Installment), (due.AddMonths(2), -Installment));

        var alerts = (await RunDailyAlertsAsync(token, userId)).Where(a => IsAbout(a, item.Id)).ToList();
        (await ItemAsync(token, item.Id)).Status.Should().Be("active", "S1: không vào luồng tự tạm dừng");
        alerts.Should().ContainSingle().Which.Title.Should().StartWith("Quá hạn");
        alerts.Should().NotContain(a => a.Kind == "bill_status_check");
    }

    /// <summary>Ghi lại job được xếp hàng thay vì chạy nền.</summary>
    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        private readonly List<Job> _jobs = [];
        private readonly Lock _lock = new();

        public string Create(Job job, IState state)
        {
            lock (_lock)
            {
                _jobs.Add(job);
                return _jobs.Count.ToString();
            }
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;

        public int TakeTracking(Guid userId)
        {
            lock (_lock)
            {
                var mine = _jobs.Where(j => j.Type == typeof(RecurringTrackingJob) && j.Args.Count > 0 && Equals(j.Args[0], userId)).ToList();
                _jobs.RemoveAll(mine.Contains);
                return mine.Count;
            }
        }
    }
}
