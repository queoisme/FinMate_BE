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
}
