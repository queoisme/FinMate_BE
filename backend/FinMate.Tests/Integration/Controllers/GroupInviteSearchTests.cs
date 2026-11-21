using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Tìm người dùng theo email / FinMate ID, gợi ý mời, link mời công khai và thu hồi tất cả lời mời.</summary>
[Collection("Integration")]
public class GroupInviteSearchTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GroupInviteSearchTests(AuthApiFactory factory)
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
    private record MeData(Guid Id, string Email, string FinMateId);
    private record IdData(Guid Id);
    private record InviteData(Guid Id, string? Token, string? JoinUrl);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);
    private record TestUser(Guid Id, string Email, string FinMateId, string Token);
    private record SearchHit(Guid UserId, string DisplayName, string MaskedEmail, string FinMateId);
    private record Suggestion(Guid UserId, string DisplayName, string FinMateId, int SharedGroupCount);
    private record RevokedData(int Revoked);
    private record JoinLinkData(string Code, string JoinUrl, string Role, bool Enabled);
    private record PreviewData(string GroupName, string Role, DateTimeOffset? ExpiresAt, int MemberCount);

    private async Task<TestUser> NewUserAsync(string name = "Search Tester")
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", name));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        var token = (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
        var me = await ReadAsync<MeData>(HttpMethod.Get, "/api/v1/users/me", token);
        return new TestUser(me.Id, email, me.FinMateId, token);
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

    private async Task VerifyEmailAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerifiedAt, DateTimeOffset.UtcNow));
    }

    private Task<IdData> NewGroupAsync(TestUser owner, string name = "Nhóm tìm người")
        => ReadAsync<IdData>(HttpMethod.Post, "/api/v1/groups", owner.Token, new { name, type = "Friends" });

    private async Task JoinAsync(TestUser owner, TestUser user, Guid groupId)
    {
        var token = (await ReadAsync<InviteData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/invitations", owner.Token, new { role = "Member" })).Token;
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", user.Token, new { token });
    }

    [Fact]
    public async Task SearchMatchesExactlyHidesMembersAndMasksTheEmail()
    {
        var owner = await NewUserAsync();
        var target = await NewUserAsync("Nguyễn Văn B");
        var group = await NewGroupAsync(owner);

        target.FinMateId.Should().MatchRegex("^FM-[A-Z0-9]{8}$");
        var byId = await ReadAsync<List<SearchHit>>(HttpMethod.Get, $"/api/v1/users/search?q={target.FinMateId.ToLowerInvariant()}&groupId={group.Id}", owner.Token);
        byId.Should().ContainSingle(h => h.UserId == target.Id && h.DisplayName == "Nguyễn Văn B");
        byId[0].MaskedEmail.Should().Be($"{target.Email[0]}***@finmate.local");

        // Email chỉ khớp khi tài khoản đã xác minh; khớp từng phần thì không ra gì.
        (await ReadAsync<List<SearchHit>>(HttpMethod.Get, $"/api/v1/users/search?q={target.Email}", owner.Token)).Should().BeEmpty();
        await VerifyEmailAsync(target.Id);
        (await ReadAsync<List<SearchHit>>(HttpMethod.Get, $"/api/v1/users/search?q={target.Email}", owner.Token)).Should().ContainSingle(h => h.UserId == target.Id);
        (await ReadAsync<List<SearchHit>>(HttpMethod.Get, $"/api/v1/users/search?q={target.Email[..10]}", owner.Token)).Should().BeEmpty();
        (await ReadAsync<List<SearchHit>>(HttpMethod.Get, $"/api/v1/users/search?q={target.FinMateId[..6]}", owner.Token)).Should().BeEmpty();
        (await ReadAsync<List<SearchHit>>(HttpMethod.Get, "/api/v1/users/search?q=0912345678", owner.Token)).Should().BeEmpty();   // số điện thoại chưa hỗ trợ
        (await ReadAsync<List<SearchHit>>(HttpMethod.Get, $"/api/v1/users/search?q={owner.FinMateId}", owner.Token)).Should().BeEmpty();   // chính mình

        // Đã trong nhóm thì không trả.
        await JoinAsync(owner, target, group.Id);
        (await ReadAsync<List<SearchHit>>(HttpMethod.Get, $"/api/v1/users/search?q={target.FinMateId}&groupId={group.Id}", owner.Token)).Should().BeEmpty();

        await ExpectAsync(HttpMethod.Get, "/api/v1/users/search?q=ab", owner.Token, HttpStatusCode.BadRequest);
        await ExpectAsync(HttpMethod.Get, $"/api/v1/users/search?q={owner.FinMateId}&groupId={group.Id}", target.Token, HttpStatusCode.Forbidden);   // Member không có quyền mời
    }

    [Fact]
    public async Task SearchIsRateLimitedPerCaller()
    {
        var caller = await NewUserAsync();
        var other = await NewUserAsync();

        for (var i = 0; i < 20; i++)
        {
            await ReadAsync<List<SearchHit>>(HttpMethod.Get, $"/api/v1/users/search?q=FM-ZZZZ{i:0000}", caller.Token);
        }

        await ExpectAsync(HttpMethod.Get, "/api/v1/users/search?q=FM-ZZZZ9999", caller.Token, (HttpStatusCode)429, code: "USER_SEARCH_RATE_LIMITED");
        await ReadAsync<List<SearchHit>>(HttpMethod.Get, "/api/v1/users/search?q=FM-ZZZZ9999", other.Token);   // giới hạn theo từng người gọi
    }

    [Fact]
    public async Task LinkInvitationsCarryAPublicUrlAndCanBeRevokedAtOnce()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);

        var link = await ReadAsync<InviteData>(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", owner.Token, new { role = "Member" });
        link.JoinUrl.Should().Be($"https://finmate.app/join/{link.Token}");
        await ReadAsync<InviteData>(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", owner.Token, new { role = "Member" });
        await ReadAsync<InviteData>(HttpMethod.Post, $"/api/v1/groups/{group.Id}/invitations", owner.Token, new { email = "nguoi.nhan@gmail.com" });

        (await ReadAsync<RevokedData>(HttpMethod.Delete, $"/api/v1/groups/{group.Id}/invitations", owner.Token)).Revoked.Should().Be(3);
        (await ReadAsync<List<InviteData>>(HttpMethod.Get, $"/api/v1/groups/{group.Id}/invitations", owner.Token)).Should().BeEmpty();
        (await ReadAsync<RevokedData>(HttpMethod.Delete, $"/api/v1/groups/{group.Id}/invitations", owner.Token)).Revoked.Should().Be(0);
        var someone = await NewUserAsync();
        await ExpectAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", someone.Token, (HttpStatusCode)422, new { token = link.Token }, "GROUP_INVITATION_INVALID");
    }

    [Fact]
    public async Task TheGroupJoinCodeIsAlwaysThereNeverExpiresAndWorksForEveryone()
    {
        var owner = await NewUserAsync();
        var group = await NewGroupAsync(owner);
        var a = await NewUserAsync();
        var b = await NewUserAsync();
        var c = await NewUserAsync();
        var url = $"/api/v1/groups/{group.Id}/join-link";

        // Mở lần đầu là có luôn, không phải bấm tạo; mở lại ra đúng mã đó.
        var link = await ReadAsync<JoinLinkData>(HttpMethod.Get, url, owner.Token);
        link.JoinUrl.Should().Be($"https://finmate.app/join/{link.Code}");
        link.Role.Should().Be("Member");
        link.Enabled.Should().BeTrue();
        (await ReadAsync<JoinLinkData>(HttpMethod.Get, url, owner.Token)).Code.Should().Be(link.Code);

        // Người xem trước thấy nhóm, không có hạn; dùng được cho nhiều người.
        var preview = await ReadAsync<PreviewData>(HttpMethod.Post, "/api/v1/group-invitations/preview", a.Token, new { token = link.Code });
        preview.GroupName.Should().Be("Nhóm tìm người");
        preview.Role.Should().Be("Member");
        preview.ExpiresAt.Should().BeNull();
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", a.Token, new { token = link.Code });
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", b.Token, new { token = link.Code });
        await ExpectAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", b.Token, HttpStatusCode.Conflict, new { token = link.Code }, "GROUP_ALREADY_MEMBER");

        // Thành viên thường không xem/quản lý được mã.
        await ExpectAsync(HttpMethod.Get, url, a.Token, HttpStatusCode.Forbidden);
        await ExpectAsync(HttpMethod.Post, $"{url}/reset", a.Token, HttpStatusCode.Forbidden);

        // Tắt thì không ai vào được; bật lại dùng đúng mã cũ.
        (await ReadAsync<JoinLinkData>(HttpMethod.Put, url, owner.Token, new { enabled = false })).Enabled.Should().BeFalse();
        await ExpectAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", c.Token, (HttpStatusCode)422, new { token = link.Code }, "GROUP_INVITATION_INVALID");
        (await ReadAsync<JoinLinkData>(HttpMethod.Put, url, owner.Token, new { enabled = true })).Code.Should().Be(link.Code);

        // Đặt lại: mã cũ chết, mã mới vào được. Thu hồi lời mời không đụng tới mã chung.
        await ReadAsync<RevokedData>(HttpMethod.Delete, $"/api/v1/groups/{group.Id}/invitations", owner.Token);
        var reset = await ReadAsync<JoinLinkData>(HttpMethod.Post, $"{url}/reset", owner.Token);
        reset.Code.Should().NotBe(link.Code);
        await ExpectAsync(HttpMethod.Post, "/api/v1/group-invitations/accept", c.Token, (HttpStatusCode)422, new { token = link.Code }, "GROUP_INVITATION_INVALID");
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", c.Token, new { token = reset.Code });
    }

    [Fact]
    public async Task SuggestionsAreCoMembersOfOtherGroupsNotAlreadyHere()
    {
        var owner = await NewUserAsync();
        var friend = await NewUserAsync("Bạn cũ");
        var first = await NewGroupAsync(owner, "Nhóm cũ");
        await JoinAsync(owner, friend, first.Id);

        var second = await NewGroupAsync(owner, "Nhóm mới");
        (await ReadAsync<List<Suggestion>>(HttpMethod.Get, $"/api/v1/groups/{second.Id}/invite-suggestions", owner.Token))
            .Should().ContainSingle(s => s.UserId == friend.Id && s.SharedGroupCount == 1);

        await JoinAsync(owner, friend, second.Id);
        (await ReadAsync<List<Suggestion>>(HttpMethod.Get, $"/api/v1/groups/{second.Id}/invite-suggestions", owner.Token)).Should().BeEmpty();   // đã trong nhóm

        await ExpectAsync(HttpMethod.Get, $"/api/v1/groups/{first.Id}/invite-suggestions", NewOutsiderToken(await NewUserAsync()), HttpStatusCode.NotFound);   // không phải thành viên
    }

    private static string NewOutsiderToken(TestUser user) => user.Token;
}
