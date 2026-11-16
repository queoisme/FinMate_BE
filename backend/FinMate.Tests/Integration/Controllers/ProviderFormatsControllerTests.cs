using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary><c>GET /api/v1/admin/monitoring/provider-formats</c> (BR-LH-11, S2): chỉ Admin; số đếm và tỉ lệ đọc được theo provider; số ca upload gap theo phiên bản app.</summary>
[Collection("Integration")]
public class ProviderFormatsControllerTests : IClassFixture<AuthApiFactory>
{
    private const string Url = "/api/v1/admin/monitoring/provider-formats";
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ProviderFormatsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> TokenAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user);
    }

    private async Task<HttpResponseMessage> GetAsync(UserRole role)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(role));
        return await _client.SendAsync(request);
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.OpsManager)]
    public async Task NonAdmins_Are403(UserRole role)
        => (await GetAsync(role)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Admin_SeesRatesPerProvider_AndUploadGapsByVersion()
    {
        var key = "test_" + Guid.NewGuid().ToString("N")[..12];
        var package = "com.test." + key;
        var version = "9.9." + Random.Shared.Next(1000, 9999);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.ProviderConfigs.Add(new ProviderConfig { Id = Guid.NewGuid(), ProviderKey = key, DisplayName = "Ngân hàng thử", PackageName = package, AccountType = AccountType.Bank, CreatedAt = now, UpdatedAt = now });
            var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", CreatedAt = now, UpdatedAt = now };
            db.Users.Add(user);
            var results = new[] { PipelineResult.Financial, PipelineResult.Financial, PipelineResult.ExtractionFailed, PipelineResult.NonFinancial, PipelineResult.Error };
            foreach (var result in results)
            {
                var log = new NotificationLog { Id = Guid.NewGuid(), UserId = user.Id, PackageName = package, ContentHash = Guid.NewGuid().ToString("N"), ReceivedAt = now.AddHours(-2), Status = NotificationLogStatus.Processed, CreatedAt = now.AddHours(-2) };
                db.NotificationLogs.Add(log);
                db.AiResults.Add(new AiResult { Id = Guid.NewGuid(), NotificationLogId = log.Id, PipelineResult = result, CreatedAt = now.AddHours(-2) });
            }

            db.ListenerUploadGapDaily.Add(new ListenerUploadGapDaily { Day = VietnamTime.Today(), AppVersion = version, Count = 2 });
            db.ListenerUploadGapDaily.Add(new ListenerUploadGapDaily { Day = VietnamTime.Today().AddDays(-1), AppVersion = version, Count = 3 });
            await db.SaveChangesAsync();
        }

        var response = await GetAsync(UserRole.Admin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        var provider = data.GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("providerKey").GetString() == key);
        provider.GetProperty("total24h").GetInt32().Should().Be(4);                // error không tính
        provider.GetProperty("readableRate24h").GetDouble().Should().Be(75);
        provider.GetProperty("financialShare24h").GetDouble().Should().Be(50);
        provider.GetProperty("total7d").GetInt32().Should().Be(0);
        provider.GetProperty("readableRate7d").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("uploadGapsByAppVersion").GetProperty(version).GetInt32().Should().Be(5);
    }
}
