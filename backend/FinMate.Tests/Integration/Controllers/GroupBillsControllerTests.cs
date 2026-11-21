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

/// <summary>Hóa đơn chia, sổ nợ, trả nợ, nhắc nợ và thẻ tổng của nhóm qua API thật.</summary>
[Collection("Integration")]
public class GroupBillsControllerTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GroupBillsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(new CapturingEmailSender());
        })).CreateClient();
    }

    public Task InitializeAsync() => _factory.SetGroupsEnabledAsync(true);

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
    private record SplitData(Guid? UserId, string DisplayName, long ShareCents);
    private record BillData(Guid Id, string Title, long AmountCents, Guid? PaidByUserId, string PaidByName, int SplitCount, List<SplitData> Splits);
    private record BalanceMember(Guid UserId, string DisplayName, long NetCents);
    private record TransferData(Guid FromUserId, Guid ToUserId, long AmountCents);
    private record BalancesData(string Method, List<BalanceMember> Members, List<TransferData>? Transfers);
    private record RemindData(List<Guid> Sent, List<SkippedData> Skipped);
    private record SkippedData(Guid UserId, DateTimeOffset RetryAt);
    private record SummaryData(long TotalSpentCents, int ActiveGroupCount, long MyShareDueCents, long? MyPayableCents, string? ClosingDate);
    private record BudgetData(Guid Id, long SpentCents);
    private record ReportData(long TotalSpentCents, List<MemberTotals> ByMember, long FundSpentCents);
    private record MemberTotals(Guid UserId, long SpentCents);

    private async Task<TestUser> NewUserAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Bill Tester"));
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

    private async Task<T> ReadAsync<T>(HttpMethod method, string url, string token, object? body = null)
    {
        var response = await SendAsync(method, url, token, body);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
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

    private async Task<(TestUser Owner, TestUser B, TestUser C, Guid GroupId)> NewGroupOfThreeAsync()
    {
        var owner = await NewUserAsync();
        var group = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/groups", owner.Token, new { name = "Phòng trọ", type = "Roommates", settlementModel = "SplitBill" });
        var b = await NewUserAsync();
        var c = await NewUserAsync();
        foreach (var u in new[] { b, c })
        {
            var invite = await ReadAsync<InviteData>(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", owner.Token, new { role = "Member" });
            await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", u.Token, new { token = invite.Token });
        }

        return (owner, b, c, group.Id);
    }

    [Fact]
    public async Task BillsDriveBalancesSettlementsRemindersSummaryAndBudget()
    {
        var (owner, b, c, groupId) = await NewGroupOfThreeAsync();
        var budget = await ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { limitCents = 10_000_000L });

        // 100.000đ chia đều cho 3 người, B trả: phần dư 1đ dồn cho B.
        var bill = await ReadAsync<BillData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/bills", b.Token, new
        {
            title = "Tiền điện",
            amountCents = 100_000L,
            paidByUserId = b.Id,
            splitMode = "Equal",
            splits = new[] { new { userId = owner.Id, shareCents = 0L }, new { userId = b.Id, shareCents = 0L }, new { userId = c.Id, shareCents = 0L } },
        });
        bill.SplitCount.Should().Be(3);
        bill.Splits.Sum(s => s.ShareCents).Should().Be(100_000);
        bill.Splits.Single(s => s.UserId == b.Id).ShareCents.Should().Be(33_334);

        // Sai tổng ở chế độ tùy chỉnh bị chặn; người trả không phải thành viên bị chặn.
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/bills", owner.Token, (HttpStatusCode)422, new
        {
            title = "Sai",
            amountCents = 100L,
            paidByUserId = owner.Id,
            splitMode = "Custom",
            splits = new[] { new { userId = owner.Id, shareCents = 40L }, new { userId = b.Id, shareCents = 40L } },
        }, "GROUP_BILL_SPLIT_MISMATCH");
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/bills", owner.Token, (HttpStatusCode)422, new
        {
            title = "Người lạ",
            amountCents = 100L,
            paidByUserId = Guid.NewGuid(),
            splitMode = "Equal",
            splits = new[] { new { userId = owner.Id, shareCents = 0L } },
        }, "GROUP_BILL_INVALID");

        // Hóa đơn tính cả vào ngân sách nhóm và báo cáo tháng, đứng tên người trả.
        (await ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", owner.Token)).Single(x => x.Id == budget.Id).SpentCents.Should().Be(100_000);
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var report = await ReadAsync<ReportData>(HttpMethod.Get, $"/api/v1/groups/{groupId}/reports/monthly?year={now.Year}&month={now.Month}", owner.Token);
        report.TotalSpentCents.Should().Be(100_000);
        report.ByMember.Single(m => m.UserId == b.Id).SpentCents.Should().Be(100_000);
        report.FundSpentCents.Should().Be(0);

        // Sổ nợ: B được nợ 66.666, owner nợ 33.333, C nợ 33.333.
        var balances = await ReadAsync<BalancesData>(HttpMethod.Get, $"/api/v1/groups/{groupId}/balances", c.Token);
        balances.Members.Single(m => m.UserId == b.Id).NetCents.Should().Be(66_666);
        balances.Members.Single(m => m.UserId == owner.Id).NetCents.Should().Be(-33_333);
        balances.Members.Sum(m => m.NetCents).Should().Be(0);
        balances.Transfers.Should().BeEquivalentTo([new TransferData(c.Id, b.Id, 33_333), new TransferData(owner.Id, b.Id, 33_333)]);

        // Trả nợ: người ngoài cuộc không ghi được; vượt nợ bị chặn; chính người trả ghi được.
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/settlements", c.Token, HttpStatusCode.Forbidden, new { fromUserId = owner.Id, toUserId = b.Id, amountCents = 1L });
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/settlements", c.Token, (HttpStatusCode)422, new { fromUserId = c.Id, toUserId = b.Id, amountCents = 33_334L }, "GROUP_SETTLEMENT_EXCEEDS_DEBT");
        await ReadAsync<object>(HttpMethod.Post, $"/api/v1/groups/{groupId}/settlements", c.Token, new { fromUserId = c.Id, toUserId = b.Id, amountCents = 33_333L, note = "Chuyển khoản" });
        (await ReadAsync<BalancesData>(HttpMethod.Get, $"/api/v1/groups/{groupId}/balances", c.Token)).Members.Single(m => m.UserId == c.Id).NetCents.Should().Be(0);

        // Nhắc nợ: C không nợ nữa; owner nợ B; chủ nợ (B) nhắc được, lần hai trong 24h bị 429; người không phải chủ nợ không nhắc được.
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/members/{c.Id}/remind-debt", b.Token, (HttpStatusCode)422, code: "GROUP_NO_DEBT");
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/members/{owner.Id}/remind-debt", c.Token, HttpStatusCode.Forbidden);
        (await ReadAsync<RemindData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/members/{owner.Id}/remind-debt", b.Token)).Sent.Should().Equal(owner.Id);
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/members/{owner.Id}/remind-debt", b.Token, (HttpStatusCode)429, code: "GROUP_REMINDER_COOLDOWN");
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/remind-all", b.Token, (HttpStatusCode)429, code: "GROUP_REMINDER_COOLDOWN");

        // Danh sách và chi tiết hóa đơn.
        (await ReadAsync<List<BillData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/bills", c.Token)).Should().ContainSingle(x => x.Id == bill.Id);
        (await ReadAsync<BillData>(HttpMethod.Get, $"/api/v1/groups/{groupId}/bills/{bill.Id}", c.Token)).Title.Should().Be("Tiền điện");

        // Thẻ tổng: owner nợ 33.333, phần của mình trong tháng 33.333; người chưa có hóa đơn nào thì payable null.
        var summary = await ReadAsync<SummaryData>(HttpMethod.Get, "/api/v1/groups/me/summary", owner.Token);
        summary.ActiveGroupCount.Should().Be(1);
        summary.TotalSpentCents.Should().Be(100_000);
        summary.MyShareDueCents.Should().Be(33_333);
        summary.MyPayableCents.Should().Be(33_333);
        summary.ClosingDate.Should().NotBeNull();
        var lonely = await NewUserAsync();
        (await ReadAsync<SummaryData>(HttpMethod.Get, "/api/v1/groups/me/summary", lonely.Token)).MyPayableCents.Should().BeNull();
    }

    [Fact]
    public async Task OutsidersAndViewersCannotUseTheDebtLedger()
    {
        var (owner, b, _, groupId) = await NewGroupOfThreeAsync();
        var outsider = await NewUserAsync();
        await ExpectAsync(HttpMethod.Get, $"/api/v1/groups/{groupId}/balances", outsider.Token, HttpStatusCode.NotFound);
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/bills", outsider.Token, HttpStatusCode.NotFound,
            new { title = "x", amountCents = 10L, paidByUserId = owner.Id, splits = new[] { new { userId = owner.Id, shareCents = 0L } } });

        await ReadAsync<object>(HttpMethod.Patch, $"/api/v1/groups/{groupId}/members/{b.Id}", owner.Token, new { role = "Viewer" });
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/bills", b.Token, HttpStatusCode.Forbidden,
            new { title = "x", amountCents = 10L, paidByUserId = owner.Id, splits = new[] { new { userId = owner.Id, shareCents = 0L } } });
    }
}
