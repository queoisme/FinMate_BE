using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>Gọi stt-service (<c>POST /transcribe</c>): audio → chữ. Khóa nội bộ ở header mặc định của HttpClient (đặt trong Program.cs).</summary>
public class SttServiceClient(HttpClient httpClient) : ISttServiceClient
{
    private record Body([property: JsonPropertyName("text")] string? Text, [property: JsonPropertyName("duration_ms")] int DurationMs);

    public bool IsConfigured => true;

    public async Task<SttResponse> TranscribeAsync(SttRequest request, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        using var audio = new ByteArrayContent(request.Audio);
        audio.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
        content.Add(audio, "file", request.FileName);

        try
        {
            using var response = await httpClient.PostAsync("transcribe", content, ct);
            // 422: audio hỏng hoặc quá 60 giây — người dùng cần ghi lại, không phải lỗi hệ thống.
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity) return new SttResponse("", 0);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<Body>(cancellationToken: ct)
                ?? throw new HttpRequestException("stt-service trả phản hồi rỗng.");
            return new SttResponse(body.Text ?? "", body.DurationMs);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new AIServiceUnavailableException("STT service không phản hồi hoặc trả lỗi.");
        }
    }
}

/// <summary>Khi STT_SERVICE_URL chưa đặt.</summary>
public class DisabledSttServiceClient : ISttServiceClient
{
    public bool IsConfigured => false;

    public Task<SttResponse> TranscribeAsync(SttRequest request, CancellationToken ct = default)
        => throw new AIServiceUnavailableException("STT service chưa được cấu hình (STT_SERVICE_URL).");
}
