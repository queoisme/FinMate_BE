using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.BusinessConfig;

/// <summary>
/// Phase 95 §13, kịch bản đầu-cuối qua HTTP. (a) Staff soạn → OpsManager từ chối → Staff làm lại và đổi thêm → OpsManager duyệt → dự báo và ngưỡng ngân sách
/// của một người dùng dùng giá trị mới. (b) bản chờ duyệt cũ bị chặn, người soạn làm lại từ chính bản đó (S1), duyệt được, thay đổi Admin vừa lưu vẫn còn.
/// Cấu hình là chung cho cả bộ test nên cuối mỗi test được trả về bản ban đầu.
/// </summary>
[Collection("Integration")]
public class ForecastConfigScenarioTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Root = "/api/v1/ops/forecast-config";
    private const string AdminConfig = "/api/v1/admin/system-config";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;
    private (Guid Id, string Token) _admin;
    private int _originalVersion;

    public ForecastConfigScenarioTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _admin = await AccountAsync(UserRole.Admin);
        _originalVersion = (await DataAsync(await SendAsync(HttpMethod.Get, AdminConfig, _admin.Token))).GetProperty("version").GetInt32();
    }

    public async Task DisposeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE system_config_versions SET status = 'cancelled' WHERE scope = 'forecast' AND status IN ('draft','pending_approval')");
        }

        var current = (await DataAsync(await SendAsync(HttpMethod.Get, AdminConfig, _admin.Token))).GetProperty("version").GetInt32();
        if (current != _originalVersion && _originalVersion > 0)
        {
            await DataAsync(await SendAsync(HttpMethod.Post, $"{AdminConfig}/rollback/{_originalVersion}", _admin.Token, new { expectedVersion = current, note = "trả lại sau kịch bản" }));
        }
    }

    private async Task<(Guid Id, string Token)> AccountAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"scn{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
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

    private Task<HttpResponseMessage> CashflowAsync(string userToken, int daysAhead)
        => SendAsync(HttpMethod.Get, $"/api/v1/forecast/cashflow?to={VietnamTime.Today().AddDays(daysAhead):yyyy-MM-dd}", userToken);

    private async Task<List<string>> AuditEventsAsync(params Guid[] actors)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().AuditLogs.AsNoTracking()
            .Where(a => a.UserId != null && actors.Contains(a.UserId.Value)).Select(a => a.EventType).ToListAsync();
    }

    [Fact]
    public async Task A_DraftRejectRedoApprove_AndTheForecastUsesTheNewValues()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var user = await AccountAsync(UserRole.User);   // không có phương pháp tài chính, không đặt ngưỡng riêng → dùng mặc định hệ thống
        var current = await DataAsync(await SendAsync(HttpMethod.Get, Root, staff.Token));
        var horizon = current.GetProperty("sections").GetProperty("forecast").GetProperty("maxHorizonDays").GetInt32();
        horizon.Should().BeLessThan(120);

        // 1. Staff soạn, xem diff, gửi duyệt.
        var first = await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts", staff.Token,
            new { changes = new { forecast = new { maxHorizonDays = 120 } }, reason = "cho phép xem dự báo 4 tháng" }));
        var diff = first.GetProperty("diff").EnumerateArray().Single();
        (diff.GetProperty("path").GetString(), diff.GetProperty("before").GetInt32(), diff.GetProperty("after").GetInt32()).Should().Be(("forecast.maxHorizonDays", horizon, 120));
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts/{first.GetProperty("id").GetGuid()}/submit", staff.Token));
        (await CashflowAsync(user.Token, 100)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "bản chờ duyệt chưa có hiệu lực");

        // 2. OpsManager từ chối kèm ghi chú.
        var rejected = await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts/{first.GetProperty("id").GetGuid()}/reject", manager.Token,
            new { note = "đổi luôn ngưỡng T1 cho khớp" }));
        rejected.GetProperty("status").GetString().Should().Be("rejected");

        // 3. Staff làm lại từ bản bị từ chối và đổi thêm T1, gửi duyệt.
        var redo = await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts", staff.Token,
            new { fromDraftId = first.GetProperty("id").GetGuid(), changes = new { limits = new { alertT1Percent = 75 } }, reason = "sửa theo góp ý" }));
        redo.GetProperty("diff").EnumerateArray().Select(d => d.GetProperty("path").GetString()).Should().BeEquivalentTo("forecast.maxHorizonDays", "limits.alertT1Percent");
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts/{redo.GetProperty("id").GetGuid()}/submit", staff.Token));

        // 4. OpsManager duyệt.
        var approved = await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts/{redo.GetProperty("id").GetGuid()}/approve", manager.Token, new { note = "ok" }));
        approved.GetProperty("status").GetString().Should().Be("active");
        approved.GetProperty("createdBy").GetGuid().Should().Be(staff.Id);
        approved.GetProperty("decidedBy").GetGuid().Should().Be(manager.Id);

        // 5. Dự báo và ngưỡng ngân sách của người dùng dùng giá trị mới.
        (await CashflowAsync(user.Token, 100)).StatusCode.Should().Be(HttpStatusCode.OK, "dự báo giờ cho tới 120 ngày");
        var thresholds = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/me/alert-thresholds", user.Token));
        thresholds.GetProperty("t1Pct").GetInt32().Should().Be(75);
        thresholds.GetProperty("source").GetString().Should().Be("system");

        // 6. Audit đủ các bước, không có giá trị hay ghi chú.
        var events = await AuditEventsAsync(staff.Id, manager.Id);
        events.Should().Contain(["Ops.ForecastConfig.DraftCreated", "Ops.ForecastConfig.Submitted", "Ops.ForecastConfig.Rejected", "Ops.ForecastConfig.Approved", "Admin.SystemConfig.Changed"]);
        events.Count(e => e == "Ops.ForecastConfig.DraftCreated").Should().Be(2);
    }

    [Fact]
    public async Task B_AStalePendingDraftIsBlocked_TheAuthorRedoesIt_AndTheAdminsChangeSurvives()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var draft = await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts", staff.Token,
            new { changes = new { forecast = new { billReminderDaysBefore = 5 } }, reason = "nhắc hoá đơn sớm hơn" }));
        var draftId = draft.GetProperty("id").GetGuid();
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts/{draftId}/submit", staff.Token));

        // Admin sửa trực tiếp một nhóm khác.
        var config = await DataAsync(await SendAsync(HttpMethod.Get, AdminConfig, _admin.Token));
        var content = JsonNode.Parse(config.GetProperty("content").GetRawText())!.AsObject();
        var retention = content["snapshots"]!["retentionDays"]!.GetValue<int>() + 1;
        content["snapshots"]!["retentionDays"] = retention;
        await DataAsync(await SendAsync(HttpMethod.Put, AdminConfig, _admin.Token, new { expectedVersion = config.GetProperty("version").GetInt32(), content, note = "admin đổi thời hạn ảnh chụp" }));

        var stale = await SendAsync(HttpMethod.Post, $"{Root}/drafts/{draftId}/approve", manager.Token);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(stale)).Should().Be("FORECAST_CONFIG_DRAFT_STALE");

        // S1: làm lại từ chính bản đang chờ (của mình, đã cũ) → bản cũ bị huỷ, chỗ chờ duyệt được nhả ra.
        var redo = await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts", staff.Token, new { fromDraftId = draftId, reason = "làm lại trên bản mới" }));
        redo.GetProperty("conflicts").GetArrayLength().Should().Be(0);
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts/{redo.GetProperty("id").GetGuid()}/submit", staff.Token));
        (await DataAsync(await SendAsync(HttpMethod.Get, $"{Root}/drafts/{draftId}", manager.Token))).GetProperty("status").GetString().Should().Be("cancelled");
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Root}/drafts/{redo.GetProperty("id").GetGuid()}/approve", manager.Token));

        var after = await DataAsync(await SendAsync(HttpMethod.Get, AdminConfig, _admin.Token));
        after.GetProperty("content").GetProperty("forecast").GetProperty("billReminderDaysBefore").GetInt32().Should().Be(5);
        after.GetProperty("content").GetProperty("snapshots").GetProperty("retentionDays").GetInt32().Should().Be(retention, "thay đổi Admin vừa lưu không bị đè");
    }
}
