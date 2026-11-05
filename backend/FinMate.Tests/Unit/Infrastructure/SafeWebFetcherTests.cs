using System.Net;
using System.Text;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.MarketData;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class SafeWebFetcherTests
{
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static SafeWebFetcher Fetcher(HttpMessageHandler handler)
        => new(new Factory(handler), NullLogger<SafeWebFetcher>.Instance, TimeSpan.Zero);

    private static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.8.8.8")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::127.0.0.1")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("2002:7f00:1::")]
    public void InternalAddressesAreBlocked(string address)
        => WebAddressGuard.IsBlocked(IPAddress.Parse(address)).Should().BeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    public void PublicAddressesPass(string address)
        => WebAddressGuard.IsBlocked(IPAddress.Parse(address)).Should().BeFalse();

    [Theory]
    [InlineData("http://example.vn/")]
    [InlineData("ftp://example.vn/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pass@example.vn/")]
    [InlineData("https://example.vn:8443/")]
    [InlineData("https://redis/")]
    [InlineData("https://ai-service/")]
    [InlineData("https://db.internal/")]
    [InlineData("https://printer.local/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://2130706433/")]
    [InlineData("https://0x7f.0.0.1/")]
    public async Task BadUrlsAreRefusedWithoutAnyRequest(string url)
    {
        var handler = new FakeHandler(_ => Html("x"));

        var result = await Fetcher(handler).FetchAsync(new Uri(url));

        result.Outcome.Should().Be(WebFetchOutcome.AddressNotAllowed);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ARedirectToAnInternalAddressIsNotFollowed()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://169.254.169.254/latest/meta-data/");
            return response;
        });

        var result = await Fetcher(handler).FetchAsync(new Uri("https://example.vn/"));

        result.Outcome.Should().Be(WebFetchOutcome.AddressNotAllowed);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ARedirectToHttpIsNotFollowed()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            response.Headers.Location = new Uri("http://example.vn/");
            return response;
        });

        (await Fetcher(handler).FetchAsync(new Uri("https://example.vn/"))).Outcome.Should().Be(WebFetchOutcome.AddressNotAllowed);
    }

    [Fact]
    public async Task ARedirectLoopStops()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://example.vn/again");
            return response;
        });

        (await Fetcher(handler).FetchAsync(new Uri("https://example.vn/"))).Outcome.Should().Be(WebFetchOutcome.TooManyRedirects);
        handler.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task ARelativeRedirectToAPublicPageIsFollowed()
    {
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/new")
            {
                return Html("ok");
            }

            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("/new", UriKind.Relative);
            return response;
        });

        var result = await Fetcher(handler).FetchAsync(new Uri("https://example.vn/old"));

        result.Outcome.Should().Be(WebFetchOutcome.Ok);
        result.Content.Should().Be("ok");
        result.FinalUri.Should().Be(new Uri("https://example.vn/new"));
    }

    /// <summary>Đi qua socket thật: chứng minh chặn ở lúc kết nối, kể cả tên miền phân giải ra IP nội bộ.</summary>
    [Fact]
    public async Task AHostnameResolvingToAnInternalAddressIsStoppedAtConnectTime()
    {
        using var handler = SafeWebFetcher.CreateHandler();

        var result = await Fetcher(handler).FetchAsync(new Uri("https://localhost.localdomain.example/"));
        result.Outcome.Should().BeOneOf(WebFetchOutcome.Failed, WebFetchOutcome.AddressNotAllowed);

        using var client = new HttpClient(handler, disposeHandler: false);
        var direct = () => client.GetAsync("https://localhost/");
        (await direct.Should().ThrowAsync<HttpRequestException>()).Which.InnerException.Should().BeOfType<BlockedAddressException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ARefusalIsReportedAndNotRetried(HttpStatusCode status)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));

        (await Fetcher(handler).FetchAsync(new Uri("https://example.vn/"))).Outcome.Should().Be(WebFetchOutcome.Refused);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ItIdentifiesItselfHonestly()
    {
        string? agent = null;
        var handler = new FakeHandler(request =>
        {
            agent = request.Headers.UserAgent.ToString();
            return Html("x");
        });

        await Fetcher(handler).FetchAsync(new Uri("https://example.vn/"));

        agent.Should().Be("FinMateBot/1.0");
    }

    [Fact]
    public async Task APageOverTheLimitIsRejectedEvenWithoutContentLength()
    {
        var handler = new FakeHandler(_ =>
        {
            var content = new StreamContent(new MemoryStream(new byte[SafeWebFetcher.MaxBytes + 1]));
            content.Headers.TryAddWithoutValidation("Content-Type", "text/html");
            content.Headers.ContentLength = null;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        (await Fetcher(handler).FetchAsync(new Uri("https://example.vn/"))).Outcome.Should().Be(WebFetchOutcome.TooLarge);
    }

    [Fact]
    public async Task BinaryContentIsRejected()
    {
        var handler = new FakeHandler(_ =>
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.TryAddWithoutValidation("Content-Type", "application/pdf");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        (await Fetcher(handler).FetchAsync(new Uri("https://example.vn/"))).Outcome.Should().Be(WebFetchOutcome.UnsupportedContent);
    }

    private sealed class StallingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    [Fact]
    public async Task ABodyThatNeverFinishesCannotHangTheFetch()
    {
        var handler = new FakeHandler(_ =>
        {
            var content = new StreamContent(new StallingStream());
            content.Headers.TryAddWithoutValidation("Content-Type", "text/html");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var fetch = Fetcher(handler).FetchAsync(new Uri("https://example.vn/"));
        var finished = await Task.WhenAny(fetch, Task.Delay(TimeSpan.FromSeconds(30)));

        finished.Should().Be(fetch);
        (await fetch).Outcome.Should().Be(WebFetchOutcome.Failed);
    }

    [Fact]
    public void TheRealHandlerDecompressesAndDoesNotFollowRedirects()
    {
        using var handler = SafeWebFetcher.CreateHandler();

        handler.AllowAutoRedirect.Should().BeFalse();
        handler.AutomaticDecompression.Should().Be(DecompressionMethods.All);
        handler.ConnectCallback.Should().NotBeNull();
        handler.UseCookies.Should().BeFalse();
        handler.UseProxy.Should().BeFalse();
    }
}
