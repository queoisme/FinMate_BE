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
            UserIdHasher.Hash(request.UserId),
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

    // Ghi nhớ chỉ là phần thêm sau khi giao dịch đã lưu: chờ quá vài giây là bỏ, không để AI Service treo kéo theo việc lưu.
    private static readonly TimeSpan LearnTimeout = TimeSpan.FromSeconds(3);

    public Task LearnMerchantAsync(LearnMerchantRequest request, CancellationToken ct = default)
        => BestEffortAsync(t => _api.BillLearnAsync(
            new BillLearnApiRequest(UserIdHasher.Hash(request.UserId), request.MerchantName, request.CategorySlug), t), ct);

    public Task RecordTrainingSampleAsync(TrainingSampleRequest request, CancellationToken ct = default)
        => BestEffortAsync(t => _api.BillSampleAsync(
            new BillSampleApiRequest(request.ScanId, UserIdHasher.Hash(request.UserId), request.MerchantName, request.Lines, request.TotalAmount,
                request.PredictedSlug, request.PredictedSource), t), ct);

    public Task LabelTrainingSampleAsync(TrainingLabelRequest request, CancellationToken ct = default)
        => BestEffortAsync(t => _api.BillSampleLabelAsync(
            request.ScanId, new BillSampleLabelApiRequest(UserIdHasher.Hash(request.UserId), request.FinalSlug), t), ct);

    public Task EraseTrainingSamplesAsync(Guid userId, CancellationToken ct = default)
        => BestEffortAsync(t => _api.BillSamplesEraseAsync(new BillSampleEraseApiRequest(UserIdHasher.Hash(userId)), t), ct);

    private static async Task BestEffortAsync(Func<CancellationToken, Task> send, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(LearnTimeout);
        try
        {
            await send(timeout.Token);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            // Best-effort theo thiết kế (xem IAIServiceClient) — không chặn luồng chính.
        }
    }

    public async Task SendFeedbackAsync(FeedbackRequest request, CancellationToken ct = default)
    {
        var apiRequest = new FeedbackApiRequest(
            UserIdHasher.Hash(request.TransactionId),
            UserIdHasher.Hash(request.UserId),
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
                response = await _api.OcrAsync(part, UserIdHasher.Hash(request.UserId), ct);
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

    public async Task<ChatParseResponse> ParseChatAsync(ChatParseRequest request, CancellationToken ct = default)
    {
        ChatParseApiResponse response;
        try
        {
            response = await _api.ChatParseAsync(new ChatParseApiRequest(request.Text, UserIdHasher.Hash(request.UserId)), ct);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            throw new AIServiceUnavailableException("AI Service không phản hồi hoặc trả lỗi.");
        }

        static ChatParseEntry Map(ChatEntryApiResult e) => new(
            e.Kind, e.AmountCents, e.Description, e.Merchant, e.CategorySlug, e.IncomeType, e.AutoSaveOk, e.ToOthers, e.OccurredAt, e.CategorySource);

        return new ChatParseResponse(
            response.Intent,
            response.ReplyText,
            response.Entry is null ? null : Map(response.Entry),
            response.Query is null
                ? null
                : new ChatParseQuery(
                    response.Query.Metric,
                    response.Query.PeriodLabel,
                    response.Query.Start,
                    response.Query.End,
                    response.Query.CategorySlug,
                    response.Query.IncomeType),
            response.ProcessingMs,
            response.Entries?.Select(Map).ToList(),
            response.TotalAmount,
            response.TotalMismatch,
            response.Skipped);
    }

    public async Task<BillClassifyResponse> ClassifyBillAsync(BillClassifyRequest request, CancellationToken ct = default)
    {
        BillClassifyApiResponse response;
        try
        {
            response = await _api.BillClassifyAsync(
                new BillClassifyApiRequest(UserIdHasher.Hash(request.UserId), request.MerchantName, request.Lines), ct);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            throw new AIServiceUnavailableException("AI Service không phản hồi hoặc trả lỗi.");
        }

        return new BillClassifyResponse(response.CategorySlug, response.LocalCategory, response.Confidence, response.Source);
    }

    public async Task<ClassifyItemsResponse> ClassifyItemsAsync(ClassifyItemsRequest request, CancellationToken ct = default)
    {
        ClassifyItemsApiResponse response;
        try
        {
            response = await _api.ClassifyItemsAsync(
                new ClassifyItemsApiRequest(
                    request.Items.Select(i => new ClassifyItemApiInput(i.Name, i.Parent)).ToList(),
                    request.MerchantName,
                    UserIdHasher.Hash(request.UserId),
                    request.AllowAi),
                ct);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            throw new AIServiceUnavailableException("AI Service không phản hồi hoặc trả lỗi.");
        }

        return new ClassifyItemsResponse(
            response.Items.Select(i => new ClassifiedItem(i.Index, i.CategorySlug, i.Source)).ToList(), response.AiUsed);
    }
}
