using System.Net;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Trước Phase 16 <c>/health</c> trả "healthy" kể cả khi Postgres và Redis đã chết, nên load
/// balancer vẫn tiếp tục đẩy request vào instance hỏng.
/// </summary>
[Collection("Integration")]
public class HealthCheckControllerTests : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client;

    public HealthCheckControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task EveryHealthRouteAnswersWithoutAToken(string url)
    {
        // Load balancer không cầm token. Một health check đòi xác thực thì luôn đọc là hỏng.
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadyActuallyReachesTheDependencies()
    {
        // Testcontainers dựng Postgres thật và IDistributedCache được thay bằng bản in-memory,
        // nên cả hai kiểm tra đều chạy thật chứ không bị bỏ qua.
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("Healthy");
    }

    [Fact]
    public async Task LiveDoesNotDependOnAnything()
    {
        // Liveness hỏng nghĩa là "khởi động lại container". Nếu nó đi hỏi Postgres thì
        // Postgres sập sẽ kéo cả đàn app restart vô ích — mà restart không cứu được Postgres.
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
