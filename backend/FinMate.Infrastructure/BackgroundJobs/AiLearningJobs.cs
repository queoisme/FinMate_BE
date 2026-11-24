using FinMate.Application.Common.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>Chạy các lệnh học đã xếp hàng. <c>IAIServiceClient</c> tự nuốt lỗi và có hạn chờ ngắn, nên không cần thử lại nhiều.</summary>
public class AiLearningJob(IAIServiceClient client)
{
    [AutomaticRetry(Attempts = 2)]
    public Task LearnMerchantAsync(LearnMerchantRequest request) => client.LearnMerchantAsync(request, CancellationToken.None);

    [AutomaticRetry(Attempts = 2)]
    public Task FeedbackAsync(FeedbackRequest request) => client.SendFeedbackAsync(request, CancellationToken.None);
}

/// <summary>Xếp hàng lệnh học qua Hangfire. Lỗi xếp hàng chỉ ghi log (không ghi nội dung: có tên cửa hàng).</summary>
public class HangfireAiLearningQueue(IBackgroundJobClient jobs, ILogger<HangfireAiLearningQueue> logger) : IAiLearningQueue
{
    public void LearnMerchant(LearnMerchantRequest request) => Enqueue(() => jobs.Enqueue<AiLearningJob>(job => job.LearnMerchantAsync(request)));

    public void Feedback(FeedbackRequest request) => Enqueue(() => jobs.Enqueue<AiLearningJob>(job => job.FeedbackAsync(request)));

    private void Enqueue(Func<string> enqueue)
    {
        try
        {
            enqueue();
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not enqueue an AI learning job: {ExceptionType}", ex.GetType().Name);
        }
    }
}
