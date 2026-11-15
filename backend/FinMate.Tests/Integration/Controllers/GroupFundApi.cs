using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.API.Controllers;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Gọi API quỹ nhóm như app thật (Phase 85): người dùng, nhóm, thành viên theo vai trò, quỹ, dòng quỹ.</summary>
internal sealed class GroupFundApi
{
    public const long InitialBalance = 5_000_000L;

    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly WebApplicationFactory<FinMate.API.Program> _factory;
    private readonly HttpClient _client;

    public GroupFundApi(WebApplicationFactory<FinMate.API.Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public sealed record User(Guid Id, string Token, Guid AccountId, string Name);
    public sealed record Wrapper<T>(bool Success, T Data);
    public sealed record ErrorBody(bool Success, ErrorData Error);
    public sealed record ErrorData(string Code);
    public sealed record IdData(Guid Id);
    public sealed record AuthData(string AccessToken);
    public sealed record InvitationData(string? Token);
    public sealed record CategoryData(Guid Id, string Slug, bool IsSystem);
    public sealed record FundData(Guid Id, string Name, string? Note, long BalanceCents, bool IsNegative, bool IsArchived, DateTimeOffset? ArchivedAt);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public async Task<User> NewUserAsync(string? name = null)
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        var display = name ?? "Người " + email[..4];
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", display));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        var token = (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
        var me = await ReadAsync<IdData>(HttpMethod.Get, "/api/v1/users/me", token);
        var account = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token, new { accountType = "Cash", accountName = "VCB", initialBalanceCents = InitialBalance });
        return new User(me.Id, token, account.Id, display);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return _client.SendAsync(request);
    }

    public async Task<T> ReadAsync<T>(HttpMethod method, string url, string token, object? body = null)
    {
        var response = await SendAsync(method, url, token, body);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Wrapper<T>>(JsonOptions))!.Data;
    }

    public async Task<HttpResponseMessage> ExpectAsync(HttpMethod method, string url, string token, HttpStatusCode status, object? body = null, string? code = null)
    {
        var response = await SendAsync(method, url, token, body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, text);
        if (code is not null)
        {
            JsonSerializer.Deserialize<ErrorBody>(text, JsonOptions)!.Error.Code.Should().Be(code);
        }

        return response;
    }

    public async Task<(User Owner, Guid GroupId)> NewGroupAsync(string name = "Phòng trọ", string? ownerName = null)
    {
        var owner = await NewUserAsync(ownerName);
        var group = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/groups", owner.Token, new { name });
        return (owner, group.Id);
    }

    public async Task<User> JoinAsync(User owner, Guid groupId, string role = "Member", string? name = null)
    {
        var user = await NewUserAsync(name);
        var invite = await ReadAsync<InvitationData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/invitations", owner.Token, new { role });
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", user.Token, new { token = invite.Token });
        return user;
    }

    public async Task<Guid> SystemCategoryAsync(string token, string slug)
        => (await ReadAsync<List<CategoryData>>(HttpMethod.Get, "/api/v1/categories", token)).First(c => c.IsSystem && c.Slug == slug).Id;

    public static string Funds(Guid groupId) => $"/api/v1/groups/{groupId}/funds";

    public Task<FundData> CreateFundAsync(User manager, Guid groupId, string name = "Tiền điện nước", string? note = null)
        => ReadAsync<FundData>(HttpMethod.Post, Funds(groupId), manager.Token, new { name, note });

    public Task<FundData> GetFundAsync(User viewer, Guid groupId, Guid fundId)
        => ReadAsync<FundData>(HttpMethod.Get, $"{Funds(groupId)}/{fundId}", viewer.Token);

    public async Task<T> WithDbAsync<T>(Func<FinMateDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    public Task<long> BalanceAsync(Guid accountId)
        => WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == accountId).Select(a => a.BalanceCents).SingleAsync());

    public sealed record EntryData(
        Guid Id, Guid FundId, string Kind, long AmountCents, DateTimeOffset TransactedAt, Guid? MemberUserId, string MemberName, Guid? CategoryId,
        Guid? GroupCategoryId, string? CategoryName, string? Note, string? MerchantName, string Origin, bool? HasPersonalLink);

    public sealed record EntryResult(EntryData Entry, FundData Fund, bool AlreadyExisted);

    public sealed record MonthlySummary(long TotalSpentCents, long TotalIncomeCents);

    public static string Entries(Guid groupId, Guid fundId) => $"{Funds(groupId)}/{fundId}/entries";

    public Task<EntryResult> RecordAsync(User user, Guid groupId, Guid fundId, object body)
        => ReadAsync<EntryResult>(HttpMethod.Post, Entries(groupId, fundId), user.Token, body);

    public Task<EntryResult> ContributeAsync(User user, Guid groupId, Guid fundId, long amount, bool fromWallet = true)
        => RecordAsync(user, groupId, fundId, new { kind = "Contribution", amountCents = amount, financialAccountId = fromWallet ? user.AccountId : (Guid?)null });

    public Task<EntryResult> SpendAsync(User user, Guid groupId, Guid fundId, long amount, Guid categoryId, Guid? accountId = null, string merchant = "EVN")
        => RecordAsync(user, groupId, fundId, new { kind = "Expense", amountCents = amount, categoryId, merchantName = merchant, financialAccountId = accountId });

    public Task<MonthlySummary> MonthlySummaryAsync(User user)
        => ReadAsync<MonthlySummary>(HttpMethod.Get, "/api/v1/reports/monthly-summary", user.Token);

    /// <summary>Giao dịch cá nhân (chưa xoá) gắn với một dòng quỹ, đọc thẳng DB.</summary>
    public Task<FinMate.Domain.Entities.Transaction?> PersonalLinkAsync(Guid entryId)
        => WithDbAsync(db => db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.GroupFundEntryId == entryId));
}
