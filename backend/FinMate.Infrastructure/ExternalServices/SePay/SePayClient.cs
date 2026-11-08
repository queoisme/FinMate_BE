using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Application.Subscriptions;

namespace FinMate.Infrastructure.ExternalServices.SePay;

/// <summary>Chỉ đọc trạng thái đơn. URL đến từ cấu hình của đội vận hành, không từ dữ liệu người dùng. Không ghi log phản hồi, vì nó chứa số tiền và thông tin thẻ.</summary>
public sealed class SePayClient : ISePayClient
{
    public const string HttpClientName = "sepay";
    private const string ApprovedStatus = "APPROVED";

    private readonly IHttpClientFactory _http;
    private readonly ISecretVault _vault;
    private readonly SubscriptionOptions _options;
    private readonly IProviderFailureTracker _tracker;

    public SePayClient(IHttpClientFactory http, ISecretVault vault, SubscriptionOptions options, IProviderFailureTracker tracker)
    {
        _tracker = tracker;
        _http = http;
        _vault = vault;
        _options = options;
    }

    public async Task<SePayOrderInfo?> GetOrderAsync(string invoiceNumber, CancellationToken ct = default)
    {
        var secret = await _vault.GetAsync(BackendSecrets.SepaySecretKey, ct);
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(_options.MerchantId) || string.IsNullOrWhiteSpace(_options.ApiBaseUrl))
        {
            _tracker.RecordNotConfigured(MonitoredServices.SePay.Key);
            throw new SePayUnavailableException();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.ApiBaseUrl.TrimEnd('/')}/v1/order/detail/{Uri.EscapeDataString(invoiceNumber)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.MerchantId}:{secret}")));

        try
        {
            using var response = await _http.CreateClient(HttpClientName).SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _tracker.RecordSuccess(MonitoredServices.SePay.Key);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _tracker.RecordFailure(MonitoredServices.SePay.Key, $"http_{(int)response.StatusCode}");
                throw new SePayUnavailableException();
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            _tracker.RecordSuccess(MonitoredServices.SePay.Key);
            return Parse(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _tracker.RecordFailure(MonitoredServices.SePay.Key, ex.GetType().Name);
            throw new SePayUnavailableException();
        }
    }

    public static SePayOrderInfo? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new SePayOrderInfo(Text(data, "order_status"), Amount(data), Text(data, "order_currency"), TransactionId(data));
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>SePay trả số tiền dạng chuỗi, ví dụ <c>"300000.00"</c>. Đọc không được thì null, và đơn sẽ không được coi là khớp.</summary>
    private static decimal? Amount(JsonElement data)
    {
        if (!data.TryGetProperty("order_amount", out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    /// <summary>Đơn có thể có lần thử thất bại trước lần thành công, nên ưu tiên giao dịch đã duyệt. Mã này chỉ để đối chiếu với SePay.</summary>
    private static string? TransactionId(JsonElement data)
    {
        if (!data.TryGetProperty("transactions", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
        {
            return null;
        }

        var approved = items.EnumerateArray().Where(t => Text(t, "transaction_status") == ApprovedStatus).Select(t => Text(t, "id")).FirstOrDefault();
        return approved ?? Text(items[0], "id");
    }
}
