using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class NotificationsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public NotificationsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record FinancialAccountBody(bool Success, FinancialAccountData Data);
    private record FinancialAccountData(Guid Id);
    private record AnalysisBody(bool Success, AnalysisData Data);
    private record AnalysisData(Guid NotificationLogId, string Status, Guid? DraftTransactionId);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    private async Task<Guid> GetSeededProviderIdAsync(string providerKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var provider = await db.ProviderConfigs.FirstAsync(p => p.ProviderKey == providerKey);
        return provider.Id;
    }

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Notification Test User"));

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return loginBody!.Data.AccessToken;
    }

    private HttpRequestMessage AuthedRequest(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<Guid> CreateBankAccountAsync(string accessToken, string providerKey)
    {
        var providerId = await GetSeededProviderIdAsync(providerKey);
        var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", accessToken);
        createRequest.Content = JsonContent.Create(new
        {
            accountName = "Tài khoản test",
            accountType = "Bank",
            providerConfigId = providerId,
            initialBalanceCents = 0,
        });
        var response = await _client.SendAsync(createRequest);
        var body = await response.Content.ReadFromJsonAsync<FinancialAccountBody>(JsonOptions);
        return body!.Data.Id;
    }

    [Fact]
    public async Task Analyze_PackageNotWhitelisted_ReturnsIgnored()
    {
        var accessToken = await RegisterAndLoginAsync();

        var request = AuthedRequest(HttpMethod.Post, "/api/v1/notifications/analyze", accessToken);
        request.Content = JsonContent.Create(new
        {
            packageName = "com.unknown.app",
            notificationTitle = "Thong bao",
            notificationBody = "Noi dung",
            receivedAt = DateTimeOffset.UtcNow,
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AnalysisBody>(JsonOptions);
        body!.Data.Status.Should().Be("Ignored");
        body.Data.DraftTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task Analyze_AIServiceUnavailable_Returns503AndMarksNotificationLogFailed()
    {
        var accessToken = await RegisterAndLoginAsync();
        await CreateBankAccountAsync(accessToken, "vietcombank");
        FakeAIServiceClient.SetUnavailable("com.VCB");

        var request = AuthedRequest(HttpMethod.Post, "/api/v1/notifications/analyze", accessToken);
        request.Content = JsonContent.Create(new
        {
            packageName = "com.VCB",
            notificationTitle = "Thong bao",
            notificationBody = "TK 123: -50,000VND",
            receivedAt = DateTimeOffset.UtcNow,
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be((HttpStatusCode)503);
        var errorBody = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        errorBody!.Error.Code.Should().Be("NOTIFICATION_AI_SERVICE_UNAVAILABLE");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var log = await db.NotificationLogs.OrderByDescending(n => n.CreatedAt).FirstAsync();
        log.Status.Should().Be(FinMate.Domain.Enums.NotificationLogStatus.Failed);
    }

    [Fact]
    public async Task Analyze_FinancialNotification_CreatesDraftTransactionThatCanBeConfirmedLater()
    {
        var accessToken = await RegisterAndLoginAsync();
        await CreateBankAccountAsync(accessToken, "mb_bank");

        FakeAIServiceClient.SetScenario("com.mbmobile", new AnalyzeResponse(
            "financial",
            new ClassifierResult("financial", 0.97),
            new ExtractionResult(75_000, "debit", "Highlands Coffee", "Thanh toan", DateTimeOffset.UtcNow, 2_500_000, 0.91),
            new CategorizationResult("food", 0.89),
            new DuplicateResult(false, null),
            new ModelVersions("1.0", "1.0", "1.0"),
            123));

        var request = AuthedRequest(HttpMethod.Post, "/api/v1/notifications/analyze", accessToken);
        request.Content = JsonContent.Create(new
        {
            packageName = "com.mbmobile",
            notificationTitle = "Thong bao giao dich",
            notificationBody = "TK 123: -75,000VND tai Highlands Coffee",
            receivedAt = DateTimeOffset.UtcNow,
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AnalysisBody>(JsonOptions);
        body!.Data.Status.Should().Be("Processed");
        body.Data.DraftTransactionId.Should().NotBeNull();
    }

    [Fact]
    public async Task Analyze_DuplicateNotificationWithinFiveMinutes_DoesNotCreateSecondDraft()
    {
        var accessToken = await RegisterAndLoginAsync();
        await CreateBankAccountAsync(accessToken, "momo");

        FakeAIServiceClient.SetScenario("com.mservice.momotransfer", new AnalyzeResponse(
            "financial",
            new ClassifierResult("financial", 0.9),
            new ExtractionResult(30_000, "debit", "Grab", "Grab ride", DateTimeOffset.UtcNow, 1_000_000, 0.8),
            new CategorizationResult("transport", 0.8),
            new DuplicateResult(false, null),
            new ModelVersions("1.0", "1.0", "1.0"),
            50));

        var receivedAt = DateTimeOffset.UtcNow;
        var payload = new
        {
            packageName = "com.mservice.momotransfer",
            notificationTitle = "Thanh toan",
            notificationBody = "Ban da thanh toan 30,000d cho Grab",
            receivedAt,
        };

        var firstRequest = AuthedRequest(HttpMethod.Post, "/api/v1/notifications/analyze", accessToken);
        firstRequest.Content = JsonContent.Create(payload);
        var firstResponse = await _client.SendAsync(firstRequest);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<AnalysisBody>(JsonOptions);

        var secondRequest = AuthedRequest(HttpMethod.Post, "/api/v1/notifications/analyze", accessToken);
        secondRequest.Content = JsonContent.Create(payload);
        var secondResponse = await _client.SendAsync(secondRequest);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<AnalysisBody>(JsonOptions);

        secondBody!.Data.NotificationLogId.Should().Be(firstBody!.Data.NotificationLogId);
        secondBody.Data.DraftTransactionId.Should().BeNull();
    }
}
