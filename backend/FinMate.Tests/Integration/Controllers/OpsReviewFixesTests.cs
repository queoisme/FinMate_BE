using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API;
using FinMate.API.Controllers;
using FinMate.API.Controllers.Ops;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Application.Support;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Các lỗi tìm được khi rà soát: đề xuất quá hạn chặn vĩnh viễn, ca chuyển cấp tự rời hàng đợi, ca đã đóng vẫn dùng được, quyền xem dữ liệu sống sau khi khách rút lui.</summary>
[Collection("Integration")]
public class OpsReviewFixesTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Approvals = "/api/v1/ops/approvals";
    private const string Tickets = "/api/v1/support/tickets";
    private const string Ops = "/api/v1/ops/tickets";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public OpsReviewFixesTests(AuthApiFactory factory)
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
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private async Task<T> InDbAsync<T>(Func<FinMateDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private async Task<Guid> TicketAsync(Account customer)
        => (await DataAsync(await SendAsync(HttpMethod.Post, Tickets, customer.Token, new CreateSupportTicketRequest(SupportTicketCategory.Security, "Sự cố", "Mô tả")))).GetProperty("ticket").GetProperty("id").GetGuid();

    private async Task<Guid> ClaimedTicketAsync(Account customer, Account staff)
    {
        var id = await TicketAsync(customer);
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{id}/claim", staff.Token));
        return id;
    }

    private async Task<string> StatusAsync(Account manager, Guid ticketId)
        => (await DataAsync(await SendAsync(HttpMethod.Get, $"{Ops}/{ticketId}", manager.Token))).GetProperty("ticket").GetProperty("status").GetString()!;

    [Fact]
    public async Task AnExpiredProposalNoLongerBlocksANewOneForTheSameTarget()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var customer = await AccountAsync(UserRole.User, u => u.IsLocked = true);
        var body = new OpsProposeApprovalRequest(OpsApprovalType.UnlockUser, customer.Id, null, null, "Đã xác minh");
        var expired = (await DataAsync(await SendAsync(HttpMethod.Post, Approvals, staff.Token, body))).GetProperty("id").GetGuid();
        await InDbAsync(db => db.OpsApprovalRequests.Where(a => a.Id == expired).ExecuteUpdateAsync(s => s.SetProperty(a => a.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1))));

        await DataAsync(await SendAsync(HttpMethod.Post, Approvals, staff.Token, body));

        var old = await InDbAsync(db => db.OpsApprovalRequests.AsNoTracking().SingleAsync(a => a.Id == expired));
        old.Status.Should().Be(OpsApprovalStatus.Failed);
        old.FailureCode.Should().Be(OpsErrorCodes.ApprovalExpired);
    }

    [Fact]
    public async Task AProposalCannotCiteAnotherCustomersTicket()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var other = await AccountAsync(UserRole.User);
        var target = await AccountAsync(UserRole.User, u => u.IsLocked = true);
        var ticket = await ClaimedTicketAsync(other, staff);

        var response = await SendAsync(HttpMethod.Post, Approvals, staff.Token, new OpsProposeApprovalRequest(OpsApprovalType.UnlockUser, target.Id, null, ticket, "x"));

        (await ErrorCodeAsync(response)).Should().Be(OpsErrorCodes.InvalidTarget);
    }

    [Fact]
    public async Task AnEscalatedTicketStaysInTheManagerQueueUntilAManagerActs()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var customer = await AccountAsync(UserRole.User);
        var ticket = await ClaimedTicketAsync(customer, staff);
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticket}/escalate", staff.Token, new OpsTicketEscalateRequest("Đã thử quét lại")));

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticket}/messages", staff.Token, new OpsTicketMessageRequest("Đã chuyển cấp trên", false)));
        (await StatusAsync(manager, ticket)).Should().Be("Escalated");

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{ticket}/messages", customer.Token, new SupportMessageRequest("Cảm ơn")));
        (await StatusAsync(manager, ticket)).Should().Be("Escalated");

        (await SendAsync(HttpMethod.Patch, $"{Ops}/{ticket}/status", staff.Token, new OpsTicketStatusRequest(SupportTicketStatus.Resolved))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await DataAsync(await SendAsync(HttpMethod.Patch, $"{Ops}/{ticket}/status", manager.Token, new OpsTicketStatusRequest(SupportTicketStatus.InProgress)));
        (await StatusAsync(manager, ticket)).Should().Be("InProgress");
    }

    [Fact]
    public async Task EscalatingAResolvedTicketReopensItsSlaClock()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var customer = await AccountAsync(UserRole.User);
        var ticket = await ClaimedTicketAsync(customer, staff);
        await DataAsync(await SendAsync(HttpMethod.Patch, $"{Ops}/{ticket}/status", staff.Token, new OpsTicketStatusRequest(SupportTicketStatus.Resolved)));

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticket}/escalate", staff.Token, new OpsTicketEscalateRequest("Vẫn lỗi")));

        (await InDbAsync(db => db.SupportTickets.AsNoTracking().Where(t => t.Id == ticket).Select(t => t.ResolvedAt).SingleAsync())).Should().BeNull();
    }

    [Fact]
    public async Task AClosedTicketCannotBeUsedToLockAnAccountOrViewAGroup()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var customer = await AccountAsync(UserRole.User);
        var ticket = await ClaimedTicketAsync(customer, staff);
        var groupId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InDbAsync(async db =>
        {
            db.Groups.Add(new Group { Id = groupId, Name = "Nhóm", Type = GroupType.Family, TransactionVisibility = GroupTransactionVisibility.Full, CreatedAt = now, UpdatedAt = now });
            db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = groupId, UserId = customer.Id, Role = GroupRole.Owner, JoinedAt = now, UpdatedAt = now });
            return await db.SaveChangesAsync();
        });
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{ticket}/close", customer.Token));

        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/ops/users/{customer.Id}/emergency-lock", staff.Token, new OpsEmergencyLockRequest(ticket, "muộn")))).Should().Be(SupportErrorCodes.TicketClosed);
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/ops/groups/{groupId}?ticketId={ticket}", staff.Token))).Should().Be(SupportErrorCodes.TicketClosed);
        (await InDbAsync(db => db.Users.Where(u => u.Id == customer.Id).Select(u => u.IsLocked).SingleAsync())).Should().BeFalse();
    }

    private async Task<(Account Customer, Account Staff, Guid Ticket, Guid Grant)> GrantedAsync()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var customer = await AccountAsync(UserRole.User);
        var ticket = await ClaimedTicketAsync(customer, staff);
        var grant = (await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticket}/access-requests", staff.Token, new OpsAccessRequest([SupportAccessScope.Accounts], "Kiểm tra")))).GetProperty("id").GetGuid();
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{ticket}/access-requests/{grant}/approve", customer.Token, new ApproveSupportAccessRequest(null)));
        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticket}/customer-data/accounts", staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        return (customer, staff, ticket, grant);
    }

    [Fact]
    public async Task RequestingAccountDeletionEndsGrantsForGood()
    {
        var (customer, staff, ticket, grant) = await GrantedAsync();
        const string password = "Password123!";
        using (var scope = _factory.Services.CreateScope())
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == customer.Id);
            user.PasswordHash = hasher.Hash(password);
            await db.SaveChangesAsync();
        }

        (await SendAsync(HttpMethod.Delete, "/api/v1/auth/account", customer.Token, new DeleteAccountRequest(password))).IsSuccessStatusCode.Should().BeTrue();

        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticket}/customer-data/accounts", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await InDbAsync(db => db.SupportAccessGrants.AsNoTracking().Where(g => g.Id == grant).Select(g => g.Status).SingleAsync())).Should().Be(SupportAccessStatus.Revoked);
    }

    [Fact]
    public async Task LockingTheCustomerEndsGrantsAndUnlockingDoesNotRestoreThem()
    {
        var (customer, staff, ticket, grant) = await GrantedAsync();

        await DataAsync(await SendAsync(HttpMethod.Post, $"/api/v1/ops/users/{customer.Id}/emergency-lock", staff.Token, new OpsEmergencyLockRequest(ticket, "bị chiếm")));
        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticket}/customer-data/accounts", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await InDbAsync(db => db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsLocked, false)));
        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticket}/customer-data/accounts", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await InDbAsync(db => db.SupportAccessGrants.AsNoTracking().Where(g => g.Id == grant).Select(g => g.Status).SingleAsync())).Should().Be(SupportAccessStatus.Revoked);
    }

    [Fact]
    public async Task ALockedCustomersExistingGrantIsRefusedEvenIfItWasNotEnded()
    {
        var (customer, staff, ticket, _) = await GrantedAsync();

        await InDbAsync(db => db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsLocked, true)));

        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticket}/customer-data/accounts", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithSupportOffNoNewConsentCanBeGiven_ButCustomersCanStillWithdrawIt()
    {
        var (customer, staff, ticket, grant) = await GrantedAsync();
        var pendingCase = await ClaimedTicketAsync(customer, staff);
        var pending = (await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{pendingCase}/access-requests", staff.Token, new OpsAccessRequest([SupportAccessScope.Budgets], "Kiểm tra")))).GetProperty("id").GetGuid();

        await _factory.SetFeaturesAsync(support: false);
        try
        {
            (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{pendingCase}/access-requests/{pending}/approve", customer.Token, new ApproveSupportAccessRequest(null)))).Should().Be(SupportErrorCodes.Disabled);
            (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticket}/access-requests", staff.Token, new OpsAccessRequest([SupportAccessScope.Budgets], "Thêm")))).Should().Be(SupportErrorCodes.Disabled);

            await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/support/access-log", customer.Token));
            await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{pendingCase}/access-requests/{pending}/deny", customer.Token));
            await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{ticket}/access-requests/{grant}/revoke", customer.Token));
        }
        finally
        {
            await _factory.SetFeaturesAsync(support: true);
        }

        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticket}/customer-data/accounts", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithGroupsOffOpsCannotReadGroupsOrMoveOwnership()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var owner = await AccountAsync(UserRole.User);
        var member = await AccountAsync(UserRole.User);
        var ticket = await ClaimedTicketAsync(member, staff);
        var groupId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InDbAsync(async db =>
        {
            db.Groups.Add(new Group { Id = groupId, Name = "Nhóm", Type = GroupType.Family, TransactionVisibility = GroupTransactionVisibility.Full, CreatedAt = now, UpdatedAt = now });
            db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = groupId, UserId = owner.Id, Role = GroupRole.Owner, JoinedAt = now, UpdatedAt = now });
            db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = groupId, UserId = member.Id, Role = GroupRole.Member, JoinedAt = now, UpdatedAt = now });
            return await db.SaveChangesAsync();
        });
        var body = new OpsProposeApprovalRequest(OpsApprovalType.TransferGroupOwner, groupId, member.Id, null, "Chủ nhóm mất liên lạc");
        var approval = (await DataAsync(await SendAsync(HttpMethod.Post, Approvals, staff.Token, body))).GetProperty("id").GetGuid();

        await _factory.SetFeaturesAsync(groups: false);
        try
        {
            (await ErrorCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/ops/groups/{groupId}?ticketId={ticket}", staff.Token))).Should().Be("GROUPS_DISABLED");
            (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{approval}/approve", manager.Token, new OpsApprovalDecisionRequest(null)))).Should().Be(OpsErrorCodes.ApprovalFailed);
        }
        finally
        {
            await _factory.SetFeaturesAsync(groups: true);
        }

        var roles = await InDbAsync(db => db.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId).ToListAsync());
        roles.Single(m => m.UserId == owner.Id).Role.Should().Be(GroupRole.Owner);
        (await InDbAsync(db => db.OpsApprovalRequests.AsNoTracking().SingleAsync(a => a.Id == approval))).FailureCode.Should().Be("GROUPS_DISABLED");
    }

    private sealed class FailingOnceEmailSender : IEmailSender
    {
        public int Attempts { get; private set; }

        public Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
            => Task.FromResult(++Attempts > 1);
    }

    [Fact]
    public async Task AFailedOtpEmailIsReportedAndCanBeRetriedImmediately()
    {
        var email = new FailingOnceEmailSender();
        var client = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(email);
        })).CreateClient();
        var staff = await AccountAsync(UserRole.Staff);

        var first = await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/send", staff.Token, client: client);
        (await ErrorCodeAsync(first)).Should().Be(OpsErrorCodes.OtpEmailFailed);

        (await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/send", staff.Token, client: client)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        email.Attempts.Should().Be(2, "thời gian chờ đã được bỏ nên lần thử lại thật sự gửi email");
    }
}
