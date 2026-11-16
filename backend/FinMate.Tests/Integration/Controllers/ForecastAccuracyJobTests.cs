using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Đủ vòng của job đo dự báo trên dữ liệu thật: chụp, chạy lại không tạo trùng, rồi tới ngày của mốc thì điền số dư thực tế.</summary>
[Collection("Integration")]
public class ForecastAccuracyJobTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ForecastAccuracyJobTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record IdData(Guid Id);
    private record CategoryData(Guid Id, string Slug);

    private async Task<string> NewUserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Accuracy Test"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, string token, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Wrapper<T>>(JsonOptions))!.Data;
    }

    private Task<IdData> SpendAsync(string token, Guid wallet, Guid category, long amount, int daysAgo)
        => SendAsync<IdData>(HttpMethod.Post, "/api/v1/transactions", token, new
        {
            financialAccountId = wallet, categoryId = category, amountCents = amount, transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo), merchantName = "Quán cơm",
        }, HttpStatusCode.Created);

    [Fact]
    public async Task ItSnapshotsOncePerDayThenFillsTheActualBalanceWhenTheHorizonDayHasPassed()
    {
        var token = await NewUserTokenAsync();
        var userId = (await SendAsync<IdData>(HttpMethod.Get, "/api/v1/users/me", token)).Id;
        var wallet = await SendAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token,
            new { accountName = "Tiền mặt", accountType = "Cash", initialBalanceCents = 5_000_000 }, HttpStatusCode.Created);
        // Không lấy danh mục cho vay/mượn: giao dịch của nó không phải thu chi thật nên dự báo loại ra.
        var category = (await SendAsync<List<CategoryData>>(HttpMethod.Get, "/api/v1/categories", token)).First(c => c.Slug != FinMate.Domain.Entities.Category.LoanSlug).Id;
        for (var daysAgo = 1; daysAgo <= 10; daysAgo++)
        {
            await SpendAsync(token, wallet.Id, category, 50_000, daysAgo);   // lịch sử gần đây: để người này được coi là có hoạt động
        }

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IForecastAccuracyService>();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var today = VietnamTime.Today();

        // Chụp: ba mốc, một lần mỗi ngày.
        await service.CaptureAsync(today);
        var snapshots = await db.ForecastSnapshots.Where(s => s.UserId == userId).OrderBy(s => s.HorizonDays).ToListAsync();
        snapshots.Select(s => s.HorizonDays).Should().Equal(7, 14, 30);
        snapshots.Should().OnlyContain(s => s.OriginDate == today && s.ActualCents == null && s.Method == ForecastMethods.MedianBand);
        var start = snapshots[0].StartBalanceCents;
        start.Should().Be(5_000_000 - (10 * 50_000));

        (await service.CaptureAsync(today)).Should().Be(0, "chạy lại trong ngày không tạo bản chụp trùng");

        // Lùi bản chụp 20 ngày để mốc 7 và 14 ngày đã qua, mốc 30 ngày thì chưa.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE forecast_snapshots SET origin_date = origin_date - 20, horizon_date = horizon_date - 20 WHERE user_id = {0}", userId);
        db.ChangeTracker.Clear();
        await SpendAsync(token, wallet.Id, category, 120_000, daysAgo: 18);   // nằm trong khoảng của mốc 7 ngày (từ -20 tới -13)

        (await service.FillActualsAsync(today)).Should().Be(2);

        var filled = await db.ForecastSnapshots.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.HorizonDays).ToListAsync();
        // Mốc 7 ngày (hết ngày -13): chỉ khoản chi 120.000 ở ngày -18. Mốc 14 ngày (hết ngày -6): thêm năm khoản 50.000 ở các ngày -10..-6.
        filled[0].ActualCents.Should().Be(start - 120_000);
        filled[1].ActualCents.Should().Be(start - 120_000 - (5 * 50_000));
        filled[2].ActualCents.Should().BeNull("mốc 30 ngày chưa tới");
        filled[0].ActualFilledAt.Should().NotBeNull();
    }
}
