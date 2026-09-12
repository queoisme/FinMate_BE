using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Admin.Queries;

public class GetAiStatsQueryHandler : IGetAiStatsQueryHandler
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

    private readonly IAiStatsRepository _aiStatsRepository;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly ILogger<GetAiStatsQueryHandler> _logger;

    public GetAiStatsQueryHandler(
        IAiStatsRepository aiStatsRepository,
        IAIServiceClient aiServiceClient,
        ILogger<GetAiStatsQueryHandler> logger)
    {
        _aiStatsRepository = aiStatsRepository;
        _aiServiceClient = aiServiceClient;
        _logger = logger;
    }

    public async Task<AiStatsDto> HandleAsync(
        GetAiStatsQuery query, CancellationToken ct = default)
    {
        var to = query.To ?? DateTimeOffset.UtcNow;
        var from = query.From ?? to - DefaultWindow;

        var production = await _aiStatsRepository.GetProductionStatsAsync(from, to, ct);

        // Hai nửa của màn hình này nằm ở hai hệ thống khác nhau và nửa thứ hai đi qua mạng.
        // Để exception thoát ra sẽ biến "AI Service đang restart" thành "màn hình quản trị sập"
        // — trong khi nửa quan trọng hơn (AI đang chạy tốt đến đâu) vốn nằm ngay trong DB này.
        AiServiceStatsDto? aiService = null;
        string? aiServiceError = null;
        try
        {
            var stats = await _aiServiceClient.GetStatsAsync(ct);
            aiService = ToDto(stats);
        }
        catch (AIServiceUnavailableException ex)
        {
            aiServiceError = ex.Message;
            _logger.LogWarning("AI Service stats không lấy được: {Reason}", ex.Message);
        }

        return new AiStatsDto(ToDto(production, from, to), aiService, aiServiceError);
    }

    private static AiProductionStatsDto ToDto(
        AiProductionStats stats, DateTimeOffset from, DateTimeOffset to) => new(
        from,
        to,
        stats.TotalAnalyzed,
        stats.ByPipelineResult,
        stats.ByPackageName,
        stats.ByClassifierVersion,
        stats.PotentialDuplicates,
        stats.DraftsCreated,
        stats.DraftsConfirmed,
        Rate(stats.DraftsConfirmed, stats.DraftsCreated),
        stats.CategoryCorrections,
        Rate(stats.CategoryCorrections, stats.CategorisedTransactions),
        stats.AvgClassifierConfidence,
        stats.AvgExtractionConfidence,
        stats.AvgCategorizationConfidence,
        stats.AvgProcessingMs,
        stats.MaxProcessingMs);

    /// <summary>null khi chưa có mẫu nào — "chưa biết" không phải là 0%.</summary>
    private static double? Rate(int numerator, int denominator)
        => denominator == 0 ? null : Math.Round((double)numerator / denominator, 4);

    private static AiServiceStatsDto ToDto(AiServiceStats stats) => new(
        stats.Models
            .Select(m => new AiModelStatusDto(
                m.Stage, m.Version, m.TrainedAt, m.Accuracy, m.MacroF1, m.EvaluatedOnSplit))
            .ToList(),
        stats.RawSampleCount,
        stats.LabeledSampleCount,
        stats.UnlabeledSampleCount,
        stats.SplitCounts,
        stats.LastTrainingJob is null
            ? null
            : new AiTrainingJobDto(
                stats.LastTrainingJob.Stage,
                stats.LastTrainingJob.Status,
                stats.LastTrainingJob.SampleCount,
                stats.LastTrainingJob.StartedAt,
                stats.LastTrainingJob.FinishedAt,
                stats.LastTrainingJob.ErrorMessage),
        stats.PendingFeedbackCount);
}
