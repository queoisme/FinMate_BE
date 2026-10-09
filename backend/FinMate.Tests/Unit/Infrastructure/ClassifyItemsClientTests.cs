using System.Net;
using System.Text;
using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Refit;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Đi qua Refit thật với handler giả để kiểm chính cái body gửi đi, không chỉ record trung gian.</summary>
public class ClassifyItemsClientTests
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

    private static readonly ClassifyItemsRequest Request = new(
        UserId, "Highlands Coffee", [new ClassifyItemInput("Phin Sua Da", null), new ClassifyItemInput("Topping", 0)], true);

    [Fact]
    public async Task ClassifyItems_SendsHashedUserIdAndNamesOnly()
    {
        var (client, handler) = ClientReturning(() => Json("""{"items":[],"ai_used":false}"""));

        await client.ClassifyItemsAsync(Request);

        handler.Path.Should().Be("/api/v1/bill/classify-items");
        using var doc = JsonDocument.Parse(handler.Body!);
        var root = doc.RootElement;
        root.GetProperty("user_id_hash").GetString().Should().Be(UserIdHasher.Hash(UserId));
        root.GetProperty("merchant_name").GetString().Should().Be("Highlands Coffee");
        root.GetProperty("allow_ai").GetBoolean().Should().BeTrue();
        var items = root.GetProperty("items");
        items[0].GetProperty("name").GetString().Should().Be("Phin Sua Da");
        items[0].GetProperty("parent").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetProperty("parent").GetInt32().Should().Be(0);
        handler.Body.Should().NotContain("amount").And.NotContain("price").And.NotContain(UserId.ToString());
        handler.Body.Should().NotContain(UserId.ToString("N"));
    }

    [Fact]
    public async Task ClassifyItems_MapsResponse()
    {
        var (client, _) = ClientReturning(() => Json(
            """{"items":[{"index":0,"category_slug":"food","source":"rule"},{"index":1,"category_slug":null,"source":"unknown"}],"ai_used":true}"""));

        var result = await client.ClassifyItemsAsync(Request);

        result.AiUsed.Should().BeTrue();
        result.Items.Should().Equal(new ClassifiedItem(0, "food", "rule"), new ClassifiedItem(1, null, "unknown"));
    }

    [Fact]
    public async Task ClassifyItems_OnHttpFailureThrowsAIServiceUnavailable()
    {
        var (client, _) = ClientReturning(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = () => client.ClassifyItemsAsync(Request);

        await act.Should().ThrowAsync<AIServiceUnavailableException>();
    }
}
