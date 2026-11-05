using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.MarketData;

/// <summary>Chặn ở lúc mở socket, không ở lúc đọc URL: một tên miền có thể phân giải ra IP nội bộ, kể cả sau khi đã qua bước kiểm tra URL.</summary>
public static class WebAddressGuard
{
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (bytes[0] & 0xFE) == 0xFC // ULA fc00::/7
                || bytes.Take(12).All(b => b == 0) // ::/96 cũ, có thể bọc IPv4 nội bộ
                || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) // tài liệu
                || (bytes[0] == 0 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B) // NAT64 trỏ về IPv4 bất kỳ
                || (bytes[0] == 0x20 && bytes[1] == 0x02); // 6to4 trỏ về IPv4 bất kỳ
        }

        return bytes[0] switch
        {
            0 or 10 or 127 => true,
            100 => bytes[1] is >= 64 and <= 127, // CGNAT
            169 => bytes[1] == 254, // link-local, gồm metadata đám mây
            172 => bytes[1] is >= 16 and <= 31,
            192 => (bytes[1] == 168) || (bytes[1] == 0 && bytes[2] is 0 or 2) || (bytes[1] == 88 && bytes[2] == 99),
            198 => bytes[1] is 18 or 19 || (bytes[1] == 51 && bytes[2] == 100),
            203 => bytes[1] == 0 && bytes[2] == 113,
            >= 224 => true, // multicast và dành riêng
            _ => false,
        };
    }

    /// <summary>Cắm vào <see cref="SocketsHttpHandler.ConnectCallback"/>. Kết nối đúng IP đã kiểm tra, nên không bị đổi IP giữa chừng (DNS rebinding).</summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0 || addresses.Any(IsBlocked))
        {
            throw new BlockedAddressException();
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

public sealed class BlockedAddressException : Exception
{
    public BlockedAddressException() : base("Address not allowed.")
    {
    }
}

public sealed class SafeWebFetcher : IWebPageFetcher
{
    public const string HttpClientName = "market-web";
    public const string UserAgentValue = "FinMateBot/1.0";
    public const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxRedirects = 3;

    /// <summary>Tính cả lúc đọc nội dung. <see cref="HttpClient.Timeout"/> dừng ở phần header, nên một trang gửi từng byte có thể làm treo cả job.</summary>
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(15);

    private static readonly string[] AllowedTypes = ["text/html", "application/xhtml+xml", "application/json", "application/xml", "text/xml", "application/rss+xml", "application/atom+xml", "text/plain"];

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> HostLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> HostLastRequest = new(StringComparer.OrdinalIgnoreCase);

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<SafeWebFetcher> _logger;
    private readonly TimeSpan _minInterval;

    public SafeWebFetcher(IHttpClientFactory clients, ILogger<SafeWebFetcher> logger)
        : this(clients, logger, TimeSpan.FromSeconds(1))
    {
    }

    internal SafeWebFetcher(IHttpClientFactory clients, ILogger<SafeWebFetcher> logger, TimeSpan minInterval)
    {
        _clients = clients;
        _logger = logger;
        _minInterval = minInterval;
    }

    /// <summary>Dựng handler của client. Tự theo redirect phải tắt, để từng bước nhảy đều được kiểm tra.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,

        // Có proxy thì ConnectCallback chỉ thấy địa chỉ proxy, không kiểm tra được trang đích.
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = WebAddressGuard.ConnectAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        UseCookies = false,
    };

    public async Task<WebFetchResult> FetchAsync(Uri uri, CancellationToken ct = default)
    {
        var client = _clients.CreateClient(HttpClientName);
        var current = uri;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TotalTimeout);
        var outer = ct;
        ct = timeout.Token;
        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!IsAllowedUri(current))
                {
                    return new WebFetchResult(WebFetchOutcome.AddressNotAllowed);
                }

                await PaceAsync(current.Host, ct);
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd(UserAgentValue);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var status = (int)response.StatusCode;
                if (status is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired or (HttpStatusCode)451)
                {
                    return new WebFetchResult(WebFetchOutcome.Refused, StatusCode: status);
                }

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    return new WebFetchResult(WebFetchOutcome.NotFound, StatusCode: status);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new WebFetchResult(WebFetchOutcome.Failed, StatusCode: status);
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (contentType is not null && !AllowedTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
                {
                    return new WebFetchResult(WebFetchOutcome.UnsupportedContent, ContentType: contentType, StatusCode: status);
                }

                if (response.Content.Headers.ContentLength > MaxBytes)
                {
                    return new WebFetchResult(WebFetchOutcome.TooLarge, StatusCode: status);
                }

                var bytes = await ReadLimitedAsync(response, ct);
                if (bytes is null)
                {
                    return new WebFetchResult(WebFetchOutcome.TooLarge, StatusCode: status);
                }

                return new WebFetchResult(WebFetchOutcome.Ok, Decode(bytes, response), contentType, current, status);
            }

            return new WebFetchResult(WebFetchOutcome.TooManyRedirects);
        }
        catch (HttpRequestException ex) when (IsBlockedAddress(ex))
        {
            return new WebFetchResult(WebFetchOutcome.AddressNotAllowed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UriFormatException or OperationCanceledException && !outer.IsCancellationRequested)
        {
            // Chỉ log tên kiểu lỗi: thông điệp có thể chứa URL và địa chỉ nội bộ đã phân giải.
            _logger.LogInformation("Web fetch failed: {ErrorType}", ex.GetType().Name);
            return new WebFetchResult(WebFetchOutcome.Failed);
        }
    }

    internal static bool IsAllowedUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
        {
            return false;
        }

        var host = uri.IdnHost;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            return !WebAddressGuard.IsBlocked(literal);
        }

        // Tên trong mạng nội bộ không có dấu chấm: redis, postgres-main, ai-service.
        return host.Contains('.') && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockedAddress(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is BlockedAddressException)
            {
                return true;
            }
        }

        return false;
    }

    private async Task PaceAsync(string host, CancellationToken ct)
    {
        if (_minInterval <= TimeSpan.Zero)
        {
            return;
        }

        var gate = HostLocks.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (HostLastRequest.TryGetValue(host, out var last))
            {
                var wait = last + _minInterval - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }
            }

            HostLastRequest[host] = DateTimeOffset.UtcNow;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string Decode(byte[] bytes, HttpResponseMessage response)
    {
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        try
        {
            return (string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset)).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
