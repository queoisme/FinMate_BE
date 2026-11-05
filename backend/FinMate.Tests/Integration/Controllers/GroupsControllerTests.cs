using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Nhóm, thành viên, lời mời và phân quyền qua API thật trên Postgres thật.</summary>
[Collection("Integration")]
public class GroupsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly AuthApiFactory _factory;
    private readonly CapturingEmailSender _email = new();
    private readonly HttpClient _client;

    public GroupsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_email);
        })).CreateClient();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken, UserData? User);
    private record UserData(Guid Id);
    private record MeData(Guid Id);
    private record GroupData(Guid Id, string Name, string MyRole, List<string> MyPermissions, int MemberCount, string TransactionVisibility);
    private record MemberData(Guid UserId, string DisplayName, string Role, bool IsActive, List<string>? Permissions);
    private record InvitationData(Guid Id, string? Email, string Role, string? Token, bool? EmailSent);
    private record PreviewData(string GroupName, string Role, string? InvitedBy);
    private record ActivityData(string Type, Guid? ActorUserId, Guid? SubjectId);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);
    private record TestUser(Guid Id, string Email, string Token);

    private async Task<TestUser> NewUserAsync(string name = "Group Test User")
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", name));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        var token = (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
        var me = await ReadAsync<MeData>(HttpMethod.Get, "/api/v1/users/me", token);
        return new TestUser(me.Id, email, token);
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

    private Task<GroupData> NewGroupAsync(TestUser owner, string name = "Gia đình")
        => ReadAsync<GroupData>(HttpMethod.Post, "/api/v1/groups", owner.Token, new { name, type = "Family" });

    private async Task<string> LinkInviteAsync(TestUser inviter, Guid groupId, string role = "Member")
        => (await ReadAsync<InvitationData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/invitations", inviter.Token, new { role })).Token!;

    private async Task<TestUser> JoinAsync(TestUser owner, Guid groupId, string role = "Member")
    {
        var user = await NewUserAsync();
        var token = await LinkInviteAsync(owner, groupId, role);
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", user.Token, new { token });
        return user;
    }

    private async Task MarkEmailVerifiedAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerifiedAt, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task TheCreatorBecomesOwnerWithEveryPermission()
    {
        var owner = await NewUserAsync();

        var group = await NewGroupAsync(owner);

        group.MyRole.Should().Be("Owner");
        group.MemberCount.Should().Be(1);
        group.MyPermissions.Should().Contain(["ManageBudgets", "InviteMembers", "RemoveMembers"]);
        (await ReadAsync<List<GroupData>>(HttpMethod.Get, "/api/v1/groups", owner.Token)).Should().ContainSingle(g => g.Id == group.Id);
    }

    [Fact]
    public async Task ANonMemberGets404_NotProofTheGroupExists()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();
        var group = await NewGroupAsync(owner);

        await ExpectAsync(HttpMethod.Get, $"/api/v1/groups/{group.Id}", stranger.Token, HttpStatusCode.NotFound);
        await ExpectAsync(HttpMethod.Get, $"/api/v1/groups/{group.Id}/members", stranger.Token, HttpStatusCode.NotFound);
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", stranger.Token, HttpStatusCode.NotFound, new { role = "Member" });
        (await ReadAsync<List<GroupData>>(HttpMethod.Get, "/api/v1/groups", stranger.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task AMemberWithoutThePermissionGets403()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var member = await JoinAsync(owner, group.Id);
        var other = await JoinAsync(owner, group.Id);

        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", member.Token, HttpStatusCode.Forbidden, new { role = "Viewer" });
        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{group.Id}/members/{other.Id}", member.Token, HttpStatusCode.Forbidden);
        await ExpectAsync(HttpMethod.Patch, $"/api/v1/groups/{group.Id}", member.Token, HttpStatusCode.Forbidden, new { name = "Của tôi" });
        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{group.Id}", member.Token, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task APerPersonGrantAndRevokeTakeEffectImmediately()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var member = await JoinAsync(owner, group.Id);

        await ReadAsync<MemberData>(HttpMethod.Patch, $"/api/v1/groups/{group.Id}/members/{member.Id}", owner.Token, new { grantedPermissions = new[] { "InviteMembers" } });
        await LinkInviteAsync(member, group.Id, "Viewer");

        await ReadAsync<MemberData>(HttpMethod.Patch, $"/api/v1/groups/{group.Id}/members/{member.Id}", owner.Token, new { grantedPermissions = Array.Empty<string>(), revokedPermissions = new[] { "ViewActivity" } });
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", member.Token, HttpStatusCode.Forbidden, new { role = "Viewer" });
        await ExpectAsync(HttpMethod.Get, $"/api/v1/groups/{group.Id}/activities", member.Token, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangingARoleClearsOldOverrides()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var member = await JoinAsync(owner, group.Id);
        await ReadAsync<MemberData>(HttpMethod.Patch, $"/api/v1/groups/{group.Id}/members/{member.Id}", owner.Token, new { grantedPermissions = new[] { "ManageBudgets" } });

        var demoted = await ReadAsync<MemberData>(HttpMethod.Patch, $"/api/v1/groups/{group.Id}/members/{member.Id}", owner.Token, new { role = "Viewer" });

        demoted.Role.Should().Be("Viewer");
        demoted.Permissions.Should().NotContain("ManageBudgets");
    }

    [Fact]
    public async Task OwnerOnlyActionsCannotBeGrantedOrReachedByAManager()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var manager = await JoinAsync(owner, group.Id, "Manager");
        var member = await JoinAsync(owner, group.Id);

        await ExpectAsync(HttpMethod.Patch, $"/api/v1/groups/{group.Id}/members/{member.Id}", manager.Token, HttpStatusCode.Forbidden, new { role = "Viewer" });
        await ExpectAsync(HttpMethod.Patch, $"/api/v1/groups/{group.Id}", manager.Token, HttpStatusCode.Forbidden, new { transactionVisibility = "Summary" });
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/transfer-ownership", manager.Token, HttpStatusCode.Forbidden, new { newOwnerUserId = manager.Id });
        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{group.Id}", manager.Token, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AManagerOnlyActsOnLowerRoles()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var manager = await JoinAsync(owner, group.Id, "Manager");
        var otherManager = await JoinAsync(owner, group.Id, "Manager");
        var member = await JoinAsync(owner, group.Id);

        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{group.Id}/members/{owner.Id}", manager.Token, HttpStatusCode.Forbidden);
        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{group.Id}/members/{otherManager.Id}", manager.Token, HttpStatusCode.Forbidden);
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", manager.Token, HttpStatusCode.Forbidden, new { role = "Manager" });

        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{group.Id}/members/{member.Id}", manager.Token, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task NobodyChangesTheirOwnRoleOrAssignsOwnerDirectly()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var member = await JoinAsync(owner, group.Id);

        await ExpectAsync(HttpMethod.Patch, $"/api/v1/groups/{group.Id}/members/{owner.Id}", owner.Token, (HttpStatusCode)422, new { role = "Member" });
        await ExpectAsync(HttpMethod.Patch, $"/api/v1/groups/{group.Id}/members/{member.Id}", owner.Token, (HttpStatusCode)422, new { role = "Owner" });
        await ExpectAsync(HttpMethod.Patch, $"/api/v1/groups/{group.Id}/members/{member.Id}", member.Token, HttpStatusCode.Forbidden, new { role = "Manager" });
    }

    [Fact]
    public async Task ARemovedMemberLosesAccessOnTheNextRequest()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var member = await JoinAsync(owner, group.Id);
        await ReadAsync<GroupData>(HttpMethod.Get, $"/api/v1/groups/{group.Id}", member.Token);

        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{group.Id}/members/{member.Id}", owner.Token, HttpStatusCode.NoContent);

        await ExpectAsync(HttpMethod.Get, $"/api/v1/groups/{group.Id}", member.Token, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheOwnerMustTransferBeforeLeaving_AndTransferKeepsExactlyOneOwner()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var member = await JoinAsync(owner, group.Id);

        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/leave", owner.Token, (HttpStatusCode)422);

        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/transfer-ownership", owner.Token, HttpStatusCode.NoContent, new { newOwnerUserId = member.Id });

        var members = await ReadAsync<List<MemberData>>(HttpMethod.Get, $"/api/v1/groups/{group.Id}/members", member.Token);
        members.Should().ContainSingle(m => m.Role == "Owner").Which.UserId.Should().Be(member.Id);
        members.Single(m => m.UserId == owner.Id).Role.Should().Be("Manager");

        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/leave", owner.Token, HttpStatusCode.NoContent);
        await ExpectAsync(HttpMethod.Get, $"/api/v1/groups/{group.Id}", owner.Token, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AMemberWhoLeftCanRejoinWithoutOldOverrides()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var member = await JoinAsync(owner, group.Id);
        await ReadAsync<MemberData>(HttpMethod.Patch, $"/api/v1/groups/{group.Id}/members/{member.Id}", owner.Token, new { grantedPermissions = new[] { "ManageBudgets" } });
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/leave", member.Token, HttpStatusCode.NoContent);

        var token = await LinkInviteAsync(owner, group.Id);
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", member.Token, new { token });

        (await ReadAsync<GroupData>(HttpMethod.Get, $"/api/v1/groups/{group.Id}", member.Token)).MyPermissions.Should().NotContain("ManageBudgets");
    }

    [Fact]
    public async Task ALinkInvitationWorksOnce_AndIsNeverReturnedAgain()
    {
        var owner = await NewUserAsync();
        var first = await NewUserAsync();
        var second = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var token = await LinkInviteAsync(owner, group.Id, "Viewer");

        var pending = await ReadAsync<List<InvitationData>>(HttpMethod.Get, $"/api/v1/groups/{group.Id}/invitations", owner.Token);
        pending.Should().ContainSingle().Which.Token.Should().BeNull();

        var preview = await ReadAsync<PreviewData>(HttpMethod.Post, "/api/v1/group-invitations/preview", first.Token, new { token });
        preview.GroupName.Should().Be("Gia đình");
        preview.Role.Should().Be("Viewer");

        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", first.Token, new { token });
        (await ReadAsync<GroupData>(HttpMethod.Get, $"/api/v1/groups/{group.Id}", first.Token)).MyRole.Should().Be("Viewer");

        await ExpectAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", second.Token, (HttpStatusCode)422, new { token }, "GROUP_INVITATION_INVALID");
        (await ReadAsync<List<InvitationData>>(HttpMethod.Get, $"/api/v1/groups/{group.Id}/invitations", owner.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task ARevokedOrUnknownOrExpiredInvitationAllGiveTheSameError()
    {
        var owner = await NewUserAsync();
        var user = await NewUserAsync();
        var group = await NewGroupAsync(owner);

        var revoked = await ReadAsync<InvitationData>(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", owner.Token, new { role = "Member" });
        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{group.Id}/invitations/{revoked.Id}", owner.Token, HttpStatusCode.NoContent);

        var expired = await ReadAsync<InvitationData>(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", owner.Token, new { role = "Member" });
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.GroupInvitations.Where(i => i.Id == expired.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        foreach (var token in new[] { revoked.Token!, expired.Token!, "khong-co-ma-nay" })
        {
            await ExpectAsync(HttpMethod.Post, "/api/v1/group-invitations/preview", user.Token, (HttpStatusCode)422, new { token }, "GROUP_INVITATION_INVALID");
            await ExpectAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", user.Token, (HttpStatusCode)422, new { token }, "GROUP_INVITATION_INVALID");
        }
    }

    [Fact]
    public async Task AnEmailInvitationOnlyWorksForThatVerifiedAddress()
    {
        var owner = await NewUserAsync();
        var invited = await NewUserAsync();
        var someoneElse = await NewUserAsync();
        var group = await NewGroupAsync(owner);

        var created = await ReadAsync<InvitationData>(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", owner.Token, new { email = invited.Email.ToUpperInvariant(), role = "Member" });
        created.Token.Should().BeNull();
        created.EmailSent.Should().BeTrue();

        var token = _email.BodyFor(invited.Email)!.Split('\n', StringSplitOptions.RemoveEmptyEntries)[2].Trim();

        await ExpectAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", someoneElse.Token, (HttpStatusCode)422, new { token }, "GROUP_INVITATION_INVALID");
        await ExpectAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", invited.Token, (HttpStatusCode)422, new { token }, "GROUP_INVITATION_INVALID");

        await MarkEmailVerifiedAsync(invited.Id);
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", invited.Token, new { token });
    }

    [Fact]
    public async Task InvitingAnAddressAnswersTheSameWhetherOrNotItHasAnAccount()
    {
        var owner = await NewUserAsync();
        var existing = await NewUserAsync();
        var group = await NewGroupAsync(owner);

        var forExisting = await SendAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", owner.Token, new { email = existing.Email, role = "Member" });
        var forUnknown = await SendAsync(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", owner.Token, new { email = $"{Guid.NewGuid():N}@finmate.local", role = "Member" });

        forExisting.StatusCode.Should().Be(forUnknown.StatusCode);
        var shape = async (HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("data").EnumerateObject().Select(p => p.Name).ToList();
        (await shape(forExisting)).Should().BeEquivalentTo(await shape(forUnknown));
    }

    [Fact]
    public async Task TwoPeopleRacingForOneInvitation_ExactlyOneGetsIn()
    {
        var owner = await NewUserAsync();
        var first = await NewUserAsync();
        var second = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var token = await LinkInviteAsync(owner, group.Id);

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", first.Token, new { token }),
            SendAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", second.Token, new { token }));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict || s == (HttpStatusCode)422);
        (await ReadAsync<GroupData>(HttpMethod.Get, $"/api/v1/groups/{group.Id}", owner.Token)).MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task ADeletedGroupDisappearsAndItsInvitationsStopWorking()
    {
        var owner = await NewUserAsync();
        var user = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var token = await LinkInviteAsync(owner, group.Id);

        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{group.Id}", owner.Token, HttpStatusCode.NoContent);

        await ExpectAsync(HttpMethod.Get, $"/api/v1/groups/{group.Id}", owner.Token, HttpStatusCode.NotFound);
        (await ReadAsync<List<GroupData>>(HttpMethod.Get, "/api/v1/groups", owner.Token)).Should().BeEmpty();
        await ExpectAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", user.Token, (HttpStatusCode)422, new { token }, "GROUP_INVITATION_INVALID");
    }

    [Fact]
    public async Task ChangesLandInTheActivityFeedAndAuditLog()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var member = await JoinAsync(owner, group.Id);
        await ReadAsync<MemberData>(HttpMethod.Patch, $"/api/v1/groups/{group.Id}/members/{member.Id}", owner.Token, new { role = "Viewer" });

        var feed = await ReadAsync<List<ActivityData>>(HttpMethod.Get, $"/api/v1/groups/{group.Id}/activities", owner.Token);
        feed.Select(a => a.Type).Should().Contain(["GroupCreated", "InvitationCreated", "MemberJoined", "MemberUpdated"]);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.AuditLogs.AnyAsync(a => a.UserId == owner.Id && a.EventType == "Group.Member.Updated")).Should().BeTrue();
    }

    [Fact]
    public async Task MembersSeeNamesButOnlyTheOwnerSeesOtherPeoplesPermissions()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var member = await JoinAsync(owner, group.Id);

        var seenByMember = await ReadAsync<List<MemberData>>(HttpMethod.Get, $"/api/v1/groups/{group.Id}/members", member.Token);
        seenByMember.Single(m => m.UserId == owner.Id).Permissions.Should().BeNull();
        seenByMember.Single(m => m.UserId == member.Id).Permissions.Should().NotBeNull();

        var seenByOwner = await ReadAsync<List<MemberData>>(HttpMethod.Get, $"/api/v1/groups/{group.Id}/members", owner.Token);
        seenByOwner.Single(m => m.UserId == member.Id).Permissions.Should().NotBeNull();
    }
}
