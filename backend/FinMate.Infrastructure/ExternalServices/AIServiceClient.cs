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

    // AGENTS.md §3.1: AI DB không lưu user_id/transaction_id thực — luôn gửi SHA-256 qua HTTP.
    private static string HashGuid(Guid value)
    {
        var bytes = Encoding.UTF8.GetBytes(value.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
