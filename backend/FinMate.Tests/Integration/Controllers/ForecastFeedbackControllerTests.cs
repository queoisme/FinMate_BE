using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Vòng khép kín dự báo: người dùng báo sai, Staff gán nguyên nhân, Manager xem độ chính xác. Staff không thấy danh tính; mỗi vai trò chỉ thấy phần của mình.</summary>
[Collection("Integration")]
public class ForecastFeedbackControllerTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ForecastFeedbackControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private sealed record Account(Guid Id, string Token);

    private async Task<Account> AccountAsync(UserRole role = UserRole.User)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Nguyễn Văn Tên Thật", Role = role, CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new Account(user.Id, scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user));
    }

    private async Task<Guid> SnapshotAsync(Guid userId, long? actual, int horizon = 14, string confidence = "normal", int originDaysAgo = 20)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var origin = VietnamTime.Today().AddDays(-originDaysAgo);
        var snapshot = new ForecastSnapshot
        {
            Id = Guid.NewGuid(), UserId = userId, OriginDate = origin, HorizonDays = horizon, HorizonDate = origin.AddDays(horizon), Method = "median_p25p75", ConfigVersion = 1,
            StartBalanceCents = 1_500_000, PredictedCents = 1_000_000, ConservativeCents = 800_000, OptimisticCents = 1_200_000, RecurringOnlyCents = 1_050_000,
            Confidence = confidence, ActualCents = actual, ActualFilledAt = actual is null ? null : DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ForecastSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return snapshot.Id;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();

    [Fact]
    public async Task AUserCanReportAWrongForecastAndTheStaffQueueShowsItWithoutAnyIdentity()
    {
        var user = await AccountAsync();
        await SnapshotAsync(user.Id, actual: 900_000);
        var staff = await AccountAsync(UserRole.Staff);

        var created = await SendAsync(HttpMethod.Post, "/api/v1/forecast/feedback", user.Token, new { reason = "missing_bill", note = "Quên tiền net" });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var listed = await SendAsync(HttpMethod.Get, "/api/v1/ops/forecast/feedback?status=new&limit=100", staff.Token);
        listed.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await listed.Content.ReadAsStringAsync();
        var item = JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("items").EnumerateArray().First(i => i.GetProperty("note").GetString() == "Quên tiền net");
        item.GetProperty("reason").GetString().Should().Be("missing_bill");
        item.GetProperty("snapshot").GetProperty("predictedCents").GetInt64().Should().Be(1_000_000);
        item.GetProperty("snapshot").GetProperty("actualCents").GetInt64().Should().Be(900_000);
        body.Should().NotContain(user.Id.ToString(), "Staff chỉ thấy con số, không thấy ai là người dùng");
    }

    [Fact]
    public async Task ReportingTwiceWhileTheFirstIsOpenReturnsTheSameFeedback()
    {
        var user = await AccountAsync();
        await SnapshotAsync(user.Id, actual: null);

        var first = await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/forecast/feedback", user.Token, new { reason = "balance_wrong" }));
        var second = await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/forecast/feedback", user.Token, new { reason = "other" }));

        second.GetProperty("id").GetGuid().Should().Be(first.GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("khong-co-ly-do-nay", false)]
    [InlineData("other", true)]
    public async Task AnInvalidReasonOrATooLongNoteIs422(string reason, bool longNote)
    {
        var user = await AccountAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/forecast/feedback", user.Token, new { reason, note = longNote ? new string('a', 301) : null });

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await response.Content.ReadAsStringAsync()).Should().Contain("FORECAST_FEEDBACK_INVALID");
    }

    [Fact]
    public async Task StaffLabelTheCauseAndItIsAuditedWithoutTheUserOrTheNote()
    {
        var user = await AccountAsync();
        var staff = await AccountAsync(UserRole.Staff);
        var created = await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/forecast/feedback", user.Token, new { reason = "wrong_bill", note = "Bí mật của tôi" }));
        var id = created.GetProperty("id").GetGuid();

        var labeled = await SendAsync(HttpMethod.Patch, $"/api/v1/ops/forecast/feedback/{id}", staff.Token, new { cause = "recurring_misread" });

        labeled.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(labeled);
        (data.GetProperty("status").GetString(), data.GetProperty("cause").GetString()).Should().Be(("labeled", "recurring_misread"));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var audit = db.AuditLogs.Where(a => a.EventType == AuditEvents.OpsForecastFeedbackLabeled && a.UserId == staff.Id).ToList();
        audit.Should().ContainSingle();
        audit[0].Metadata.Should().Contain("recurring_misread").And.NotContain("Bí mật").And.NotContain(user.Id.ToString());
    }

    [Fact]
    public async Task LabellingWithAnUnknownCauseIs422AndAnUnknownFeedbackIs404()
    {
        var staff = await AccountAsync(UserRole.Staff);

        (await SendAsync(HttpMethod.Patch, $"/api/v1/ops/forecast/feedback/{Guid.NewGuid()}", staff.Token, new { cause = "other" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Patch, $"/api/v1/ops/forecast/feedback/{Guid.NewGuid()}", staff.Token, new { cause = "khong-co" })).StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task OnlyOpsStaffReachTheOpsRoutes_AndOnlyAManagerSeesTheAccuracyReport()
    {
        var user = await AccountAsync();
        var staff = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);

        (await SendAsync(HttpMethod.Get, "/api/v1/ops/forecast/feedback", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, "/api/v1/ops/forecast/feedback", user.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/ops/forecast/accuracy", user.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/ops/forecast/feedback", staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/v1/ops/forecast/accuracy", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/ops/forecast/accuracy", manager.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, "/api/v1/forecast/feedback", null, new { reason = "other" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheAccuracyReportMeasuresFilledSnapshotsAndCountsFeedbackByReason()
    {
        var user = await AccountAsync();
        var manager = await AccountAsync(UserRole.OpsManager);
        await SnapshotAsync(user.Id, actual: 900_000, horizon: 7, originDaysAgo: 3);   // sai 100.000 so với dự báo 1.000.000
        await SnapshotAsync(user.Id, actual: null, horizon: 30, originDaysAgo: 1);       // còn chờ tới ngày của mốc
        await SendAsync(HttpMethod.Post, "/api/v1/forecast/feedback", user.Token, new { reason = "unusual_spending" });

        var report = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/ops/forecast/accuracy?windowDays=30", manager.Token));

        report.GetProperty("windowDays").GetInt32().Should().Be(30);
        report.GetProperty("snapshotsFilled").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        report.GetProperty("snapshotsPending").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        var week = report.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("horizonDays").GetInt32() == 7 && r.GetProperty("confidence").GetString() == "all");
        week.GetProperty("samples").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        report.GetProperty("feedbackByReason").EnumerateArray().Should().Contain(c => c.GetProperty("key").GetString() == "unusual_spending");
        report.GetRawText().Should().NotContain(user.Id.ToString());
    }
}
