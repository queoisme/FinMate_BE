using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API;
using FinMate.API.Controllers;
using FinMate.API.Controllers.Ops;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Support;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Chuông thông báo của web vận hành: ai nhận, đếm chưa đọc, đánh dấu đã đọc, và không có dữ liệu khách trong nội dung.</summary>
[Collection("Integration")]
public class OpsNotificationsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Notifications = "/api/v1/ops/notifications";
    private const string Approvals = "/api/v1/ops/approvals";
    private const string Tickets = "/api/v1/support/tickets";
    private const string Ops = "/api/v1/ops/tickets";
    private const string CustomerSecret = "MatKhauBiMat-9981";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public OpsNotificationsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Account(Guid Id, string Token);

    public Task InitializeAsync() => _factory.SetFeaturesAsync(groups: true, support: true);

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Account> AccountAsync(UserRole role, Action<User>? customize = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
        customize?.Invoke(user);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new Account(user.Id, scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user));
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    private async Task<Guid> TicketAsync(Account customer)
        => (await DataAsync(await SendAsync(HttpMethod.Post, Tickets, customer.Token, new CreateSupportTicketRequest(SupportTicketCategory.Security, CustomerSecret, CustomerSecret)))).GetProperty("ticket").GetProperty("id").GetGuid();

    private async Task<int> UnreadAsync(Account who)
        => (await DataAsync(await SendAsync(HttpMethod.Get, $"{Notifications}/unread-count", who.Token))).GetProperty("unread").GetInt32();

    private async Task<List<JsonElement>> ListAsync(Account who, string query = "")
        => (await DataAsync(await SendAsync(HttpMethod.Get, $"{Notifications}{query}", who.Token))).EnumerateArray().ToList();

    private static JsonElement? For(IEnumerable<JsonElement> items, string type, Guid entityId)
        => items.Where(i => i.GetProperty("type").GetString() == type && i.GetProperty("relatedEntityId").GetGuid() == entityId)
            .Select(i => (JsonElement?)i).FirstOrDefault();

    [Fact]
    public async Task ANewTicketReachesManagersAndStaffButNotTheCustomerAndCarriesNoCustomerText()
    {
        var manager = await AccountAsync(UserRole.OpsManager);
        var staff = await AccountAsync(UserRole.Staff);
        var customer = await AccountAsync(UserRole.User);

        var ticket = await TicketAsync(customer);

        var item = For(await ListAsync(manager), "ticket_new", ticket);
        item.Should().NotBeNull();
        item!.Value.GetProperty("readAt").ValueKind.Should().Be(JsonValueKind.Null);
        item.Value.GetProperty("relatedEntityType").GetString().Should().Be("SupportTicket");
        item.Value.GetRawText().Should().NotContain(CustomerSecret).And.NotContain(customer.Id.ToString()).And.NotContain("finmate.local");
        For(await ListAsync(staff), "ticket_new", ticket).Should().NotBeNull();

        // Nhân viên tự mở ca với tư cách khách thì không tự được báo về ca của mình.
        var staffAsCustomer = await AccountAsync(UserRole.Staff);
        var ownTicket = await TicketAsync(staffAsCustomer);
        For(await ListAsync(staffAsCustomer), "ticket_new", ownTicket).Should().BeNull();
        For(await ListAsync(staff), "ticket_new", ownTicket).Should().NotBeNull();
    }

    [Fact]
    public async Task UnreadCountFollowsReadAndReadAllAndIsPerPerson()
    {
        var manager = await AccountAsync(UserRole.OpsManager);
        var other = await AccountAsync(UserRole.OpsManager);
        var customer = await AccountAsync(UserRole.User);
        var first = await TicketAsync(customer);
        var second = await TicketAsync(customer);
        (await UnreadAsync(manager)).Should().Be(2);

        var firstId = For(await ListAsync(manager), "ticket_new", first)!.Value.GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Post, $"{Notifications}/{firstId}/read", manager.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UnreadAsync(manager)).Should().Be(1);

        // Người khác không đọc, không đánh dấu được thông báo của mình.
        (await SendAsync(HttpMethod.Post, $"{Notifications}/{firstId}/read", other.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await UnreadAsync(other)).Should().Be(2);

        (await ListAsync(manager, "?unreadOnly=true")).Should().ContainSingle().Which.GetProperty("relatedEntityId").GetGuid().Should().Be(second);

        (await DataAsync(await SendAsync(HttpMethod.Post, $"{Notifications}/read-all", manager.Token))).GetProperty("marked").GetInt32().Should().Be(1);
        (await UnreadAsync(manager)).Should().Be(0);
        (await UnreadAsync(other)).Should().Be(2);
    }

    [Fact]
    public async Task ListIsPagedAndSearchedOnTheServer()
    {
        var manager = await AccountAsync(UserRole.OpsManager);
        var customer = await AccountAsync(UserRole.User);
        for (var i = 0; i < 3; i++)
        {
            await TicketAsync(customer);
        }

        var response = await SendAsync(HttpMethod.Get, $"{Notifications}?limit=2&offset=0", manager.Token);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        root.GetProperty("meta").GetProperty("total").GetInt32().Should().Be(3);
        root.GetProperty("data").GetArrayLength().Should().Be(2);
        (await ListAsync(manager, "?limit=2&offset=2")).Should().HaveCount(1);
        (await ListAsync(manager, "?search=khong-co-gi-nhu-vay")).Should().BeEmpty();
        (await ListAsync(manager, "?search=hỗ trợ")).Should().HaveCount(3);
    }

    [Fact]
    public async Task OnlyTheInternalTeamCanOpenTheBell()
    {
        var customer = await AccountAsync(UserRole.User);

        (await SendAsync(HttpMethod.Get, Notifications, customer.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.GetAsync(Notifications)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AssigningATicketNotifiesTheAssigneeOnly()
    {
        var manager = await AccountAsync(UserRole.OpsManager);
        var staff = await AccountAsync(UserRole.Staff);
        var customer = await AccountAsync(UserRole.User);
        var ticket = await TicketAsync(customer);

        await DataAsync(await SendAsync(HttpMethod.Patch, $"{Ops}/{ticket}/assignee", manager.Token, new OpsTicketAssignRequest(staff.Id)));

        For(await ListAsync(staff), "ticket_assigned", ticket).Should().NotBeNull();
        For(await ListAsync(manager), "ticket_assigned", ticket).Should().BeNull();
    }

    [Fact]
    public async Task AProposalNotifiesApproversThenTheProposerLearnsTheOutcome()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var target = await AccountAsync(UserRole.User, u => u.IsLocked = true);

        var approval = (await DataAsync(await SendAsync(HttpMethod.Post, Approvals, staff.Token,
            new OpsProposeApprovalRequest(OpsApprovalType.UnlockUser, target.Id, null, null, "Đã xác minh")))).GetProperty("id").GetGuid();

        var pending = For(await ListAsync(manager), "approval_pending", approval);
        pending.Should().NotBeNull();
        pending!.Value.GetRawText().Should().NotContain(target.Id.ToString());
        For(await ListAsync(staff), "approval_pending", approval).Should().BeNull();

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{approval}/approve", manager.Token, new { note = "ok" }));

        For(await ListAsync(staff), "approval_decided", approval).Should().NotBeNull();
    }

    [Fact]
    public async Task ADeletionRequestReachesAdminsOnly()
    {
        var admin = await AccountAsync(UserRole.Admin);
        var manager = await AccountAsync(UserRole.OpsManager);
        var customer = await AccountAsync(UserRole.User);

        // Gọi thẳng dịch vụ: đường xóa tài khoản đòi mật khẩu băm thật, không phải thứ test này kiểm.
        using var scope = _factory.Services.CreateScope();
        var deletionId = Guid.NewGuid();
        await scope.ServiceProvider.GetRequiredService<FinMate.Application.Ops.IOpsNotificationService>().NotifyCapabilityAsync(
            OpsNotificationType.DataDeletionRequested, FinMate.Application.Ops.OpsCapability.ManageSystem, "Có yêu cầu xóa dữ liệu mới", "x", "DataDeletionRequest", deletionId, [customer.Id]);

        For(await ListAsync(admin), "data_deletion_requested", deletionId).Should().NotBeNull();
        For(await ListAsync(manager), "data_deletion_requested", deletionId).Should().BeNull();
    }
}
