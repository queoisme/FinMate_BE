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
        Environment.SetEnvironmentVariable("HANGFIRE_DASHBOARD_USER", "admin");
        Environment.SetEnvironmentVariable("HANGFIRE_DASHBOARD_PASS", "admin");
        Environment.SetEnvironmentVariable("ADMIN_SEED_EMAIL", "");
        Environment.SetEnvironmentVariable("ADMIN_SEED_PASSWORD", "");
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
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
        });
    }
}
