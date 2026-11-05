using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Việc nhạy cảm luôn cần hai người, và các chức năng của Manager không mở cho Staff.</summary>
[Collection("Integration")]
public class OpsApprovalsAndManagerTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Approvals = "/api/v1/ops/approvals";
    private const string Tickets = "/api/v1/support/tickets";
    private const string OpsTickets = "/api/v1/ops/tickets";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static bool _supportEnabled;

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public OpsApprovalsAndManagerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Account(Guid Id, string Token);

    public async Task InitializeAsync()
    {
        if (_supportEnabled)
        {
            return;
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var admin = await db.Users.AsNoTracking().SingleAsync(u => u.Email == AuthApiFactory.AdminEmail);
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(admin);
        const string config = "/api/v1/admin/system-config";
        var version = (await DataAsync(await SendAsync(HttpMethod.Get, config, token))).GetProperty("version").GetInt32();
        await DataAsync(await SendAsync(HttpMethod.Put, config, token, new { expectedVersion = version, content = new { features = new { support = true, groups = true } }, note = "kiem thu" }));
        _supportEnabled = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Account> AccountAsync(UserRole role, Action<User>? customize = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"khach{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
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

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private async Task<T> InDbAsync<T>(Func<FinMateDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private Task<bool> IsLockedAsync(Guid id) => InDbAsync(db => db.Users.IgnoreQueryFilters().Where(u => u.Id == id).Select(u => u.IsLocked).SingleAsync());

    private async Task<Guid> ProposeUnlockAsync(Account proposer, Guid targetId)
        => (await DataAsync(await SendAsync(HttpMethod.Post, Approvals, proposer.Token, new OpsProposeApprovalRequest(OpsApprovalType.UnlockUser, targetId, null, null, "Đã xác minh chính chủ")))).GetProperty("id").GetGuid();

    private async Task<Guid> TicketAsync(Account customer, SupportTicketCategory category = SupportTicketCategory.Security)
        => (await DataAsync(await SendAsync(HttpMethod.Post, Tickets, customer.Token, new CreateSupportTicketRequest(category, "Sự cố", "Mô tả")))).GetProperty("ticket").GetProperty("id").GetGuid();

    private Task<JsonElement> ClaimAsync(Account staff, Guid ticketId) => SendAsync(HttpMethod.Post, $"{OpsTickets}/{ticketId}/claim", staff.Token).ContinueWith(t => DataAsync(t.Result)).Unwrap();

    [Fact]
    public async Task StaffProposesAndManagerApproves_ThenTheAccountIsUnlocked()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var customer = await AccountAsync(UserRole.User, u => u.IsLocked = true);

        var id = await ProposeUnlockAsync(staff, customer.Id);
        (await IsLockedAsync(customer.Id)).Should().BeTrue("đề xuất chưa làm gì");

        var approved = await DataAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{id}/approve", manager.Token, new OpsApprovalDecisionRequest("OK")));

        approved.GetProperty("status").GetString().Should().Be("Approved");
        approved.GetProperty("decidedByUserId").GetGuid().Should().Be(manager.Id);
        (await IsLockedAsync(customer.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DecisionsWorkWithoutABody_BecauseTheNoteIsOptional()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var first = await AccountAsync(UserRole.User, u => u.IsLocked = true);
        var second = await AccountAsync(UserRole.User, u => u.IsLocked = true);

        var approve = await ProposeUnlockAsync(staff, first.Id);
        (await DataAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{approve}/approve", manager.Token))).GetProperty("status").GetString().Should().Be("Approved");

        var reject = await ProposeUnlockAsync(staff, second.Id);
        (await DataAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{reject}/reject", manager.Token))).GetProperty("status").GetString().Should().Be("Rejected");
    }

    [Fact]
    public async Task NobodyApprovesTheirOwnProposal_EvenAnAdmin()
    {
        var admin = await AccountAsync(UserRole.Admin);
        var customer = await AccountAsync(UserRole.User, u => u.IsLocked = true);
        var id = await ProposeUnlockAsync(admin, customer.Id);

        (await SendAsync(HttpMethod.Post, $"{Approvals}/{id}/approve", admin.Token, new OpsApprovalDecisionRequest(null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IsLockedAsync(customer.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task StaffCannotDecide_AndOnlySeesTheirOwnProposals()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var other = await AccountAsync(UserRole.Staff);
        var customer = await AccountAsync(UserRole.User, u => u.IsLocked = true);
        var id = await ProposeUnlockAsync(staff, customer.Id);

        (await SendAsync(HttpMethod.Post, $"{Approvals}/{id}/approve", other.Token, new OpsApprovalDecisionRequest(null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"{Approvals}/{id}/reject", other.Token, new OpsApprovalDecisionRequest(null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await (await SendAsync(HttpMethod.Get, Approvals, staff.Token)).Content.ReadAsStringAsync()).Should().Contain(id.ToString());
        (await (await SendAsync(HttpMethod.Get, Approvals, other.Token)).Content.ReadAsStringAsync()).Should().NotContain(id.ToString());
    }

    [Fact]
    public async Task OrdinaryUserCannotProposeOrList()
    {
        var user = await AccountAsync(UserRole.User);
        var target = await AccountAsync(UserRole.User, u => u.IsLocked = true);

        (await SendAsync(HttpMethod.Get, Approvals, user.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, Approvals, user.Token, new OpsProposeApprovalRequest(OpsApprovalType.UnlockUser, target.Id, null, null, "x"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentApprovalsWins()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var first = await AccountAsync(UserRole.OpsManager);
        var second = await AccountAsync(UserRole.Admin);
        var customer = await AccountAsync(UserRole.User, u => u.IsLocked = true);
        var id = await ProposeUnlockAsync(staff, customer.Id);

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Post, $"{Approvals}/{id}/approve", first.Token, new OpsApprovalDecisionRequest(null)),
            SendAsync(HttpMethod.Post, $"{Approvals}/{id}/approve", second.Token, new OpsApprovalDecisionRequest(null)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var audits = await InDbAsync(db => db.AuditLogs.AsNoTracking().Where(a => a.EventType == "Ops.Approval.Approved").ToListAsync());
        audits.Count(a => a.Metadata!.Contains(id.ToString())).Should().Be(1);
    }

    [Fact]
    public async Task RejectedOrExpiredProposalsDoNothing()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var customer = await AccountAsync(UserRole.User, u => u.IsLocked = true);

        var rejected = await ProposeUnlockAsync(staff, customer.Id);
        (await DataAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{rejected}/reject", manager.Token, new OpsApprovalDecisionRequest("Chưa đủ căn cứ")))).GetProperty("status").GetString().Should().Be("Rejected");
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{rejected}/approve", manager.Token, new OpsApprovalDecisionRequest(null)))).Should().Be(OpsErrorCodes.ApprovalNotPending);
        (await IsLockedAsync(customer.Id)).Should().BeTrue();

        var expired = await ProposeUnlockAsync(staff, customer.Id);
        await InDbAsync(db => db.OpsApprovalRequests.Where(a => a.Id == expired).ExecuteUpdateAsync(s => s.SetProperty(a => a.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1))));
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{expired}/approve", manager.Token, new OpsApprovalDecisionRequest(null)))).Should().Be(OpsErrorCodes.ApprovalExpired);
        (await IsLockedAsync(customer.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ADuplicateProposalForTheSameTargetIsRefused()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var customer = await AccountAsync(UserRole.User, u => u.IsLocked = true);
        await ProposeUnlockAsync(staff, customer.Id);

        var second = await SendAsync(HttpMethod.Post, Approvals, staff.Token, new OpsProposeApprovalRequest(OpsApprovalType.UnlockUser, customer.Id, null, null, "lần nữa"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(second)).Should().Be(OpsErrorCodes.ApprovalAlreadyPending);
    }

    [Fact]
    public async Task ProposalsCannotTargetInternalAccountsOrUnlockedAccounts()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var lockedAdmin = await AccountAsync(UserRole.Admin, u => u.IsLocked = true);
        var fine = await AccountAsync(UserRole.User);

        foreach (var target in new[] { lockedAdmin.Id, fine.Id })
        {
            (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, Approvals, staff.Token, new OpsProposeApprovalRequest(OpsApprovalType.UnlockUser, target, null, null, "x")))).Should().Be(OpsErrorCodes.InvalidTarget);
        }
    }

    [Fact]
    public async Task IfTheTargetChangedBeforeApprovalTheApprovalFailsAndIsRecorded()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var customer = await AccountAsync(UserRole.User, u => u.IsLocked = true);
        var id = await ProposeUnlockAsync(staff, customer.Id);

        await InDbAsync(db => db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsLocked, false)));

        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{id}/approve", manager.Token, new OpsApprovalDecisionRequest(null)))).Should().Be(OpsErrorCodes.ApprovalFailed);

        var row = await InDbAsync(db => db.OpsApprovalRequests.AsNoTracking().SingleAsync(a => a.Id == id));
        row.Status.Should().Be(OpsApprovalStatus.Failed);
        row.FailureCode.Should().Be(OpsErrorCodes.InvalidTarget);
    }

    [Fact]
    public async Task ApprovedCancelDeletionRestoresTheAccount()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var now = DateTimeOffset.UtcNow;
        var customer = await AccountAsync(UserRole.User, u => u.DeletedAt = now);
        await InDbAsync(async db =>
        {
            db.DataDeletionRequests.Add(new DataDeletionRequest { Id = Guid.NewGuid(), UserId = customer.Id, Status = DataDeletionStatus.Pending, RequestedAt = now, ScheduledHardDeleteAt = now.AddDays(30) });
            return await db.SaveChangesAsync();
        });

        var id = (await DataAsync(await SendAsync(HttpMethod.Post, Approvals, staff.Token, new OpsProposeApprovalRequest(OpsApprovalType.CancelDeletion, customer.Id, null, null, "Khách đổi ý")))).GetProperty("id").GetGuid();
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{id}/approve", manager.Token, new OpsApprovalDecisionRequest(null)));

        (await InDbAsync(db => db.Users.IgnoreQueryFilters().Where(u => u.Id == customer.Id).Select(u => u.DeletedAt).SingleAsync())).Should().BeNull();
        (await InDbAsync(db => db.DataDeletionRequests.AsNoTracking().Where(d => d.UserId == customer.Id).Select(d => d.Status).SingleAsync())).Should().Be(DataDeletionStatus.Cancelled);
    }

    private async Task<(Guid GroupId, Account Owner, Account Member)> GroupAsync()
    {
        var owner = await AccountAsync(UserRole.User);
        var member = await AccountAsync(UserRole.User);
        var groupId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InDbAsync(async db =>
        {
            db.Groups.Add(new Group { Id = groupId, Name = "Gia đình", Type = GroupType.Family, TransactionVisibility = GroupTransactionVisibility.Full, CreatedByUserId = owner.Id, CreatedAt = now, UpdatedAt = now });
            db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = groupId, UserId = owner.Id, Role = GroupRole.Owner, JoinedAt = now, UpdatedAt = now });
            db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = groupId, UserId = member.Id, Role = GroupRole.Member, JoinedAt = now, UpdatedAt = now });
            return await db.SaveChangesAsync();
        });
        return (groupId, owner, member);
    }

    [Fact]
    public async Task ApprovedGroupOwnerTransferMovesOwnershipAndKeepsExactlyOneOwner()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var (groupId, owner, member) = await GroupAsync();

        var id = (await DataAsync(await SendAsync(HttpMethod.Post, Approvals, staff.Token, new OpsProposeApprovalRequest(OpsApprovalType.TransferGroupOwner, groupId, member.Id, null, "Chủ nhóm mất liên lạc")))).GetProperty("id").GetGuid();
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Approvals}/{id}/approve", manager.Token, new OpsApprovalDecisionRequest(null)));

        var roles = await InDbAsync(db => db.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId).ToListAsync());
        roles.Single(m => m.UserId == member.Id).Role.Should().Be(GroupRole.Owner);
        roles.Single(m => m.UserId == owner.Id).Role.Should().Be(GroupRole.Manager);

        // Nhân viên không phải thành viên nên không hiện tên trong lịch sử nhóm.
        var activity = await InDbAsync(db => db.GroupActivities.AsNoTracking().SingleAsync(a => a.GroupId == groupId && a.Type == GroupActivityType.OwnershipTransferred));
        activity.ActorUserId.Should().BeNull();
    }

    [Fact]
    public async Task GroupTransferRefusesANonMemberOrTheCurrentOwner()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var (groupId, owner, _) = await GroupAsync();
        var stranger = await AccountAsync(UserRole.User);

        foreach (var target in new[] { stranger.Id, owner.Id })
        {
            (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, Approvals, staff.Token, new OpsProposeApprovalRequest(OpsApprovalType.TransferGroupOwner, groupId, target, null, "x")))).Should().Be(OpsErrorCodes.InvalidTarget);
        }
    }

    [Fact]
    public async Task GroupViewRequiresARelatedTicketAndShowsNoMoney()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var (groupId, _, member) = await GroupAsync();
        var outsider = await AccountAsync(UserRole.User);
        var relatedTicket = await TicketAsync(member, SupportTicketCategory.Group);
        var unrelatedTicket = await TicketAsync(outsider, SupportTicketCategory.Group);

        // Chưa phụ trách ca.
        (await SendAsync(HttpMethod.Get, $"/api/v1/ops/groups/{groupId}?ticketId={relatedTicket}", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await ClaimAsync(staff, relatedTicket);
        await ClaimAsync(staff, unrelatedTicket);

        // Người tạo ca không thuộc nhóm.
        (await SendAsync(HttpMethod.Get, $"/api/v1/ops/groups/{groupId}?ticketId={unrelatedTicket}", staff.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/ops/groups/{groupId}?ticketId={relatedTicket}", staff.Token);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("Gia đình");
        text.ToLowerInvariant().Should().NotContain("cents").And.NotContain("amount").And.NotContain("subjectid");
    }

    [Fact]
    public async Task EmergencyLockOnlyHitsTheRequesterOfATicketYouHandle_AndEscalatesIt()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var other = await AccountAsync(UserRole.Staff);
        var customer = await AccountAsync(UserRole.User);
        var bystander = await AccountAsync(UserRole.User);
        var ticket = await TicketAsync(customer);
        var lockUrl = $"/api/v1/ops/users/{customer.Id}/emergency-lock";

        (await SendAsync(HttpMethod.Post, lockUrl, staff.Token, new OpsEmergencyLockRequest(ticket, "bị chiếm"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await ClaimAsync(staff, ticket);
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/ops/users/{bystander.Id}/emergency-lock", staff.Token, new OpsEmergencyLockRequest(ticket, "nhầm")))).Should().Be(OpsErrorCodes.InvalidTarget);
        (await IsLockedAsync(bystander.Id)).Should().BeFalse();
        (await SendAsync(HttpMethod.Post, lockUrl, other.Token, new OpsEmergencyLockRequest(ticket, "bị chiếm"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await DataAsync(await SendAsync(HttpMethod.Post, lockUrl, staff.Token, new OpsEmergencyLockRequest(ticket, "bị chiếm")));

        (await IsLockedAsync(customer.Id)).Should().BeTrue();
        var row = await InDbAsync(db => db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == ticket));
        row.Status.Should().Be(SupportTicketStatus.Escalated);
    }

    [Fact]
    public async Task EmergencyLockCannotTargetAnInternalAccount()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var colleague = await AccountAsync(UserRole.Staff);
        var ticket = await TicketAsync(colleague);
        await ClaimAsync(staff, ticket);

        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"/api/v1/ops/users/{colleague.Id}/emergency-lock", staff.Token, new OpsEmergencyLockRequest(ticket, "x")))).Should().Be(OpsErrorCodes.InvalidTarget);
        (await IsLockedAsync(colleague.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task UserViewIsMinimalAndScopedToTheCase()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var customer = await AccountAsync(UserRole.User);
        var ticket = await TicketAsync(customer);

        (await SendAsync(HttpMethod.Get, $"/api/v1/ops/users/{customer.Id}", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, $"/api/v1/ops/users/{customer.Id}", manager.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await ClaimAsync(staff, ticket);
        var data = await DataAsync(await SendAsync(HttpMethod.Get, $"/api/v1/ops/users/{customer.Id}", staff.Token));

        data.GetProperty("maskedEmail").GetString().Should().StartWith("kh***@");
        data.ToString().Should().NotContain("cents", because: "không có dữ liệu tài chính");
    }

    [Fact]
    public async Task ManagerToolsAreClosedToStaff()
    {
        var staff = await AccountAsync(UserRole.Staff);

        foreach (var url in new[] { "/api/v1/ops/dashboard", "/api/v1/ops/audit-logs", "/api/v1/ops/ai-stats" })
        {
            (await SendAsync(HttpMethod.Get, url, staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
        }

        (await SendAsync(HttpMethod.Post, "/api/v1/ops/canned-replies", staff.Token, new OpsCannedReplyRequest("a", "b"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ManagerAuditViewOnlyShowsOpsEvents()
    {
        var manager = await AccountAsync(UserRole.OpsManager);
        var staff = await AccountAsync(UserRole.Staff);
        var customer = await AccountAsync(UserRole.User, u => u.IsLocked = true);
        await ProposeUnlockAsync(staff, customer.Id);

        var data = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/ops/audit-logs?limit=100", manager.Token));
        var events = data.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("eventType").GetString()!).ToList();
        events.Should().Contain("Ops.Approval.Proposed").And.OnlyContain(e => e.StartsWith("Ops."));

        (await SendAsync(HttpMethod.Get, "/api/v1/ops/audit-logs?eventType=Auth.User.Registered", manager.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CannedRepliesAreManagedByManagersAndReadableByStaff()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);

        var id = (await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/ops/canned-replies", manager.Token, new OpsCannedReplyRequest("Lỗi OCR", "Chúng tôi đang xử lý")))).GetProperty("id").GetGuid();
        (await (await SendAsync(HttpMethod.Get, "/api/v1/ops/canned-replies", staff.Token)).Content.ReadAsStringAsync()).Should().Contain(id.ToString());

        await DataAsync(await SendAsync(HttpMethod.Patch, $"/api/v1/ops/canned-replies/{id}", manager.Token, new OpsCannedReplyPatchRequest(null, null, false)));
        (await (await SendAsync(HttpMethod.Get, "/api/v1/ops/canned-replies", staff.Token)).Content.ReadAsStringAsync()).Should().NotContain(id.ToString());
    }

    [Fact]
    public async Task WideOutage_ManagerLinksTicketsAndAnswersThemAllAtOnce()
    {
        var manager = await AccountAsync(UserRole.OpsManager);
        var staff = await AccountAsync(UserRole.Staff);
        var (a, b, c) = (await AccountAsync(UserRole.User), await AccountAsync(UserRole.User), await AccountAsync(UserRole.User));
        var parent = await TicketAsync(a, SupportTicketCategory.Receipt);
        var child1 = await TicketAsync(b, SupportTicketCategory.Receipt);
        var child2 = await TicketAsync(c, SupportTicketCategory.Receipt);

        (await SendAsync(HttpMethod.Post, $"{OpsTickets}/{parent}/children", staff.Token, new OpsTicketLinkRequest([child1, child2]))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"{OpsTickets}/{parent}/broadcast", staff.Token, new OpsTicketBroadcastRequest("x", false))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await DataAsync(await SendAsync(HttpMethod.Post, $"{OpsTickets}/{parent}/children", manager.Token, new OpsTicketLinkRequest([child1, child2])));

        var filtered = await (await SendAsync(HttpMethod.Get, $"{OpsTickets}?parentTicketId={parent}", manager.Token)).Content.ReadAsStringAsync();
        filtered.Should().Contain(child1.ToString()).And.Contain(child2.ToString());

        // Ca cha không gom vào ca khác được: chỉ một tầng.
        var other = await TicketAsync(await AccountAsync(UserRole.User));
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{OpsTickets}/{other}/children", manager.Token, new OpsTicketLinkRequest([parent])))).Should().Be(SupportErrorCodes.InvalidLink);

        var result = await DataAsync(await SendAsync(HttpMethod.Post, $"{OpsTickets}/{parent}/broadcast", manager.Token, new OpsTicketBroadcastRequest("Đã khắc phục lỗi đọc hóa đơn", true)));
        result.GetProperty("count").GetInt32().Should().Be(3);

        foreach (var (customer, ticket) in new[] { (a, parent), (b, child1), (c, child2) })
        {
            var view = await DataAsync(await SendAsync(HttpMethod.Get, $"{Tickets}/{ticket}", customer.Token));
            view.GetProperty("ticket").GetProperty("status").GetString().Should().Be("Resolved");
            view.ToString().Should().Contain("Đã khắc phục lỗi đọc hóa đơn");
        }
    }

    [Fact]
    public async Task DashboardCountsMatchTheQueue()
    {
        // Lớp này dùng chung DB nên so sánh chênh lệch, không so số tuyệt đối.
        var manager = await AccountAsync(UserRole.OpsManager);
        var staff = await AccountAsync(UserRole.Staff);

        async Task<JsonElement> DashboardAsync() => await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/ops/dashboard", manager.Token));
        var before = await DashboardAsync();

        var customer = await AccountAsync(UserRole.User);
        var assigned = await TicketAsync(customer);
        await TicketAsync(customer);
        await ClaimAsync(staff, assigned);
        await InDbAsync(db => db.SupportTickets.Where(t => t.Id == assigned).ExecuteUpdateAsync(s => s.SetProperty(t => t.ResolveDueAt, DateTimeOffset.UtcNow.AddMinutes(-1))));

        var after = await DashboardAsync();

        after.GetProperty("unassigned").GetInt32().Should().Be(before.GetProperty("unassigned").GetInt32() + 1);
        after.GetProperty("overdue").GetInt32().Should().Be(before.GetProperty("overdue").GetInt32() + 1);
        after.GetProperty("recentCount").GetInt32().Should().Be(before.GetProperty("recentCount").GetInt32() + 2);

        var load = after.GetProperty("load").EnumerateArray().Single(l => l.GetProperty("userId").GetGuid() == staff.Id);
        load.GetProperty("openCount").GetInt32().Should().Be(1);
        load.GetProperty("overdueCount").GetInt32().Should().Be(1);
    }
}
