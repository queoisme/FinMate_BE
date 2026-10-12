using System.Net;
using System.Text;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>JSON mẫu có đúng các trường stt-service/main.py trả (text, duration_ms, engine, processing_ms).</summary>
public class SttServiceClientTests
{
    private const string Json = """{"text": "ăn phở bốn mươi nghìn", "duration_ms": 2500, "engine": "gipformer1.5-68M-rnnt-fp32", "processing_ms": 410}""";

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            return Task.FromResult(respond(request));
        }
    }

    private static (SttServiceClient, Stub) Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var stub = new Stub(respond);
        return (new SttServiceClient(new HttpClient(stub) { BaseAddress = new Uri("http://stt-service:8000/") }), stub);
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ReadsTranscriptFromServiceResponse()
    {
        var (client, stub) = Client(_ => Reply(HttpStatusCode.OK, Json));

        var result = await client.TranscribeAsync(new SttRequest(Guid.NewGuid(), [1, 2, 3], "voice.m4a", "audio/mp4"));

        result.Text.Should().Be("ăn phở bốn mươi nghìn");
        result.DurationMs.Should().Be(2500);
        stub.Seen!.RequestUri!.AbsolutePath.Should().EndWith("transcribe");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ServiceFailureBecomesAiServiceUnavailable(HttpStatusCode status)
    {
        var (client, _) = Client(_ => Reply(status, "{}"));

        var act = () => client.TranscribeAsync(new SttRequest(Guid.NewGuid(), [1], "a.wav", "audio/wav"));

        await act.Should().ThrowAsync<AIServiceUnavailableException>();
    }

    [Fact]
    public async Task UnreadableAudioIsReportedAsEmptyText()
    {
        var (client, _) = Client(_ => Reply(HttpStatusCode.UnprocessableEntity, """{"detail":"Invalid audio or duration limit exceeded"}"""));

        var result = await client.TranscribeAsync(new SttRequest(Guid.NewGuid(), [1], "a.wav", "audio/wav"));

        result.Text.Should().BeEmpty();
    }
}
