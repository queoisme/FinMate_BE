using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Duyệt hóa đơn và chi quỹ từ ngưỡng do người không phải Owner tạo (P1-G).</summary>
[Collection("Integration")]
public class GroupApprovalsControllerTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GroupApprovalsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(new CapturingEmailSender());
        })).CreateClient();
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record MeData(Guid Id);
    private record IdData(Guid Id);
    private record InviteData(string? Token);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);
    private record TestUser(Guid Id, string Token);
    private record BillData(Guid Id, string Title, long AmountCents);
    private record ApprovalData(Guid Id, string Kind, string Status, long AmountCents, string Title, Guid? RequestedByUserId, string? DecisionReason, Guid? ResultId);
    private record BudgetData(Guid Id, long SpentCents);
    private record FundData(Guid Id, long BalanceCents);
    private record EntryResult(IdData Entry, FundData Fund);

    private async Task<TestUser> NewUserAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Approval Tester"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        var token = (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
        var me = await ReadAsync<MeData>(HttpMethod.Get, "/api/v1/users/me", token);
        return new TestUser(me.Id, token);
    }

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

    private async Task<T> ReadAsync<T>(HttpMethod method, string url, string token, object? body = null, HttpStatusCode? expect = null)
    {
        var response = await SendAsync(method, url, token, body);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        if (expect is { } status)
        {
            response.StatusCode.Should().Be(status);
        }

        return (await response.Content.ReadFromJsonAsync<Wrapper<T>>(JsonOptions))!.Data;
    }

    private async Task ExpectAsync(HttpMethod method, string url, string token, HttpStatusCode status, object? body = null, string? code = null)
    {
        var response = await SendAsync(method, url, token, body);
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        if (code is not null)
        {
            (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be(code);
        }
    }

    private async Task<(TestUser Owner, TestUser B, TestUser C, Guid GroupId)> NewGroupAsync(long? threshold)
    {
        var owner = await NewUserAsync();
        var group = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/groups", owner.Token, new { name = "Đi chơi", type = "Friends" });
        var b = await NewUserAsync();
        var c = await NewUserAsync();
        foreach (var u in new[] { b, c })
        {
            var invite = await ReadAsync<InviteData>(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", owner.Token, new { role = "Member" });
            await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", u.Token, new { token = invite.Token });
        }

        await ReadAsync<object>(HttpMethod.Put, $"/api/v1/groups/{group.Id}/settings", owner.Token, new
        {
            debtSettlementMethod = "AiOptimized",
            requireApprovalAboveCents = threshold,
            weeklySummaryDay = 1,
        });
        return (owner, b, c, group.Id);
    }

    private static object Bill(string title, long amount, Guid paidBy, params Guid[] split)
        => new { title, amountCents = amount, paidByUserId = paidBy, splitMode = "Equal", splits = split.Select(s => new { userId = s, shareCents = 0L }).ToArray() };

    [Fact]
    public async Task BillsFromTheThresholdWaitForTheOwnerAndOnlyCountAfterApproval()
    {
        var (owner, b, c, groupId) = await NewGroupAsync(5_000_000);
        var budget = await ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { limitCents = 100_000_000L });

        var pending = await ReadAsync<ApprovalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/bills", b.Token, Bill("Khách sạn", 5_000_000, b.Id, owner.Id, b.Id), HttpStatusCode.Accepted);
        pending.Status.Should().Be("Pending");
        pending.Kind.Should().Be("Bill");
        await ReadAsync<BillData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/bills", b.Token, Bill("Cà phê", 4_999_999, b.Id, owner.Id, b.Id), HttpStatusCode.Created);
        await ReadAsync<BillData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/bills", owner.Token, Bill("Chủ nhóm chi lớn", 9_000_000, owner.Id, owner.Id, b.Id), HttpStatusCode.Created);

        // Khoản chờ chưa tính vào đâu cả.
        (await ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", owner.Token)).Single(x => x.Id == budget.Id).SpentCents.Should().Be(13_999_999);
        (await ReadAsync<List<BillData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/bills", owner.Token)).Should().NotContain(x => x.Title == "Khách sạn");

        (await ReadAsync<List<ApprovalData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/approvals?status=Pending", owner.Token)).Should().ContainSingle(a => a.Id == pending.Id);
        (await ReadAsync<List<ApprovalData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/approvals", b.Token)).Should().ContainSingle(a => a.Id == pending.Id);
        (await ReadAsync<List<ApprovalData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/approvals", c.Token)).Should().BeEmpty();
        await ExpectAsync(HttpMethod.Get, $"/api/v1/groups/{groupId}/approvals?status=Nope", owner.Token, HttpStatusCode.BadRequest);

        // Chỉ Owner duyệt: kể cả người tạo cũng không.
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/approvals/{pending.Id}/approve", b.Token, HttpStatusCode.Forbidden);
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/approvals/{pending.Id}/reject", c.Token, HttpStatusCode.Forbidden);

        var approved = await ReadAsync<ApprovalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/approvals/{pending.Id}/approve", owner.Token);
        approved.Status.Should().Be("Approved");
        approved.ResultId.Should().NotBeNull();
        (await ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", owner.Token)).Single(x => x.Id == budget.Id).SpentCents.Should().Be(18_999_999);
        (await ReadAsync<BillData>(HttpMethod.Get, $"/api/v1/groups/{groupId}/bills/{approved.ResultId}", owner.Token)).Title.Should().Be("Khách sạn");
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/approvals/{pending.Id}/approve", owner.Token, (HttpStatusCode)422, code: "GROUP_APPROVAL_NOT_PENDING");
        (await ReadAsync<List<ApprovalData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/approvals", owner.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task FundExpensesWaitUntilApprovedAndRejectionChangesNothing()
    {
        var (owner, b, _, groupId) = await NewGroupAsync(5_000_000);
        var fund = await ReadAsync<FundData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/funds", owner.Token, new { name = "Quỹ chuyến đi" });
        var category = await ReadAsync<IdData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/categories", owner.Token, new { name = "Ăn uống" });
        var entries = $"/api/v1/groups/{groupId}/funds/{fund.Id}/entries";
        await ReadAsync<EntryResult>(HttpMethod.Post, entries, b.Token, new { kind = "Contribution", amountCents = 10_000_000L });

        var requestId = Guid.NewGuid();
        var expense = new { kind = "Expense", amountCents = 6_000_000L, groupCategoryId = category.Id, merchantName = "Nhà hàng", clientRequestId = requestId };
        var pending = await ReadAsync<ApprovalData>(HttpMethod.Post, entries, b.Token, expense, HttpStatusCode.Accepted);
        pending.Kind.Should().Be("FundEntry");
        pending.Title.Should().Be("Nhà hàng");
        (await ReadAsync<ApprovalData>(HttpMethod.Post, entries, b.Token, expense, HttpStatusCode.Accepted)).Id.Should().Be(pending.Id);   // gửi lại không tạo thêm
        (await ReadAsync<FundData>(HttpMethod.Get, $"/api/v1/groups/{groupId}/funds/{fund.Id}", owner.Token)).BalanceCents.Should().Be(10_000_000);

        var rejected = await ReadAsync<ApprovalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/approvals/{pending.Id}/reject", owner.Token, new { reason = "Quá đắt" });
        rejected.Status.Should().Be("Rejected");
        rejected.DecisionReason.Should().Be("Quá đắt");
        (await ReadAsync<FundData>(HttpMethod.Get, $"/api/v1/groups/{groupId}/funds/{fund.Id}", owner.Token)).BalanceCents.Should().Be(10_000_000);
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/approvals/{pending.Id}/reject", owner.Token, (HttpStatusCode)422, code: "GROUP_APPROVAL_NOT_PENDING");

        // Rút từ ngưỡng cũng chờ; chi dưới ngưỡng và Owner chi lớn thì đi thẳng.
        var second = await ReadAsync<ApprovalData>(HttpMethod.Post, entries, b.Token, new { kind = "Withdrawal", amountCents = 7_000_000L }, HttpStatusCode.Accepted);
        await ReadAsync<EntryResult>(HttpMethod.Post, entries, b.Token, new { kind = "Expense", amountCents = 1_000_000L, groupCategoryId = category.Id }, HttpStatusCode.Created);
        await ReadAsync<EntryResult>(HttpMethod.Post, entries, owner.Token, new { kind = "Expense", amountCents = 6_000_000L, groupCategoryId = category.Id }, HttpStatusCode.Created);
        (await ReadAsync<FundData>(HttpMethod.Get, $"/api/v1/groups/{groupId}/funds/{fund.Id}", owner.Token)).BalanceCents.Should().Be(3_000_000);

        var approved = await ReadAsync<ApprovalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/approvals/{second.Id}/approve", owner.Token);
        approved.Status.Should().Be("Approved");
        (await ReadAsync<FundData>(HttpMethod.Get, $"/api/v1/groups/{groupId}/funds/{fund.Id}", owner.Token)).BalanceCents.Should().Be(-4_000_000);
    }

    [Fact]
    public async Task WithoutAThresholdNothingWaits()
    {
        var (_, b, _, groupId) = await NewGroupAsync(null);

        await ReadAsync<BillData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/bills", b.Token, Bill("Lớn", 900_000_000, b.Id, b.Id), HttpStatusCode.Created);
    }
}
