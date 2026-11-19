using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Forecasting;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>"Nhắc tôi" từ đề xuất "hoãn mua tới ngày" của agent, qua API thật: hẹn ngày tương lai, hẹn lại cùng ngày không tạo thêm, ngày không hợp lệ bị từ chối, cần đăng nhập.</summary>
[Collection("Integration")]
public class PurchaseReminderControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Url = "/api/v1/forecast/purchase-reminders";

    private readonly HttpClient _client;

    public PurchaseReminderControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);
    private record ReminderData(DateOnly Date, DateTimeOffset RemindAt, bool Created);

    private async Task<string> NewUserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Reminder Test"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<HttpResponseMessage> PostAsync(string? token, DateOnly date)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(new { date = date.ToString("yyyy-MM-dd") }) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task ARemindersIsCreatedOnceAndAskingAgainForTheSameDayCreatesNothingNew()
    {
        var token = await NewUserTokenAsync();
        var date = VietnamTime.Today().AddDays(12);

        var first = await PostAsync(token, date);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await first.Content.ReadFromJsonAsync<Wrapper<ReminderData>>(JsonOptions))!.Data;
        created.Created.Should().BeTrue();
        created.RemindAt.Should().Be(new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), TimeSpan.FromHours(7)));

        var again = await PostAsync(token, date);
        (await again.Content.ReadFromJsonAsync<Wrapper<ReminderData>>(JsonOptions))!.Data.Created.Should().BeFalse();
    }

    [Fact]
    public async Task TodayIsRejectedWithTheReminderErrorCode()
    {
        var response = await PostAsync(await NewUserTokenAsync(), VietnamTime.Today());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be(PurchaseReminderErrorCodes.InvalidDate);
    }

    [Fact]
    public async Task WithoutALoginTheEndpointIsClosed()
    {
        (await PostAsync(null, VietnamTime.Today().AddDays(5))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
