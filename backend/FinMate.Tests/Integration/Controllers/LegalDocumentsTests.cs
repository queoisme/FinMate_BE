using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.API.Controllers.Admin;
using FinMate.Application.Legal;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Văn bản pháp lý: đọc công khai, Manager soạn nháp, Admin xuất bản. Các test dùng chung một DB nên mỗi test gọi
/// <see cref="ResetAsync"/> trước: xóa nháp và mọi bản sau bản 4 (bản seed của migration), để kết quả không phụ thuộc thứ tự chạy.
/// </summary>
[Collection("Integration")]
public class LegalDocumentsTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "Password123!";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public LegalDocumentsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Account(Guid Id, string Email, string Token);
    private record Envelope<T>(T Data);
    private record TokenData(string AccessToken);
    private record IdData(Guid Id);
    private record SectionBody(string Title, string Body);
    private record DocumentBody(int Version, DateTimeOffset UpdatedAt, List<SectionBody> Sections);

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        await db.LegalVersions.Where(v => v.Status == LegalVersionStatus.Draft || v.Version > 4).ExecuteDeleteAsync();
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string? token = null, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: Json);
        }

        return request;
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri?.AbsolutePath}: {body}");
        return JsonSerializer.Deserialize<Envelope<T>>(body, Json)!.Data;
    }

    private async Task<string> LoginAsync(string email, string password)
        => (await ReadAsync<TokenData>(await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password)))).AccessToken;

    private async Task<Account> RegisterAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, "Legal Test"));
        var token = await LoginAsync(email, Password);
        var id = (await ReadAsync<IdData>(await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", token)))).Id;
        return new Account(id, email, token);
    }

    private Task<HttpResponseMessage> SetRoleAsync(string adminToken, Guid userId, UserRole role)
        => _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/users/{userId}/role", adminToken, new SetUserRoleRequest(role, "kiểm thử")));

    private async Task<Account> AccountWithRoleAsync(UserRole role)
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var account = await RegisterAsync();
        (await SetRoleAsync(admin, account.Id, role)).StatusCode.Should().Be(HttpStatusCode.OK);
        return account with { Token = await LoginAsync(account.Email, Password) };
    }

    [Fact]
    public async Task Provider_OnFreshDatabase_ReturnsSeededVersion4()
    {
        await ResetAsync();
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ILegalVersionProvider>();
        var repository = scope.ServiceProvider.GetRequiredService<ILegalRepository>();

        (await provider.CurrentTermsVersionAsync()).Should().Be(4);

        var current = await repository.GetCurrentPublishedAsync(default);
        current.Should().NotBeNull();
        current!.TermsSections.Should().HaveCount(4);
        current.PrivacySections.Should().HaveCount(4);
    }

    [Fact]
    public async Task AnonymousCanReadTermsAndPrivacy()
    {
        await ResetAsync();

        var terms = await ReadAsync<DocumentBody>(await _client.GetAsync("/api/v1/legal/terms"));
        var privacy = await ReadAsync<DocumentBody>(await _client.GetAsync("/api/v1/legal/privacy"));

        foreach (var doc in new[] { terms, privacy })
        {
            doc.Version.Should().Be(4);
            doc.Sections.Should().HaveCount(4);
            doc.Sections.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.Title) && !string.IsNullOrWhiteSpace(s.Body));
        }

        terms.Sections.Should().NotEqual(privacy.Sections, "mỗi đường dẫn phải trả đúng văn bản của nó");
    }

    [Fact]
    public async Task LegalRoutes_AreReadOnly()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/legal/terms", new { });

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    // ---- Task 4: Manager soạn nháp ----

    private const string Ops = "/api/v1/ops/legal";

    private record DraftBody(bool IsDraft, DateTimeOffset? UpdatedAt, List<SectionBody> TermsSections, List<SectionBody> PrivacySections);
    private record VersionBody(int? Version, string Status, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt, Guid? PublishedBy);
    private record ErrorData(string Code);
    private record ErrorBody(ErrorData Error);

    private static object Section(string title, string? body = null) => new { title, body = body ?? $"Nội dung {title}" };

    private static object DraftPayload(DateTimeOffset? baseUpdatedAt = null, string marker = "A") => new
    {
        termsSections = new[] { Section($"T1 {marker}"), Section($"T2 {marker}") },
        privacySections = new[] { Section($"P1 {marker}"), Section($"P2 {marker}") },
        baseUpdatedAt,
    };

    private async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => JsonSerializer.Deserialize<ErrorBody>(await response.Content.ReadAsStringAsync(), Json)!.Error.Code;

    private Task<HttpResponseMessage> PutDraftAsync(string token, object payload)
        => _client.SendAsync(Request(HttpMethod.Put, $"{Ops}/draft", token, payload));

    private async Task<DraftBody> GetDraftAsync(string token)
        => await ReadAsync<DraftBody>(await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/draft", token)));

    [Fact]
    public async Task Manager_CanSaveAndReadDraft()
    {
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);

        var saved = await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, DraftPayload()));

        saved.IsDraft.Should().BeTrue();
        saved.UpdatedAt.Should().NotBeNull();
        var read = await GetDraftAsync(manager.Token);
        read.IsDraft.Should().BeTrue();
        read.TermsSections.Select(s => s.Title).Should().Equal("T1 A", "T2 A");
        read.PrivacySections.Select(s => s.Title).Should().Equal("P1 A", "P2 A");
    }

    [Fact]
    public async Task GetDraft_WithoutDraft_ReturnsCurrentPublishedAsStartingPoint()
    {
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);

        var draft = await GetDraftAsync(manager.Token);

        draft.IsDraft.Should().BeFalse();
        draft.UpdatedAt.Should().BeNull();
        draft.TermsSections.Should().HaveCount(4);
        draft.PrivacySections.Should().HaveCount(4);
    }

    [Fact]
    public async Task OnlyManagersAndAdmins_CanUseTheLegalOpsEndpoints()
    {
        (await _client.GetAsync($"{Ops}/versions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var staff = await AccountWithRoleAsync(UserRole.Staff);
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/versions", staff.Token))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/versions", manager.Token))).StatusCode.Should().Be(HttpStatusCode.OK);

        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/versions", admin))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private record RoleBody(string Role, bool Editable, List<string> Capabilities);
    private record RolesBody(List<RoleBody> Roles);

    [Fact]
    public async Task Staff_GrantedTheCapabilityByAnAdmin_CanDraft_ButStillCanNotPublish()
    {
        await ResetAsync();
        var admin = await AdminTokenAsync();
        var staff = await AccountWithRoleAsync(UserRole.Staff);
        var before = (await ReadAsync<RolesBody>(await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/role-permissions", admin))))
            .Roles.Single(r => r.Role == nameof(UserRole.Staff)).Capabilities;
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/versions", staff.Token))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        try
        {
            var grant = await _client.SendAsync(Request(HttpMethod.Put, "/api/v1/admin/role-permissions/Staff", admin,
                new { capabilities = before.Append("ManageLegalDocuments"), reason = "kiểm thử" }));
            grant.StatusCode.Should().Be(HttpStatusCode.OK);

            (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/versions", staff.Token))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PutDraftAsync(staff.Token, DraftPayload())).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PublishAsync(staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            // Bộ test dùng chung một DB: trả quyền của Staff về như cũ để test khác không bị ảnh hưởng.
            await _client.SendAsync(Request(HttpMethod.Put, "/api/v1/admin/role-permissions/Staff", admin, new { capabilities = before, reason = "khôi phục sau kiểm thử" }));
        }
    }

    [Fact]
    public async Task ADemotedManager_LosesAccessImmediately_EvenWithAStillValidToken()
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/versions", manager.Token))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SetRoleAsync(admin, manager.Id, UserRole.User)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/versions", manager.Token))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SaveDraft_WithAStaleBaseUpdatedAt_Returns409_AndKeepsTheNewerText()
    {
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        var first = await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, DraftPayload(marker: "A")));
        var second = await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, DraftPayload(first.UpdatedAt, "B")));

        var stale = await PutDraftAsync(manager.Token, DraftPayload(first.UpdatedAt, "C"));

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(stale)).Should().Be(LegalErrorCodes.DraftStale);
        var kept = await GetDraftAsync(manager.Token);
        kept.TermsSections.Select(s => s.Title).Should().Equal("T1 B", "T2 B");
        kept.UpdatedAt.Should().Be(second.UpdatedAt);
    }

    [Fact]
    public async Task SaveDraft_EchoingTheUpdatedAtItJustReturned_AlwaysSucceeds()
    {
        // Postgres cắt về micro giây còn .NET giữ 100ns: nếu server trả giá trị chưa cắt thì lần lưu kế tiếp bị 409 oan.
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        var current = await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, DraftPayload(marker: "0")));

        for (var i = 1; i <= 3; i++)
        {
            current = await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, DraftPayload(current.UpdatedAt, i.ToString())));
        }

        current.TermsSections[0].Title.Should().Be("T1 3");
    }

    public static IEnumerable<object[]> InvalidDrafts()
    {
        var ok = new[] { Section("T") };
        yield return new object[] { new { termsSections = Array.Empty<object>(), privacySections = ok } };
        yield return new object[] { new { termsSections = ok, privacySections = Array.Empty<object>() } };
        yield return new object[] { new { termsSections = ok } };
        yield return new object[] { new { termsSections = new[] { Section(new string('t', 201)) }, privacySections = ok } };
        yield return new object[] { new { termsSections = new[] { Section("T", new string('b', 20_001)) }, privacySections = ok } };
        yield return new object[] { new { termsSections = new[] { Section("T", "") }, privacySections = ok } };
        yield return new object[] { new { termsSections = new[] { Section("  ") }, privacySections = ok } };
        yield return new object[] { new { termsSections = Enumerable.Range(0, 51).Select(i => Section($"T{i}")).ToArray(), privacySections = ok } };
    }

    [Theory]
    [MemberData(nameof(InvalidDrafts))]
    public async Task SaveDraft_RejectsInvalidInput_AndStoresNothing(object payload)
    {
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);

        var response = await PutDraftAsync(manager.Token, payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetDraftAsync(manager.Token)).IsDraft.Should().BeFalse();
    }

    // ---- Task 5: Admin xuất bản ----

    private const string Publish = "/api/v1/admin/legal/draft/publish";

    private record PublishedBody(int Version, DateTimeOffset PublishedAt);
    private record ProfileBody(bool TermsAccepted);

    private Task<HttpResponseMessage> PublishAsync(string token) => _client.SendAsync(Request(HttpMethod.Post, Publish, token));

    private async Task<int> CurrentVersionAsync()
        => (await ReadAsync<DocumentBody>(await _client.GetAsync("/api/v1/legal/terms"))).Version;

    private async Task<string> AdminTokenAsync() => await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);

    private async Task<HttpResponseMessage> AcceptTermsAsync(string token, int version)
        => await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/users/me/consents", token, new { kind = "terms", version }));

    private async Task<bool> TermsAcceptedAsync(string token)
        => (await ReadAsync<ProfileBody>(await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", token)))).TermsAccepted;

    [Fact]
    public async Task Manager_CanNotPublish_AndTheCurrentVersionStays()
    {
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, DraftPayload()));

        (await PublishAsync(manager.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await CurrentVersionAsync()).Should().Be(4);
        (await GetDraftAsync(manager.Token)).IsDraft.Should().BeTrue("nháp vẫn còn đó, chưa ai xuất bản");
    }

    [Fact]
    public async Task Admin_Publish_AssignsTheNextVersion_AndThePublicReadShowsIt()
    {
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, DraftPayload(marker: "NEW")));

        var published = await ReadAsync<PublishedBody>(await PublishAsync(await AdminTokenAsync()));

        published.Version.Should().Be(5);
        var terms = await ReadAsync<DocumentBody>(await _client.GetAsync("/api/v1/legal/terms"));
        var privacy = await ReadAsync<DocumentBody>(await _client.GetAsync("/api/v1/legal/privacy"));
        terms.Version.Should().Be(5);
        terms.Sections.Select(s => s.Title).Should().Equal("T1 NEW", "T2 NEW");
        privacy.Sections.Select(s => s.Title).Should().Equal("P1 NEW", "P2 NEW");

        var versions = await ReadAsync<List<VersionBody>>(await _client.SendAsync(Request(HttpMethod.Get, $"{Ops}/versions", manager.Token)));
        versions.Should().NotContain(v => v.Status == "draft");
        versions.Select(v => v.Version).Should().BeEquivalentTo(new int?[] { 4, 5 });
    }

    [Fact]
    public async Task Publish_WithoutADraft_Returns422()
    {
        await ResetAsync();

        var response = await PublishAsync(await AdminTokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be(LegalErrorCodes.NoDraft);
    }

    [Fact]
    public async Task Publish_ADraftIdenticalToTheCurrentText_Returns422_AndKeepsTheVersion()
    {
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        var current = await GetDraftAsync(manager.Token);
        await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, new
        {
            termsSections = current.TermsSections.Select(s => Section(s.Title, s.Body)).ToArray(),
            privacySections = current.PrivacySections.Select(s => Section(s.Title, s.Body)).ToArray(),
        }));

        var response = await PublishAsync(await AdminTokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be(LegalErrorCodes.DraftUnchanged);
        (await CurrentVersionAsync()).Should().Be(4);
    }

    [Fact]
    public async Task TheAnonymousReader_NeverSeesTheDraft()
    {
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        const string marker = "DAU-HIEU-CHI-CO-O-BAN-NHAP";
        await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, DraftPayload(marker: marker)));

        foreach (var kind in new[] { "terms", "privacy" })
        {
            var response = await _client.GetAsync($"/api/v1/legal/{kind}");
            (await response.Content.ReadAsStringAsync()).Should().NotContain(marker);
            (await ReadAsync<DocumentBody>(response)).Version.Should().Be(4);
        }
    }

    [Fact]
    public async Task Publishing_InvalidatesTheOldConsent_EvenWhenTheProfileIsCached()
    {
        await ResetAsync();
        var user = await RegisterAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        (await AcceptTermsAsync(user.Token, 4)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TermsAcceptedAsync(user.Token)).Should().BeTrue("lần đọc này cũng đưa profile vào cache 15 phút");
        await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, DraftPayload(marker: "V5")));

        await ReadAsync<PublishedBody>(await PublishAsync(await AdminTokenAsync()));

        (await TermsAcceptedAsync(user.Token)).Should().BeFalse("hiệu lực ngay, không đợi cache hết hạn");
        var oldApp = await AcceptTermsAsync(user.Token, 4);
        oldApp.StatusCode.Should().Be(HttpStatusCode.Conflict, "app cũ không được đồng ý thay cho câu chữ mới");
        (await AcceptTermsAsync(user.Token, 5)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TermsAcceptedAsync(user.Token)).Should().BeTrue();
    }

    [Fact]
    public async Task TwoAdminsPublishingAtOnce_YieldExactlyOneNewVersion()
    {
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, DraftPayload(marker: "RACE")));
        var admin = await AdminTokenAsync();

        var responses = await Task.WhenAll(PublishAsync(admin), PublishAsync(admin));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Single(r => r.StatusCode != HttpStatusCode.OK).StatusCode
            .Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity);
        (await CurrentVersionAsync()).Should().Be(5);
    }

    [Fact]
    public async Task Body_WithHtml_IsStoredAndReturnedVerbatim()
    {
        await ResetAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        const string body = "<script>alert(1)</script> & \"quoted\"";

        await ReadAsync<DraftBody>(await PutDraftAsync(manager.Token, new
        {
            termsSections = new[] { Section("T", body) },
            privacySections = new[] { Section("P") },
        }));

        (await GetDraftAsync(manager.Token)).TermsSections[0].Body.Should().Be(body);
    }
}

