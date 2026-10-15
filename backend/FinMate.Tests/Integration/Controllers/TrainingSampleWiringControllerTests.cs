using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Nối dây thật từ HTTP đến ai-service giả: quét → lưu giao dịch → rút đồng ý. Các test đơn vị chứng minh từng mảnh; ở đây chứng minh handler THẬT
/// được tiêm dịch vụ thu mẫu (nếu quên đăng ký DI thì tính năng tắt im lặng và chỉ các test này đỏ).
/// </summary>
[Collection("Integration")]
public class TrainingSampleWiringControllerTests : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client;

    public TrainingSampleWiringControllerTests(AuthApiFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOcrServiceClient>();
            services.AddSingleton<IOcrServiceClient, ScanByMerchantOcr>();
        })).CreateClient();
    }

    private sealed class ScanByMerchantOcr : IOcrServiceClient
    {
        public bool IsConfigured => true;

        public Task<OcrServiceResponse> AnalyzeAsync(OcrServiceRequest request, CancellationToken ct = default)
            => Task.FromResult(new OcrServiceResponse(
                true, 16_000, request.FileName, request.FileName, [request.FileName], "dining", "brand", false, null, "text"));
    }

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Wiring Test User"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        var me = await DataOf(await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/users/me", token)));
        return (token, me.GetProperty("id").GetGuid());
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    private Task<HttpResponseMessage> ConsentAsync(string token, string kind, int version) =>
        _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/users/me/consents", token, JsonContent.Create(new { kind, version })));

    private async Task<Guid> ScanAsync(string token, string merchant)
    {
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 });
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "file", merchant);
        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/transactions/scan-receipt", token, form));
        return (await DataOf(response)).GetProperty("scanId").GetGuid();
    }

    private async Task SaveAsync(string token, string merchant, Guid scanId, string slug)
    {
        var account = await DataOf(await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/financial-accounts", token, JsonContent.Create(new
        {
            accountName = "Ví", accountType = "Cash", providerConfigId = (Guid?)null, initialBalanceCents = 1_000_000,
        }))));
        var categories = await DataOf(await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/categories", token)));
        var category = categories.EnumerateArray().First(c => c.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();
        var saved = await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/transactions", token, JsonContent.Create(new
        {
            financialAccountId = account.GetProperty("id").GetGuid(), categoryId = category, amountCents = 16_000, transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow, merchantName = merchant, description = (string?)null, source = "Receipt",
            receiptScanId = scanId, clientRequestId = (Guid?)null, receiptGroup = (string?)null,
        })));
        saved.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ConsentedUser_ScanSendsASample_SavingSendsTheFinalLabel_WithdrawingErases()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        (await ConsentAsync(token, "training", ConsentVersions.Training)).StatusCode.Should().Be(HttpStatusCode.OK);
        var merchant = $"Wiring {Guid.NewGuid():N}";

        var scanId = await ScanAsync(token, merchant);
        FakeAIServiceClient.SamplesFor(scanId).Should().ContainSingle(s => s.UserId == userId);

        await SaveAsync(token, merchant, scanId, "shopping");
        FakeAIServiceClient.LabelsFor(scanId).Should().ContainSingle(l => l.UserId == userId && l.FinalSlug == "shopping");

        (await _client.SendAsync(Authed(HttpMethod.Delete, "/api/v1/users/me/consents/training", token))).StatusCode.Should().Be(HttpStatusCode.OK);
        FakeAIServiceClient.WasErased(userId).Should().BeTrue();
    }

    [Fact]
    public async Task UserWhoNeverConsented_NothingIsSentToTheAiService()
    {
        var (token, _) = await RegisterAndLoginAsync();
        var merchant = $"Wiring {Guid.NewGuid():N}";

        var scanId = await ScanAsync(token, merchant);
        await SaveAsync(token, merchant, scanId, "shopping");

        FakeAIServiceClient.SamplesFor(scanId).Should().BeEmpty();
        FakeAIServiceClient.LabelsFor(scanId).Should().BeEmpty();
    }
}
