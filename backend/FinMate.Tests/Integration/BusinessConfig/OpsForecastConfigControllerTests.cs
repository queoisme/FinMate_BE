using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.BusinessConfig;

/// <summary>
/// <c>/ops/forecast-config</c> qua HTTP. Cấu hình hệ thống là chung cho cả bộ test, nên mỗi test dọn bản chờ duyệt của mình và trả cấu hình về bản ban đầu
/// (Admin hoàn tác) để không làm lệch các test dự báo khác.
/// </summary>
[Collection("Integration")]
public class OpsForecastConfigControllerTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Root = "/api/v1/ops/forecast-config";
    private const string AdminConfig = "/api/v1/admin/system-config";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;
    private string _admin = string.Empty;
    private int _originalVersion;

    public OpsForecastConfigControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _admin = await TokenAsync(UserRole.Admin);
        _originalVersion = (await DataAsync(await SendAsync(HttpMethod.Get, AdminConfig, _admin))).GetProperty("version").GetInt32();
    }

    public async Task DisposeAsync()
    {
        // Bỏ mọi bản còn chờ (người khác từ chối), rồi đưa cấu hình về đúng bản lúc bắt đầu.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE system_config_versions SET status = 'cancelled' WHERE scope = 'forecast' AND status IN ('draft','pending_approval')");
        var current = (await DataAsync(await SendAsync(HttpMethod.Get, AdminConfig, _admin))).GetProperty("version").GetInt32();
        if (current != _originalVersion && _originalVersion > 0)
        {
            await DataAsync(await SendAsync(HttpMethod.Post, $"{AdminConfig}/rollback/{_originalVersion}", _admin, new { expectedVersion = current, note = "trả lại sau test" }));
        }
    }

    private async Task<string> TokenAsync(UserRole role, bool opsVerified = false, WebApplicationFactory<Program>? factory = null)
    {
        using var scope = (factory ?? _factory).Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"fcfg{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user, opsVerified);
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

    private async Task<Guid> SubmitDraftAsync(string author, object changes)
    {
        var created = await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts", author, new { changes, reason = "đổi tham số" }));
        var id = created.GetProperty("id").GetGuid();
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts/{id}/submit", author));
        return id;
    }

    private async Task<int> PublicHorizonAsync()
        => JsonDocument.Parse(await _client.GetStringAsync("/api/v1/app-config")).RootElement
            .GetProperty("data").GetProperty("config").GetProperty("forecast").GetProperty("lowBalanceHorizonDays").GetInt32();

    [Fact]
    public async Task StaffDrafts_ManagerApproves_AndOnlyThenTheRuntimeChanges()
    {
        var staff = await TokenAsync(UserRole.Staff);
        var manager = await TokenAsync(UserRole.OpsManager);
        var before = await PublicHorizonAsync();

        var id = await SubmitDraftAsync(staff, new { forecast = new { lowBalanceHorizonDays = before + 7 } });
        (await PublicHorizonAsync()).Should().Be(before, "bản chờ duyệt không ảnh hưởng runtime");
        using (var scope = _factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ISystemConfigProvider>().GetAsync()).Document.Forecast.LowBalanceHorizonDays.Should().Be(before);
        }

        (await SendAsync(HttpMethod.Post, $"{Root}/drafts/{id}/approve", staff)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "Staff không duyệt được");
        var approved = await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts/{id}/approve", manager, new { note = "ok" }));

        approved.GetProperty("status").GetString().Should().Be("active");
        (await PublicHorizonAsync()).Should().Be(before + 7);
        var history = await DataAsync(await SendAsync(HttpMethod.Get, $"{AdminConfig}/versions?limit=200", _admin));
        history.GetProperty("items").EnumerateArray().Should().Contain(v => v.GetProperty("version").GetInt32() == approved.GetProperty("approvedVersion").GetInt32());
    }

    [Fact]
    public async Task TheAdminHistoryNeverListsDrafts()
    {
        var staff = await TokenAsync(UserRole.Staff);
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts", staff, new { changes = new { forecast = new { incomeLateDays = 6 } }, reason = "nháp" }));

        var history = await DataAsync(await SendAsync(HttpMethod.Get, $"{AdminConfig}/versions?limit=200", _admin));

        history.GetProperty("items").EnumerateArray().Where(v => v.GetProperty("version").GetInt32() <= 0).Should().BeEmpty();
        history.GetProperty("total").GetInt32().Should().Be(history.GetProperty("items").GetArrayLength(), "tổng đếm cũng không gồm bản nháp");
    }

    [Fact]
    public async Task AdminApprovesSomeoneElsesDraft_ButNotTheirOwn()
    {
        var manager = await TokenAsync(UserRole.OpsManager);
        var managerDraft = await SubmitDraftAsync(manager, new { limits = new { alertT1Percent = 72 } });
        (await SendAsync(HttpMethod.Post, $"{Root}/drafts/{managerDraft}/approve", manager)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts/{managerDraft}/approve", _admin))).GetProperty("status").GetString().Should().Be("active");

        var adminDraft = await SubmitDraftAsync(_admin, new { limits = new { alertT1Percent = 71 } });
        var own = await SendAsync(HttpMethod.Post, $"{Root}/drafts/{adminDraft}/approve", _admin);
        own.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(own)).Should().Be("FORECAST_CONFIG_OWN_DRAFT");
    }

    [Theory]
    [InlineData("""{"maintenance":{"enabled":true}}""", "FORECAST_CONFIG_OUT_OF_SCOPE")]
    [InlineData("""{"refunds":{"matchWindowDays":10}}""", "FORECAST_CONFIG_OUT_OF_SCOPE")]
    [InlineData("""{"forecast":{"lowBalanceHorizonDayz":21}}""", "FORECAST_CONFIG_UNKNOWN_FIELD")]
    public async Task ChangesOutsideTheTwoSectionsGet400(string changes, string code)
    {
        var staff = await TokenAsync(UserRole.Staff);

        var response = await SendAsync(HttpMethod.Post, $"{Root}/drafts", staff, new { changes = JsonDocument.Parse(changes).RootElement, reason = "thử" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be(code);
    }

    [Fact]
    public async Task AStaleDraftGets409_AndAnotherPendingGets409()
    {
        var staff = await TokenAsync(UserRole.Staff);
        var staff2 = await TokenAsync(UserRole.Staff);
        var manager = await TokenAsync(UserRole.OpsManager);
        var id = await SubmitDraftAsync(staff, new { forecast = new { billReminderDaysBefore = 4 } });

        var other = await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts", staff2, new { changes = new { forecast = new { incomeLateDays = 4 } }, reason = "song song" }));
        var second = await SendAsync(HttpMethod.Post, $"{Root}/drafts/{other.GetProperty("id").GetGuid()}/submit", staff2);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(second)).Should().Be("FORECAST_CONFIG_PENDING_EXISTS");

        var config = await DataAsync(await SendAsync(HttpMethod.Get, AdminConfig, _admin));
        var content = config.GetProperty("content").Clone();
        var node = System.Text.Json.Nodes.JsonNode.Parse(content.GetRawText())!.AsObject();
        node["snapshots"]!["retentionDays"] = 400;
        await DataAsync(await SendAsync(HttpMethod.Put, AdminConfig, _admin, new { expectedVersion = config.GetProperty("version").GetInt32(), content = node, note = "admin đổi nhóm khác" }));

        var stale = await SendAsync(HttpMethod.Post, $"{Root}/drafts/{id}/approve", manager);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(stale)).Should().Be("FORECAST_CONFIG_DRAFT_STALE");
        (await DataAsync(await SendAsync(HttpMethod.Get, $"{Root}/drafts/{id}", manager))).GetProperty("stale").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task OrdinaryUserIsRefused_AndStaffWithoutTheCapabilityIsRefused()
    {
        (await SendAsync(HttpMethod.Get, Root, await TokenAsync(UserRole.User))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var staff = await TokenAsync(UserRole.Staff);
        var defaults = OpsPermissions.Names(OpsPermissions.For(UserRole.Staff));
        await DataAsync(await SendAsync(HttpMethod.Put, "/api/v1/admin/role-permissions/Staff", _admin,
            new { capabilities = defaults.Where(c => c != nameof(OpsCapability.DraftForecastConfig)).ToList(), reason = "kiểm thử" }));
        try
        {
            (await SendAsync(HttpMethod.Get, Root, staff)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await SendAsync(HttpMethod.Post, $"{Root}/drafts", staff, new { changes = new { forecast = new { incomeLateDays = 4 } }, reason = "thử" }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await SendAsync(HttpMethod.Put, "/api/v1/admin/role-permissions/Staff", _admin, new { capabilities = defaults, reason = "trả lại" });
        }
    }

    [Fact]
    public async Task WithTwoFactorOn_AnUnverifiedTokenIsRefused()
    {
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<OpsAuthOptions>();
            services.AddSingleton(new OpsAuthOptions(true));
        }));
        var client = factory.CreateClient();

        (await SendAsync(HttpMethod.Get, Root, await TokenAsync(UserRole.Staff, false, factory), client: client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, Root, await TokenAsync(UserRole.Staff, true, factory), client: client)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MetaDescribesEveryField()
    {
        var meta = await DataAsync(await SendAsync(HttpMethod.Get, $"{Root}/meta", await TokenAsync(UserRole.Staff)));

        var horizon = meta.EnumerateArray().Single(f => f.GetProperty("path").GetString() == "forecast.lowBalanceHorizonDays");
        horizon.GetProperty("min").GetInt64().Should().Be(3);
        horizon.GetProperty("max").GetInt64().Should().Be(60);
        meta.EnumerateArray().Should().Contain(f => f.GetProperty("path").GetString() == "limits.alertT1Percent" && f.GetProperty("below").GetString() == "limits.alertT2Percent");
    }
}