/// <summary>
/// Migration <c>AddLegalVersions</c> trên dữ liệu có sẵn: <c>RolePermissionSeeder</c> không bao giờ cập nhật hàng đã có, nên migration phải tự cấp quyền mới
/// cho OpsManager đã tồn tại, nếu không họ bị 403 âm thầm. Staff không được cấp.
/// </summary>
public class AddLegalVersionsMigrationTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string MigrationBefore = "20261111114259_AddForecastAccuracy";
    private const long ManageLegalDocuments = 1L << 17;
    private readonly ReceiptScanDbFixture _fixture;

    public AddLegalVersionsMigrationTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivesExistingOpsManagersTheNewCapability_SeedsVersion4_AndGoesBackDown()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"legal_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO role_permissions (role_id, capabilities, updated_at) VALUES (2, 31, now()), (3, 14335, now())");

        // Dừng đúng ở migration này: các migration sau (ví dụ AddViewProductKpiCapability) cũng cấp bit cho OpsManager.
        await migrator.MigrateAsync("20261113025605_AddLegalVersions");

        async Task<long> CapabilitiesOf(int roleId) => await db.Database
            .SqlQuery<long>($"""SELECT capabilities AS "Value" FROM role_permissions WHERE role_id = {roleId}""").SingleAsync();
        (await CapabilitiesOf(3)).Should().Be(14335 | ManageLegalDocuments);
        (await CapabilitiesOf(2)).Should().Be(31, "Staff không được cấp quyền soạn văn bản pháp lý");
        (await db.Database.SqlQuery<long>($"""SELECT count(*) AS "Value" FROM legal_versions WHERE version = 4 AND status = 'published'""").SingleAsync())
            .Should().Be(1);

        await migrator.MigrateAsync(MigrationBefore);

        (await CapabilitiesOf(3)).Should().Be(14335);
        (await CapabilitiesOf(2)).Should().Be(31);
    }
}
