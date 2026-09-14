using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Đăng ký/gỡ thiết bị nhận push. Không có bảng này thì server biết user tồn tại nhưng không
/// có đường nào chạm tới máy của họ — mọi thông báo dừng ở log.
/// </summary>
[Collection("Integration")]
public class DevicesControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public DevicesControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);

    private async Task<(string Token, string Email)> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Device Test User"));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return (body!.Data.AccessToken, email);
    }

    private HttpRequestMessage Request(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return request;
    }

    private async Task<List<(Guid UserId, string Token)>> ReadRowsAsync(string fcmToken)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        return await db.DeviceTokens
            .Where(d => d.Token == fcmToken)
            .Select(d => new ValueTuple<Guid, string>(d.UserId, d.Token))
            .ToListAsync();
    }

    [Fact]
    public async Task RegisteringADeviceStoresExactlyOneRow()
    {
        var (token, _) = await RegisterAndLoginAsync();
        var fcmToken = $"fcm-{Guid.NewGuid():N}";

        var response = await _client.SendAsync(Request(
            HttpMethod.Post, "/api/v1/devices", token,
            new RegisterDeviceRequest(fcmToken, DevicePlatform.Android)));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadRowsAsync(fcmToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task RegisteringTheSameTokenTwiceStillLeavesOneRow()
    {
        // Client gọi lại mỗi lần mở app. Mỗi lần thêm một dòng là mỗi thông báo gửi lặp
        // xuống cùng một máy.
        var (token, _) = await RegisterAndLoginAsync();
        var fcmToken = $"fcm-{Guid.NewGuid():N}";
        var payload = new RegisterDeviceRequest(fcmToken, DevicePlatform.Android);

        await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/devices", token, payload));
        var second = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/devices", token, payload));

        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadRowsAsync(fcmToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task ASecondUserOnTheSameHandsetTakesTheTokenOver()
    {
        var (firstUserToken, _) = await RegisterAndLoginAsync();
        var fcmToken = $"fcm-{Guid.NewGuid():N}";
        var payload = new RegisterDeviceRequest(fcmToken, DevicePlatform.Android);

        await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/devices", firstUserToken, payload));
        var firstOwner = (await ReadRowsAsync(fcmToken)).Single().UserId;

        var (secondUserToken, _) = await RegisterAndLoginAsync();
        await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/devices", secondUserToken, payload));

        var rows = await ReadRowsAsync(fcmToken);
        rows.Should().ContainSingle("một máy chỉ thuộc về một người");
        rows.Single().UserId.Should().NotBe(firstOwner,
            "nếu không đổi chủ thì thông báo tài chính của người mới vẫn đẩy xuống cho người cũ đọc");
    }

    [Fact]
    public async Task UnregisteringRemovesTheRow()
    {
        var (token, _) = await RegisterAndLoginAsync();
        var fcmToken = $"fcm-{Guid.NewGuid():N}";

        await _client.SendAsync(Request(
            HttpMethod.Post, "/api/v1/devices", token, new RegisterDeviceRequest(fcmToken, DevicePlatform.Android)));

        var response = await _client.SendAsync(Request(
            HttpMethod.Delete, "/api/v1/devices", token, new UnregisterDeviceRequest(fcmToken)));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadRowsAsync(fcmToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task UnregisteringSomethingThatIsNotThereStillSucceeds()
    {
        // Đăng xuất phải luôn thành công. Phân biệt "đã gỡ rồi" với "chưa từng có" chỉ tổ
        // cho biết token nào đang tồn tại.
        var (token, _) = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(Request(
            HttpMethod.Delete, "/api/v1/devices", token, new UnregisterDeviceRequest("khong-ton-tai")));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OneUserCannotUnregisterAnotherUsersDevice()
    {
        var (victimToken, _) = await RegisterAndLoginAsync();
        var fcmToken = $"fcm-{Guid.NewGuid():N}";
        await _client.SendAsync(Request(
            HttpMethod.Post, "/api/v1/devices", victimToken, new RegisterDeviceRequest(fcmToken, DevicePlatform.Android)));

        var (attackerToken, _) = await RegisterAndLoginAsync();
        var response = await _client.SendAsync(Request(
            HttpMethod.Delete, "/api/v1/devices", attackerToken, new UnregisterDeviceRequest(fcmToken)));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "không tiết lộ token có tồn tại hay không");
        (await ReadRowsAsync(fcmToken)).Should().ContainSingle("thiết bị của người khác phải còn nguyên");
    }

    [Fact]
    public async Task AnEmptyTokenIsRejected()
    {
        var (token, _) = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(Request(
            HttpMethod.Post, "/api/v1/devices", token, new RegisterDeviceRequest("", DevicePlatform.Android)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegisteringWithoutATokenIs401()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/devices", new RegisterDeviceRequest("fcm-abc", DevicePlatform.Android), JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
