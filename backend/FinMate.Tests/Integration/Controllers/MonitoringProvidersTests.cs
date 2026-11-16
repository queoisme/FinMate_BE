using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinMate.API;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Monitoring;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Tình huống gốc: key Vision hết hạn mức, OCR tự đọc cục bộ và vẫn báo khỏe, nên chỉ nguồn này phát hiện được.</summary>
[Collection("Integration")]
public class MonitoringProvidersTests : IClassFixture<AuthApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly FakeStatus _status = new();
    private readonly ProviderFailureTracker _tracker = new();
    private readonly DateTimeOffset _start = new(2026, 11, 5, 3, 0, 0, TimeSpan.Zero);

    public MonitoringProvidersTests(AuthApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IServiceProbe>();
            services.RemoveAll<IProviderStatusClient>();
            services.AddSingleton<IProviderStatusClient>(_status);
            services.RemoveAll<IProviderFailureTracker>();
            services.AddSingleton<IProviderFailureTracker>(_tracker);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(new SilentEmail());
        }));
        _client = _factory.CreateClient();
    }

    private sealed class SilentEmail : IEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FakeStatus : IProviderStatusClient
    {
        public OcrStatusBody? Ocr { get; set; }
        public AiStatusBody? Ai { get; set; }

        public Task<OcrStatusBody?> GetOcrStatusAsync(CancellationToken ct = default) => Task.FromResult(Ocr);

        public Task<AiStatusBody?> GetAiStatusAsync(CancellationToken ct = default) => Task.FromResult(Ai);
    }

    /// <summary>Các dịch vụ có tên cố định nên dọn sạch trước mỗi test. Mỗi lớp test có DB riêng và chạy tuần tự.</summary>
    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        await db.OpsIncidents.ExecuteDeleteAsync();
        await db.OpsServiceStatuses.ExecuteDeleteAsync();
    }

    private async Task RunAsync(DateTimeOffset at)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISystemMonitor>().RunAsync(at);
    }

    private async Task<OpsIncident?> IncidentAsync(string service)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().OpsIncidents.AsNoTracking()
            .OrderByDescending(i => i.FirstSeenAt)
            .FirstOrDefaultAsync(i => i.Service == service);
    }

    private async Task<OpsServiceStatus?> StatusAsync(string service)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().OpsServiceStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Service == service);
    }

    private static ProviderHealthBody Enabled(int failures = 0, string? code = null, bool succeeded = true)
        => new("enabled", failures, code, succeeded ? DateTimeOffset.UtcNow : null);

    [Fact]
    public async Task AVisionQuotaProblemOpensAHighIncidentWithTheCodeForAdmins()
    {
        await ResetAsync();
        _status.Ocr = new OcrStatusBody(Enabled(2, "429"));
        await RunAsync(_start);
        (await IncidentAsync("google_vision")).Should().BeNull("chưa đủ ngưỡng");

        _status.Ocr = new OcrStatusBody(Enabled(3, "429"));
        await RunAsync(_start.AddMinutes(1));

        var incident = await IncidentAsync("google_vision");
        incident!.Severity.Should().Be(IncidentSeverity.High);
        incident.Source.Should().Be(IncidentSource.Provider);
        incident.TechnicalDetail.Should().Contain("429");
        incident.Title.Should().NotContain("429");
    }

    [Fact]
    public async Task FuelPricesThatCannotBeConfirmedRepeatedlyOpenANormalIncident()
    {
        await ResetAsync();
        var provider = MonitoredServices.MarketData.Key;
        _tracker.RecordSuccess(provider);
        _tracker.RecordFailure(provider, "NO_AGREEMENT");
        _tracker.RecordFailure(provider, "NO_AGREEMENT");
        await RunAsync(_start);
        (await IncidentAsync(provider)).Should().BeNull("chưa đủ ngưỡng");

        _tracker.RecordFailure(provider, "NO_AGREEMENT");
        await RunAsync(_start.AddMinutes(1));

        var incident = await IncidentAsync(provider);
        incident!.Severity.Should().Be(IncidentSeverity.Normal);
        incident.TechnicalDetail.Should().Contain("NO_AGREEMENT");

        _tracker.RecordSuccess(provider);
        await RunAsync(_start.AddMinutes(2));
        (await IncidentAsync(provider))!.Status.Should().Be(IncidentStatus.Resolved);
    }

    [Fact]
    public async Task ASuccessfulCallClosesItAtOnce()
    {
        await ResetAsync();
        _status.Ocr = new OcrStatusBody(Enabled(5, "403"));
        await RunAsync(_start);

        _status.Ocr = new OcrStatusBody(Enabled());
        await RunAsync(_start.AddMinutes(1));

        (await IncidentAsync("google_vision"))!.Status.Should().Be(IncidentStatus.Resolved);
    }

    [Fact]
    public async Task ARestartedServiceWithZeroFailuresDoesNotCloseAnIncident()
    {
        await ResetAsync();
        _status.Ocr = new OcrStatusBody(Enabled(5, "429"));
        await RunAsync(_start);

        _status.Ocr = new OcrStatusBody(Enabled(0, null, succeeded: false));
        await RunAsync(_start.AddMinutes(1));

        (await IncidentAsync("google_vision"))!.Status.Should().Be(IncidentStatus.Open);
        (await StatusAsync("google_vision"))!.State.Should().Be(MonitoredServiceState.Unknown);
    }

    [Fact]
    public async Task AFailedKeyLookupDoesNotCloseAnIncident()
    {
        await ResetAsync();
        _status.Ocr = new OcrStatusBody(Enabled(5, "429"));
        await RunAsync(_start);

        _status.Ocr = new OcrStatusBody(new ProviderHealthBody("unknown", 5, "429"));
        await RunAsync(_start.AddMinutes(1));

        (await IncidentAsync("google_vision"))!.Status.Should().Be(IncidentStatus.Open);
    }

    [Fact]
    public async Task BackendRestartLeavesBrevoUnknown_AndUnconfiguredBrevoIsDisabled()
    {
        await ResetAsync();
        await RunAsync(_start);
        (await StatusAsync("brevo"))!.State.Should().Be(MonitoredServiceState.Unknown);

        _tracker.RecordNotConfigured("brevo");
        await RunAsync(_start.AddMinutes(1));
        (await StatusAsync("brevo"))!.State.Should().Be(MonitoredServiceState.Disabled);
    }

    [Fact]
    public async Task DisabledVisionIsNotAProblem()
    {
        await ResetAsync();
        _status.Ocr = new OcrStatusBody(new ProviderHealthBody("disabled", 99, "403"));

        await RunAsync(_start);

        (await IncidentAsync("google_vision")).Should().BeNull();
        (await StatusAsync("google_vision"))!.State.Should().Be(MonitoredServiceState.Disabled);
    }

    [Fact]
    public async Task AnOlderServiceWithoutTheApiIsUnknown_NotAFalseAlarm()
    {
        await ResetAsync();
        _status.Ocr = null;
        _status.Ai = null;

        await RunAsync(_start);

        (await IncidentAsync("google_vision")).Should().BeNull();
        (await IncidentAsync("gemini")).Should().BeNull();
        (await StatusAsync("google_vision"))!.State.Should().Be(MonitoredServiceState.Unknown);
    }

    [Fact]
    public async Task LosingTheSignalDoesNotCloseAnOpenIncident()
    {
        await ResetAsync();
        _status.Ocr = new OcrStatusBody(Enabled(5, "429"));
        await RunAsync(_start);

        _status.Ocr = null;
        await RunAsync(_start.AddMinutes(1));

        (await IncidentAsync("google_vision"))!.Status.Should().Be(IncidentStatus.Open);
    }

    [Fact]
    public async Task GeminiFailuresAreHigh_BackupIsNormal()
    {
        await ResetAsync();
        _status.Ai = new AiStatusBody(Enabled(4, "http_500"), Enabled(4, "http_500"));

        await RunAsync(_start);

        (await IncidentAsync("gemini"))!.Severity.Should().Be(IncidentSeverity.High);
        (await IncidentAsync("gemini_backup"))!.Severity.Should().Be(IncidentSeverity.Normal);
    }

    [Fact]
    public async Task BrevoFailuresOpenAnIncident_AndASuccessClosesIt()
    {
        await ResetAsync();
        _tracker.RecordFailure("brevo", "send_failed");
        _tracker.RecordFailure("brevo", "send_failed");
        await RunAsync(_start);
        (await IncidentAsync("brevo")).Should().BeNull();

        _tracker.RecordFailure("brevo", "send_failed");
        await RunAsync(_start.AddMinutes(1));
        (await IncidentAsync("brevo"))!.Status.Should().Be(IncidentStatus.Open);

        _tracker.RecordSuccess("brevo");
        await RunAsync(_start.AddMinutes(2));
        (await IncidentAsync("brevo"))!.Status.Should().Be(IncidentStatus.Resolved);
    }

    [Fact]
    public async Task OcrStatsCountResultsAndEngines_WithoutAnyUserData()
    {
        await ResetAsync();
        var (managerToken, userId) = await ManagerAsync();
        var now = DateTimeOffset.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.ReceiptScans.ExecuteDeleteAsync();
            db.ReceiptScans.AddRange(
                Scan(userId, ReceiptScanResults.Success, ReceiptScanEngines.GoogleVision, now.AddHours(-1), "Cửa hàng bí mật"),
                Scan(userId, ReceiptScanResults.Success, ReceiptScanEngines.Local, now.AddHours(-2), "Cửa hàng bí mật"),
                Scan(userId, ReceiptScanResults.Unreadable, ReceiptScanEngines.Local, now.AddHours(-3), null),
                Scan(userId, ReceiptScanResults.NoAmount, null, now.AddHours(-4), null),
                Scan(userId, ReceiptScanResults.Success, ReceiptScanEngines.GoogleVision, now.AddDays(-3), null),
                Scan(userId, ReceiptScanResults.Success, ReceiptScanEngines.GoogleVision, now.AddDays(-30), null));
            await db.SaveChangesAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ops/monitoring/ocr");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        text.Should().NotContain("Cửa hàng bí mật").And.NotContain(userId.ToString());

        var data = JsonDocument.Parse(text).RootElement.GetProperty("data");
        var day = data.GetProperty("last24Hours");
        day.GetProperty("total").GetInt32().Should().Be(4);
        day.GetProperty("success").GetInt32().Should().Be(2);
        day.GetProperty("googleVision").GetInt32().Should().Be(1);
        day.GetProperty("local").GetInt32().Should().Be(2);
        day.GetProperty("unknownEngine").GetInt32().Should().Be(1);
        day.GetProperty("readableRate").GetDouble().Should().Be(0.5);
        day.GetProperty("localRate").GetDouble().Should().BeApproximately(2.0 / 3, 0.001);
        data.GetProperty("last7Days").GetProperty("total").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task OcrStatsAreClosedToStaff()
    {
        var (token, _) = await AccountAsync(UserRole.Staff);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ops/monitoring/ocr");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static ReceiptScan Scan(Guid userId, string result, string? engine, DateTimeOffset at, string? merchant)
        => new() { Id = Guid.NewGuid(), UserId = userId, OcrResult = result, OcrEngine = engine, CreatedAt = at, MerchantName = merchant };

    private Task<(string Token, Guid Id)> ManagerAsync() => AccountAsync(UserRole.OpsManager);

    private async Task<(string Token, Guid Id)> AccountAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user), user.Id);
    }
}
