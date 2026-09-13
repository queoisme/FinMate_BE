using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using Refit;

namespace FinMate.Infrastructure.ExternalServices;

public class AIServiceClient : IAIServiceClient
{
    private readonly IAIServiceApi _api;

    public AIServiceClient(IAIServiceApi api)
    {
        _api = api;
    }

    public async Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request, CancellationToken ct = default)
    {
        var apiRequest = new AnalyzeApiRequest(
            request.BackendRequestId,
            HashGuid(request.UserId),
            request.PackageName,
            request.NotificationTitle,
            request.NotificationBody,
            request.ReceivedAt);

        AnalyzeApiResponse response;
        try
        {
            response = await _api.AnalyzeAsync(apiRequest, ct);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            throw new AIServiceUnavailableException("AI Service không phản hồi hoặc trả lỗi.");
        }

        return new AnalyzeResponse(
            response.PipelineResult,
            new ClassifierResult(response.Classifier.Label, response.Classifier.Confidence),
            response.Extraction is null
                ? null
                : new ExtractionResult(
                    response.Extraction.AmountCents,
                    response.Extraction.TransactionType,
                    response.Extraction.MerchantName,
                    response.Extraction.Description,
                    response.Extraction.TransactedAt,
                    response.Extraction.BalanceAfterCents,
                    response.Extraction.Confidence),
            response.Categorization is null
                ? null
                : new CategorizationResult(response.Categorization.CategorySlug, response.Categorization.Confidence),
            response.Duplicate is null
                ? null
                : new DuplicateResult(response.Duplicate.IsPotentialDuplicate, response.Duplicate.DuplicateRequestId),
            response.ModelVersions is null
                ? null
                : new ModelVersions(response.ModelVersions.Classifier, response.ModelVersions.Extractor, response.ModelVersions.Categorizer),
            response.ProcessingMs);
    }

    public async Task SendFeedbackAsync(FeedbackRequest request, CancellationToken ct = default)
    {
        var apiRequest = new FeedbackApiRequest(
            HashGuid(request.TransactionId),
            HashGuid(request.UserId),
            request.PipelineRequestId,
            request.PackageName,
            request.PredictedCategory,
            request.CorrectedCategory,
            request.FeedbackType);

        try
        {
            await _api.FeedbackAsync(apiRequest, ct);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            // Best-effort theo thiết kế (xem IAIServiceClient) — không chặn luồng chính.
        }
    }

    public async Task<AiServiceStats> GetStatsAsync(CancellationToken ct = default)
    {
        StatsApiResponse response;
        try
        {
            response = await _api.StatsAsync(ct);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            throw new AIServiceUnavailableException("AI Service không phản hồi hoặc trả lỗi.");
        }

        return new AiServiceStats(
            response.Models
                .Select(m => new AiModelStatus(
                    m.Stage, m.Version, m.TrainedAt, m.Accuracy, m.MacroF1, m.EvaluatedOnSplit))
                .ToList(),
            response.RawSampleCount,
            response.LabeledSampleCount,
            response.UnlabeledSampleCount,
            response.SplitCounts,
            response.LastTrainingJob is null
                ? null
                : new AiTrainingJobStatus(
                    response.LastTrainingJob.Stage,
                    response.LastTrainingJob.Status,
                    response.LastTrainingJob.SampleCount,
                    response.LastTrainingJob.StartedAt,
                    response.LastTrainingJob.FinishedAt,
                    response.LastTrainingJob.ErrorMessage),
            response.PendingFeedbackCount);
    }

    public async Task<ScanReceiptResponse> ScanReceiptAsync(
        ScanReceiptRequest request, CancellationToken ct = default)
    {
        OcrApiResponse response;
        // MemoryStream phải sống tới khi Refit ghi xong multipart body, nên using bao trọn
        // lời gọi chứ không chỉ chỗ dựng StreamPart.
        using (var content = new MemoryStream(request.Image))
        {
            var part = new StreamPart(content, request.FileName, request.ContentType);
            try
            {
                response = await _api.OcrAsync(part, HashGuid(request.UserId), ct);
            }
            catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
            {
                throw new AIServiceUnavailableException("AI Service không phản hồi hoặc trả lỗi.");
            }
        }

        return new ScanReceiptResponse(
            response.OcrResult,
            response.Extraction is null
                ? null
                : new ExtractionResult(
                    response.Extraction.AmountCents,
                    response.Extraction.TransactionType,
                    response.Extraction.MerchantName,
                    response.Extraction.Description,
                    response.Extraction.TransactedAt,
                    response.Extraction.BalanceAfterCents,
                    response.Extraction.Confidence),
            response.Categorization is null
                ? null
                : new CategorizationResult(
                    response.Categorization.CategorySlug, response.Categorization.Confidence),
            response.ProcessingMs);
    }

    // AGENTS.md §3.1: AI DB không lưu user_id/transaction_id thực — luôn gửi SHA-256 qua HTTP.
    private static string HashGuid(Guid value)
    {
        var bytes = Encoding.UTF8.GetBytes(value.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
