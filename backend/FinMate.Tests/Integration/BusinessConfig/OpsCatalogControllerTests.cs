using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.BusinessConfig;

/// <summary>
/// Phase 95 (B′): Staff duy trì danh mục, thương hiệu, từ khoá qua <c>/ops/catalog</c> — sửa trực tiếp, gọi đúng handler của <c>/admin</c> (đồng bộ AI vẫn chạy),
/// audit <c>Ops.Catalog.*</c>, không đổi slug, không tắt danh mục được bảo vệ. AI Service và đồng bộ danh mục là bản giả ghi lại lời gọi.
/// </summary>
[Collection("Integration")]
public class OpsCatalogControllerTests : IClassFixture<AuthApiFactory>
{
    private const string Root = "/api/v1/ops/catalog";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class RecordingRules : IAiRulesAdminClient
    {
        public List<AiRulesRequest> Calls { get; } = [];

        public Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default)
        {
            lock (Calls)
            {
                Calls.Add(request);
            }

            return Task.FromResult<JsonElement?>(JsonDocument.Parse("""{"ok":true}""").RootElement.Clone());
        }
    }

    private sealed class RecordingSync : IAiCategorySyncService
    {
        public int Count;

        public Task SyncAsync(Guid? adminId, string? reason, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> TrySyncAsync(Guid? adminId, string? reason, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Count);
            return Task.FromResult(true);
        }
    }

    private readonly RecordingRules _rules = new();
    private readonly RecordingSync _sync = new();
    private readonly WebApplicationFactory<FinMate.API.Program> _factory;
    private readonly HttpClient _client;

    public OpsCatalogControllerTests(AuthApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiRulesAdminClient>();
            services.AddSingleton<IAiRulesAdminClient>(_rules);
            services.RemoveAll<IAiCategorySyncService>();
            services.AddSingleton<IAiCategorySyncService>(_sync);
        }));
        _client = _factory.CreateClient();
    }

    private async Task<(Guid Id, string Token)> AccountAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"cat{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user));
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

    private async Task<Guid> SystemCategoryIdAsync(string slug)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().Categories.IgnoreQueryFilters()
            .Where(c => c.UserId == null && c.Slug == slug).Select(c => c.Id).SingleAsync();
    }

    [Fact]
    public async Task StaffAddsRenamesAndHidesACategory_SyncRuns_AndManagersSeeTheAudit()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var slug = $"kpi_{Guid.NewGuid():N}"[..20];
        var before = _sync.Count;

        var created = await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/categories", staff.Token, new { name = "Thú cưng", slug, iconName = "pets" }));
        var id = created.GetProperty("id").GetGuid();
        await DataAsync(await SendAsync(HttpMethod.Patch, $"{Root}/categories/{id}", staff.Token, new { name = "Thú cưng & chăm sóc" }));
        var hidden = await DataAsync(await SendAsync(HttpMethod.Patch, $"{Root}/categories/{id}/activation", staff.Token, new { isActive = false }));

        hidden.GetProperty("isActive").GetBoolean().Should().BeFalse();
        (_sync.Count - before).Should().Be(3, "mỗi lần ghi gọi đồng bộ AI như đường admin");
        var audit = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/ops/audit-logs?eventType=Ops.Catalog.CategoryCreated&limit=100", manager.Token));
        audit.GetRawText().Should().Contain(staff.Id.ToString(), "Manager đọc được thao tác của Staff ở nhật ký vận hành (chỉ hiện Ops.*)");
        using var scope = _factory.Services.CreateScope();
        var events = await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().AuditLogs.Where(a => a.UserId == staff.Id).Select(a => a.EventType).ToListAsync();
        events.Should().BeEquivalentTo("Ops.Catalog.CategoryCreated", "Ops.Catalog.CategoryUpdated", "Ops.Catalog.CategoryActivationChanged");
    }

    [Fact]
    public async Task StaffCannotChangeASlug()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var id = await SystemCategoryIdAsync("food");

        var response = await SendAsync(HttpMethod.Patch, $"{Root}/categories/{id}", staff.Token, new { slug = "an_uong" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be("ADMIN_IMMUTABLE_FIELD");
    }

    [Theory]
    [InlineData("income")]
    [InlineData("loan")]
    [InlineData("other")]
    public async Task ProtectedCategoriesCannotBeHidden_ButCanBeRenamed(string slug)
    {
        var staff = await AccountAsync(UserRole.Staff);
        var id = await SystemCategoryIdAsync(slug);

        var hide = await SendAsync(HttpMethod.Patch, $"{Root}/categories/{id}/activation", staff.Token, new { isActive = false });

        hide.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(hide)).Should().Be("PROTECTED_CATEGORY");
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == id)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task StaffMaintainsBrandsAndKeywords_ThroughTheSameAiClient_WithOpsAudit()
    {
        var staff = await AccountAsync(UserRole.Staff);

        await DataAsync(await SendAsync(HttpMethod.Put, $"{Root}/ai-rules/brands/highlands", staff.Token, new { category_slug = "dining", reason = "thương hiệu mới" }));
        (await SendAsync(HttpMethod.Post, $"{Root}/ai-rules/keywords", staff.Token, new { kind = "merchant", category_slug = "dining", keyword = "tra sua" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await DataAsync(await SendAsync(HttpMethod.Patch, $"{Root}/ai-rules/brands/highlands/active", staff.Token, new { is_active = false }));

        lock (_rules.Calls)
        {
            _rules.Calls.Where(c => c.Method != HttpMethod.Get).Select(c => c.Path).Should().Contain(["brands/highlands", "keywords", "brands/highlands/active"]);
        }

        using var scope = _factory.Services.CreateScope();
        var events = await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().AuditLogs.Where(a => a.UserId == staff.Id).Select(a => a.EventType).ToListAsync();
        events.Should().HaveCount(3).And.OnlyContain(e => e == "Ops.Catalog.AiRuleChanged");
    }

    [Theory]
    [InlineData("", HttpStatusCode.BadRequest)]
    [InlineData("?resource=item_cache", HttpStatusCode.BadRequest)]
    [InlineData("?resource=instruction", HttpStatusCode.BadRequest)]
    [InlineData("?resource=brand", HttpStatusCode.OK)]
    [InlineData("?resource=keyword", HttpStatusCode.OK)]
    public async Task HistoryIsLimitedToBrandsAndKeywords(string query, HttpStatusCode expected)
    {
        var staff = await AccountAsync(UserRole.Staff);

        (await SendAsync(HttpMethod.Get, $"{Root}/ai-rules/history{query}", staff.Token)).StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task NothingOutsideTheNarrowScopeIsReachable()
    {
        var staff = await AccountAsync(UserRole.Staff);

        (await SendAsync(HttpMethod.Put, $"{Root}/ai-rules/item-cache", staff.Token, new { name = "x", category_slug = "food" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"{Root}/ai-rules/history/{Guid.NewGuid()}/revert", staff.Token, new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, "/api/v1/admin/categories", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "route /admin vẫn chỉ Admin");
        (await SendAsync(HttpMethod.Get, "/api/v1/admin/ai-rules/brands", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UsersAreRefused_AndAdminCanTurnTheCapabilityOff()
    {
        var user = await AccountAsync(UserRole.User);
        (await SendAsync(HttpMethod.Get, $"{Root}/categories", user.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var staff = await AccountAsync(UserRole.Staff);
        var admin = await AccountAsync(UserRole.Admin);
        var defaults = OpsPermissions.Names(OpsPermissions.For(UserRole.Staff));
        await DataAsync(await SendAsync(HttpMethod.Put, "/api/v1/admin/role-permissions/Staff", admin.Token,
            new { capabilities = defaults.Where(c => c != nameof(OpsCapability.MaintainCatalog)).ToList(), reason = "kiểm thử" }));
        try
        {
            (await SendAsync(HttpMethod.Get, $"{Root}/categories", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await SendAsync(HttpMethod.Post, $"{Root}/ai-rules/keywords", staff.Token, new { kind = "merchant", category_slug = "dining", keyword = "abc" }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await SendAsync(HttpMethod.Put, "/api/v1/admin/role-permissions/Staff", admin.Token, new { capabilities = defaults, reason = "trả lại" });
        }
    }
}
