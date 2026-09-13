using Refit;

namespace FinMate.Infrastructure.ExternalServices;

public interface IAIServiceApi
{
    [Post("/api/v1/analyze")]
    Task<AnalyzeApiResponse> AnalyzeAsync([Body] AnalyzeApiRequest request, CancellationToken ct = default);

    [Post("/api/v1/feedback")]
    Task FeedbackAsync([Body] FeedbackApiRequest request, CancellationToken ct = default);

    [Get("/api/v1/stats")]
    Task<StatsApiResponse> StatsAsync(CancellationToken ct = default);

    /// <summary>
    /// Multipart: ảnh chuyển thẳng dạng stream, không base64. Base64 phình 33% cho một thứ
    /// vốn đã tới 5MB, và cả hai đầu đều phải giữ trọn chuỗi đó trong bộ nhớ.
    /// </summary>
    [Multipart]
    [Post("/api/v1/ocr")]
    Task<OcrApiResponse> OcrAsync(
        [AliasAs("file")] StreamPart file,
        [AliasAs("user_id_hash")] string userIdHash,
        CancellationToken ct = default);
}
