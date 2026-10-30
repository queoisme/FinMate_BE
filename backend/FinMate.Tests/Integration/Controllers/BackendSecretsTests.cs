using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMate.API;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Key bên thứ ba CỦA BACKEND (Brevo, Google OAuth, Firebase) qua đúng API admin dùng cho key của AI Service: lưu mã hóa ở DB của backend, trang chỉ thấy 4 ký tự cuối, giá trị rõ không ở
/// DB / phản hồi / nhật ký, đọc lại được bằng <see cref="ISecretVault"/>, và chưa có khóa chủ thì từ chối chứ không lưu rõ.
/// </summary>
[Collection("Integration")]
public class BackendSecretsTests : IClassFixture<AuthApiFactory>
{
    private const string Master = "25FD-4k4PLP9WajKvg-hcbqnVhvEcpPi_yswo4AOk4Q=";
    private const string Key = "xkeysib-0123456789abcdef-TESTKEY";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class DownAiClient : IAiRulesAdminClient
    {
        public Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default)
            => throw new HttpRequestException("ai-service down");
    }

    private readonly AuthApiFactory _factory;

    public BackendSecretsTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> With(string? masterKey) => _factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("SECRETS_MASTER_KEY", masterKey ?? "");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiRulesAdminClient>();
            services.AddSingleton<IAiRulesAdminClient, DownAiClient>();
        });
    });

    private record AuthBody(bool Success, AuthData Data);
    private record AuthData(string AccessToken);

    private static async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword));
        var token = (await login.Content.ReadFromJsonAsync<AuthBody>(JsonOptions))!.Data.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ItemAsync(HttpClient client, string name)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/secrets", JsonOptions);
        return list.GetProperty("data").GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == name).Clone();
    }

    [Fact]
    public async Task SetListUseDelete_KeepsTheValueEncrypted_AndOutOfEveryResponse()
    {
        await using var factory = With(Master);
        var client = await AdminAsync(factory);

        var put = await client.PutAsync("/api/v1/admin/secrets/brevo_api_key", Body($$"""{"value":"{{Key}}","reason":"đổi key quý 4"}"""));
        var putBody = await put.Content.ReadAsStringAsync();

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        putBody.Should().NotContain(Key);

        var item = await ItemAsync(client, "brevo_api_key");  // AI Service đang ngừng: danh sách vẫn có key của backend
        (item.GetProperty("source").GetString(), item.GetProperty("last4").GetString(), item.GetProperty("owner").GetString(), item.GetProperty("can_store").GetBoolean())
            .Should().Be(("db", Key[^4..], "backend", true));
        (await client.GetStringAsync("/api/v1/admin/secrets")).Should().NotContain(Key);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            var row = await db.SecretEntries.AsNoTracking().SingleAsync(s => s.Name == "brevo_api_key");
            Encoding.UTF8.GetString(row.Ciphertext).Should().NotContain("TESTKEY");
            (await scope.ServiceProvider.GetRequiredService<ISecretVault>().GetAsync("brevo_api_key")).Should().Be(Key);
        }

        var audit = await (await client.GetAsync($"/api/v1/admin/audit-logs?eventType={AuditEvents.AdminSecretChanged}&limit=100")).Content.ReadAsStringAsync();
        audit.Should().Contain("brevo_api_key").And.NotContain(Key);

        (await client.DeleteAsync("/api/v1/admin/secrets/brevo_api_key?reason=thu%20hoi")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ItemAsync(client, "brevo_api_key")).GetProperty("source").GetString().Should().Be("none");
    }

    [Fact]
    public async Task WithoutAMasterKey_SavingIsRefused_AndNothingIsWritten()
    {
        await using var factory = With(null);
        var client = await AdminAsync(factory);

        var put = await client.PutAsync("/api/v1/admin/secrets/google_client_secret", Body($$"""{"value":"{{Key}}","reason":"thử khi chưa có khóa"}"""));

        put.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await put.Content.ReadAsStringAsync()).Should().Contain("SECRETS_UNAVAILABLE").And.NotContain(Key);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().SecretEntries.AnyAsync(s => s.Name == "google_client_secret")).Should().BeFalse();
    }

    [Theory]
    [InlineData("fcm_credentials_json", "không phải json", "SECRET_INVALID")]
    [InlineData("brevo_api_key", "co khoang trang", "SECRET_INVALID")]
    public async Task AMalformedValue_IsRejected_WithoutEchoingIt(string name, string value, string code)
    {
        await using var factory = With(Master);
        var client = await AdminAsync(factory);

        var put = await client.PutAsync($"/api/v1/admin/secrets/{name}", Body(JsonSerializer.Serialize(new { value, reason = "thử giá trị sai" })));

        put.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await put.Content.ReadAsStringAsync()).Should().Contain(code).And.NotContain(value);
    }
}
