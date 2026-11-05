using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Tắt cờ thì không ai vào được tính năng nhóm, nhưng tiền cá nhân vẫn chạy và số liệu nhóm không bị lệch.</summary>
[Collection("Integration")]
public class GroupsFeatureFlagTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Disabled = "GROUPS_DISABLED";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GroupsFeatureFlagTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Customer(Guid Id, string Token, Guid WalletId);

    public Task InitializeAsync() => _factory.SetGroupsEnabledAsync(false);

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    private static async Task ExpectDisabledAsync(HttpResponseMessage response, string because)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, because);
        var code = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString();
        code.Should().Be(Disabled, because);
    }

    private async Task<Customer> CustomerAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Khách"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        var token = (await DataAsync(login)).GetProperty("accessToken").GetString()!;
        var id = (await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/users/me", token))).GetProperty("id").GetGuid();
        var wallet = await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/financial-accounts", token, new { accountType = "Cash", accountName = "Ví", initialBalanceCents = 10_000_000L }));
        return new Customer(id, token, wallet.GetProperty("id").GetGuid());
    }

    private async Task<Guid> SpendAsync(Customer customer, long amount, Guid? groupId = null)
        => (await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/transactions", customer.Token, new
        {
            financialAccountId = customer.WalletId,
            amountCents = amount,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            groupId,
        }))).GetProperty("id").GetGuid();

    private async Task EditAmountAsync(Customer customer, Guid transactionId, long amount)
        => await DataAsync(await SendAsync(HttpMethod.Patch, $"/api/v1/transactions/{transactionId}", customer.Token, new
        {
            financialAccountId = customer.WalletId,
            amountCents = amount,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        }));

    private async Task<(Guid GroupId, Guid BudgetId)> GroupWithBudgetAsync(Customer owner, long limit = 100_000_000L)
    {
        await _factory.SetGroupsEnabledAsync(true);
        var groupId = (await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/groups", owner.Token, new { name = "Gia đình" }))).GetProperty("id").GetGuid();
        var budgetId = (await DataAsync(await SendAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { limitCents = limit }))).GetProperty("id").GetGuid();
        return (groupId, budgetId);
    }

    private async Task<long> SpentAsync(Guid budgetId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        return await db.BudgetPeriods.AsNoTracking().Where(p => p.BudgetId == budgetId).SumAsync(p => p.SpentCents);
    }

    [Fact]
    public async Task EveryGroupEntryPointIsClosedWhileTheFlagIsOff()
    {
        var customer = await CustomerAsync();
        var anyId = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, "/api/v1/groups", null),
            (HttpMethod.Post, "/api/v1/groups", new { name = "Nhóm" }),
            (HttpMethod.Get, $"/api/v1/groups/{anyId}", null),
            (HttpMethod.Get, $"/api/v1/groups/{anyId}/members", null),
            (HttpMethod.Post, $"/api/v1/groups/{anyId}/leave", null),
            (HttpMethod.Post, $"/api/v1/groups/{anyId}/invitations", new { role = "Member" }),
            (HttpMethod.Get, $"/api/v1/groups/{anyId}/transactions", null),
            (HttpMethod.Get, $"/api/v1/groups/{anyId}/budgets", null),
            (HttpMethod.Get, $"/api/v1/groups/{anyId}/goals", null),
            (HttpMethod.Get, $"/api/v1/groups/{anyId}/categories", null),
            (HttpMethod.Get, $"/api/v1/groups/{anyId}/share-rules", null),
            (HttpMethod.Get, $"/api/v1/groups/{anyId}/reports/monthly?year=2026&month=11", null),
            (HttpMethod.Post, "/api/v1/group-invitations/preview", new { token = "x" }),
            (HttpMethod.Post, "/api/v1/group-invitations/accept", new { token = "x" }),
            (HttpMethod.Put, $"/api/v1/transactions/{anyId}/group", new { groupId = anyId }),
            (HttpMethod.Delete, $"/api/v1/transactions/{anyId}/group", null),
        };

        foreach (var (method, url, body) in calls)
        {
            await ExpectDisabledAsync(await SendAsync(method, url, customer.Token, body), $"{method} {url}");
        }
    }

    [Fact]
    public async Task AnonymousCallersStillGet401()
        => (await _client.GetAsync("/api/v1/groups")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task PersonalTransactionsKeepWorking_ButAskingForAGroupIsRefused()
    {
        var customer = await CustomerAsync();

        await SpendAsync(customer, 10_000);

        await ExpectDisabledAsync(
            await SendAsync(HttpMethod.Post, "/api/v1/transactions", customer.Token, new
            {
                financialAccountId = customer.WalletId,
                amountCents = 10_000L,
                transactionType = "Debit",
                transactedAt = DateTimeOffset.UtcNow,
                groupId = Guid.NewGuid(),
            }),
            "chủ động chia sẻ vào nhóm");
    }

    [Fact]
    public async Task AutoShareRulesAreSkippedWhileOff_AndTheTransactionStaysPersonal()
    {
        var customer = await CustomerAsync();
        var (groupId, budgetId) = await GroupWithBudgetAsync(customer);
        await DataAsync(await SendAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/share-rules", customer.Token, new { financialAccountId = customer.WalletId }));

        await SpendAsync(customer, 20_000);
        (await SpentAsync(budgetId)).Should().Be(20_000, "cờ bật thì quy tắc tự chia sẻ chạy");

        await _factory.SetGroupsEnabledAsync(false);
        var personal = await SpendAsync(customer, 7_000);

        (await SpentAsync(budgetId)).Should().Be(20_000);
        using var scope = _factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().Transactions.AsNoTracking().SingleAsync(t => t.Id == personal);
        row.GroupId.Should().BeNull();
    }

    [Fact]
    public async Task ChangingASharedTransactionWhileOffKeepsTheGroupNumbersCorrect()
    {
        var customer = await CustomerAsync();
        var (groupId, budgetId) = await GroupWithBudgetAsync(customer);
        var edited = await SpendAsync(customer, 30_000, groupId);
        var deleted = await SpendAsync(customer, 5_000, groupId);
        (await SpentAsync(budgetId)).Should().Be(35_000);

        await _factory.SetGroupsEnabledAsync(false);

        await EditAmountAsync(customer, edited, 40_000);
        (await SpentAsync(budgetId)).Should().Be(45_000);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/transactions/{deleted}", customer.Token)).IsSuccessStatusCode.Should().BeTrue();
        (await SpentAsync(budgetId)).Should().Be(40_000);
    }

    [Fact]
    public async Task DataIsStillThereWhenTheFlagIsTurnedBackOn()
    {
        var customer = await CustomerAsync();
        var (groupId, _) = await GroupWithBudgetAsync(customer);

        await _factory.SetGroupsEnabledAsync(false);
        await ExpectDisabledAsync(await SendAsync(HttpMethod.Get, $"/api/v1/groups/{groupId}", customer.Token), "đang tắt");

        await _factory.SetGroupsEnabledAsync(true);
        (await SendAsync(HttpMethod.Get, $"/api/v1/groups/{groupId}", customer.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AlertsWaitWhileOff_SoNoThresholdIsLostWhenTheFlagReturns()
    {
        var customer = await CustomerAsync();
        var (groupId, budgetId) = await GroupWithBudgetAsync(customer, limit: 100_000);
        var shared = await SpendAsync(customer, 10_000, groupId);

        await _factory.SetGroupsEnabledAsync(false);

        // Sửa giao dịch làm vượt ngưỡng, rồi job chạy khi đang tắt: cả hai đều không được đóng ngưỡng.
        await EditAmountAsync(customer, shared, 95_000);
        await RunBudgetJobAsync();
        (await PeriodAsync(budgetId)).AlertT2SentAt.Should().BeNull();

        await _factory.SetGroupsEnabledAsync(true);
        await RunBudgetJobAsync();
        (await PeriodAsync(budgetId)).AlertT2SentAt.Should().NotBeNull();
    }

    private async Task RunBudgetJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BudgetAlertJob>().RunAsync();
    }

    private async Task<BudgetPeriod> PeriodAsync(Guid budgetId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().BudgetPeriods.AsNoTracking().SingleAsync(p => p.BudgetId == budgetId);
    }

    [Fact]
    public async Task OverdueGroupGoalsAreNotMarkedAsNotifiedWhileOff()
    {
        var customer = await CustomerAsync();
        var (groupId, _) = await GroupWithBudgetAsync(customer);
        var goalId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            db.SavingGoals.Add(new SavingGoal { Id = goalId, GroupId = groupId, CreatedByUserId = customer.Id, Name = "Du lịch", TargetCents = 1_000_000, Deadline = now.AddDays(-1), CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        async Task RunAsync()
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<GoalDeadlineCheckJob>().RunAsync();
        }

        async Task<DateTimeOffset?> MarkAsync()
        {
            using var scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().SavingGoals.AsNoTracking().Where(g => g.Id == goalId).Select(g => g.DeadlineNotifiedAt).SingleAsync();
        }

        await _factory.SetGroupsEnabledAsync(false);
        await RunAsync();
        (await MarkAsync()).Should().BeNull();

        await _factory.SetGroupsEnabledAsync(true);
        await RunAsync();
        (await MarkAsync()).Should().NotBeNull();
    }
}
