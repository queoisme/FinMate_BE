using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FinMate.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

public class AuthApiFactory : WebApplicationFactory<FinMate.API.Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@finmate.local";
    public const string AdminPassword = "AdminPassword123!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("finmate_main")
        .WithUsername("finmate")
        .WithPassword("finmate")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("DATABASE_URL", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("REDIS_URL", "localhost:6379");
        Environment.SetEnvironmentVariable("JWT_SECRET", new string('x', 64));
        Environment.SetEnvironmentVariable("JWT_ACCESS_TTL_MINUTES", "15");
        Environment.SetEnvironmentVariable("JWT_REFRESH_TTL_DAYS", "30");
        Environment.SetEnvironmentVariable("GOOGLE_CLIENT_ID", "dummy-client-id.apps.googleusercontent.com");
        Environment.SetEnvironmentVariable("AI_SERVICE_URL", "http://localhost:1");
        Environment.SetEnvironmentVariable("AI_SERVICE_API_KEY", "dummy-ai-service-key-for-tests-min-32-chars");
        Environment.SetEnvironmentVariable("AI_CATEGORY_SYNC_ENABLED", "false"); // không có AI Service trong bộ test
        Environment.SetEnvironmentVariable("HANGFIRE_DASHBOARD_USER", "admin");
        Environment.SetEnvironmentVariable("HANGFIRE_DASHBOARD_PASS", "admin");
        // Phase 8 cần một tài khoản Admin thật để test policy AdminOnly. AdminUserSeeder là
        // idempotent và chỉ thêm đúng một dòng, nên bật nó không ảnh hưởng các test khác.
        Environment.SetEnvironmentVariable("ADMIN_SEED_EMAIL", AdminEmail);
        Environment.SetEnvironmentVariable("ADMIN_SEED_PASSWORD", AdminPassword);

        // Bộ test dùng CHUNG một IP (TestServer không có RemoteIpAddress) nên mọi lần đăng
        // nhập của 449 test rơi vào cùng một phân vùng — để nguyên 10/phút là các test tự
        // chặn lẫn nhau. Hạn mức mặc định KHÔNG nới: nó phân vùng theo user id nên mỗi test
        // có ngăn riêng, và RateLimitingControllerTests dựa vào đúng điều đó để kiểm 429 thật.
        Environment.SetEnvironmentVariable("RATE_LIMIT_AUTH_PER_MINUTE", "100000");

        // Hàng chục test gọi API admin bằng token đăng nhập thường. OpsTwoFactorTests bật lại riêng.
        Environment.SetEnvironmentVariable("OPS_REQUIRE_2FA", "false");

        // 23 file test gọi auth/register rồi đăng nhập ngay; bắt tất cả diễn lại màn OTP chỉ
        // tạo nhiễu trong khi chúng đang kiểm thứ khác. EmailVerificationGateTests bật lại cờ
        // này ở phạm vi của riêng nó để phần chặn vẫn được kiểm thật.
        Environment.SetEnvironmentVariable("REQUIRE_EMAIL_VERIFICATION", "false");
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    /// <summary>Đổi cờ <c>features.groups</c> qua API admin, như cách vận hành thật. Mặc định tắt, nên test nhóm phải bật trước.</summary>
    public async Task SetGroupsEnabledAsync(bool enabled)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMate.Infrastructure.Persistence.FinMateDbContext>();
        var admin = await db.Users.AsNoTracking().SingleAsync(u => u.Email == AdminEmail);
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(admin);

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        const string url = "/api/v1/admin/system-config";

        var current = JsonDocument.Parse(await client.GetStringAsync(url)).RootElement.GetProperty("data");
        if (current.GetProperty("content").GetProperty("features").GetProperty("groups").GetBoolean() == enabled)
        {
            return;
        }

        var saved = await client.PutAsJsonAsync(url, new
        {
            expectedVersion = current.GetProperty("version").GetInt32(),
            content = new { features = new { groups = enabled } },
            note = "kiem thu",
        });
        saved.EnsureSuccessStatusCode();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Real Redis is not available in the test environment — swap IDistributedCache
            // for an in-memory implementation so ICacheService still works end-to-end.
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.AddMemoryCache();

            // Real GoogleTokenVerifier calls Google's servers — swap in a deterministic fake.
            services.RemoveAll<IGoogleTokenVerifier>();
            services.AddScoped<IGoogleTokenVerifier, FakeGoogleTokenVerifier>();

            // AI Service pipeline (Phase 9) doesn't exist yet — swap in a deterministic fake
            // instead of the real Refit client.
            services.RemoveAll<IAIServiceClient>();
            services.AddScoped<IAIServiceClient, FakeAIServiceClient>();
        });
    }
}
