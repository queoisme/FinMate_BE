using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Gọi OCR service (<c>POST /analyze-bill</c>): ảnh → tổng tiền, tên, ngày và chữ ĐÃ lọc.
///
/// HttpClient trần như <see cref="BrevoEmailSender"/> (không thêm package). Khóa nội bộ nằm ở header mặc định của HttpClient,
/// đặt trong Program.cs. Người dùng đi qua dạng SHA-256 như với AI Service.
/// </summary>
public class OcrServiceClient : IOcrServiceClient
{
    private const string NotAReceiptCode = "not_a_receipt";
    private const string UserHashHeader = "X-User-Hash";

    private static readonly JsonSerializerOptions JsonOptions = new() { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    private readonly HttpClient _httpClient;

    public OcrServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool IsConfigured => true;

    /// <summary>Phản hồi của OCR service. Chỉ những trường backend dùng; trường lạ bị bỏ qua.</summary>
    internal record AnalyzeBillBody(
        [property: JsonPropertyName("total_amount")] long? TotalAmount,
        [property: JsonPropertyName("merchant")] string? Merchant,
        [property: JsonPropertyName("merchant_display")] string? MerchantDisplay,
        [property: JsonPropertyName("lines")] List<string>? Lines,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("category_source")] string? CategorySource,
        [property: JsonPropertyName("needs_review")] bool NeedsReview,
        [property: JsonPropertyName("transacted_at")] DateTimeOffset? TransactedAt,
        [property: JsonPropertyName("total_source")] string? TotalSource);

    private record ErrorBody([property: JsonPropertyName("code")] string? Code);

    public async Task<OcrServiceResponse> AnalyzeAsync(OcrServiceRequest request, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        using var image = new ByteArrayContent(request.Image);
        image.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
        content.Add(image, "image", request.FileName);

        using var message = new HttpRequestMessage(HttpMethod.Post, "analyze-bill") { Content = content };
        message.Headers.Add(UserHashHeader, UserIdHasher.Hash(request.UserId));

        try
        {
            using var response = await _httpClient.SendAsync(message, ct);
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity && await IsNotAReceiptAsync(response, ct))
            {
                return Unreadable();
            }

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<AnalyzeBillBody>(JsonOptions, ct)
                ?? throw new HttpRequestException("OCR service trả phản hồi rỗng.");
            return ToResponse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new AIServiceUnavailableException("OCR service không phản hồi hoặc trả lỗi.");
        }
    }

    private static async Task<bool> IsNotAReceiptAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions, ct);
        return error?.Code == NotAReceiptCode;
    }

    private static OcrServiceResponse Unreadable()
        => new(false, null, null, null, [], null, null, false, null, null);

    internal static OcrServiceResponse ToResponse(AnalyzeBillBody body)
        => new(
            true,
            body.TotalAmount,
            body.Merchant,
            body.MerchantDisplay,
            body.Lines ?? [],
            body.Category,
            body.CategorySource,
            body.NeedsReview,
            body.TransactedAt,
            body.TotalSource);
}
