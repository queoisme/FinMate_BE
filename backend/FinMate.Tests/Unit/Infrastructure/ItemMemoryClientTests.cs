using System.Net;
using System.Text;
using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Refit;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Ghi nhớ món và xóa bộ nhớ món đi qua Refit thật với handler giả để kiểm đường dẫn và body gửi đi.</summary>
public class ItemMemoryClientTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var response = respond();
            response.RequestMessage = request; // Refit đọc RequestMessage khi dựng ApiException
            return response;
        }
    }

    private static (AIServiceClient Client, StubHandler Handler) ClientReturning(Func<HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var api = RestService.For<IAIServiceApi>(new HttpClient(handler) { BaseAddress = new Uri("http://ai-service:8000") });
        return (new AIServiceClient(api), handler);
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task LearnItems_PostsHashedUserIdAndEntries()
    {
        var (client, handler) = ClientReturning(() => Json("""{"recorded":2}"""));
        var catA = Guid.NewGuid();
        var catB = Guid.NewGuid();

        await client.LearnItemsAsync(new LearnItemsRequest(UserId, [
            new LearnItemEntry("Phin Sua Da", catA, ItemMemoryKinds.Corrected),
            new LearnItemEntry("Banh mi", catB, ItemMemoryKinds.Accepted),
        ]));

        handler.Path.Should().Be("/api/v1/bill/items/memory");
        using var doc = JsonDocument.Parse(handler.Body!);
        var root = doc.RootElement;
        root.GetProperty("user_id_hash").GetString().Should().Be(UserIdHasher.Hash(UserId));
        var items = root.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("name").GetString().Should().Be("Phin Sua Da");
        items[0].GetProperty("category_id").GetGuid().Should().Be(catA);
        items[0].GetProperty("kind").GetString().Should().Be("corrected");
        items[1].GetProperty("kind").GetString().Should().Be("accepted");
        handler.Body.Should().NotContain(UserId.ToString()).And.NotContain(UserId.ToString("N"));
    }

    [Fact]
    public async Task LearnItems_SwallowsAiServiceFailure()
    {
        var (client, _) = ClientReturning(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = () => client.LearnItemsAsync(new LearnItemsRequest(UserId, [new LearnItemEntry("Phin", Guid.NewGuid(), ItemMemoryKinds.Accepted)]));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EraseItemMemory_PostsHashedUserId()
    {
        var (client, handler) = ClientReturning(() => Json("""{"deleted":3}"""));

        await client.EraseItemMemoryAsync(UserId);

        handler.Path.Should().Be("/api/v1/bill/items/memory/erase");
        using var doc = JsonDocument.Parse(handler.Body!);
        doc.RootElement.GetProperty("user_id_hash").GetString().Should().Be(UserIdHasher.Hash(UserId));
        handler.Body.Should().NotContain(UserId.ToString());
    }

    [Fact]
    public async Task EraseItemMemory_PropagatesAiServiceFailure()
    {
        var (client, handler) = ClientReturning(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = () => client.EraseItemMemoryAsync(UserId);

        await act.Should().ThrowAsync<ApiException>();
        handler.Path.Should().Be("/api/v1/bill/items/memory/erase");
    }
}
