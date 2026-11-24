using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Lệnh học gửi AI Service được xếp hàng ra ngoài request; lỗi xếp hàng không bao giờ làm hỏng việc lưu giao dịch.</summary>
public class AiLearningQueueTests
{
    private readonly Mock<IBackgroundJobClient> _jobs = new();

    private HangfireAiLearningQueue Queue() => new(_jobs.Object, NullLogger<HangfireAiLearningQueue>.Instance);

    [Fact]
    public void LearnMerchantAndFeedbackAreEnqueuedAsTheRightJobMethods()
    {
        var created = new List<Job>();
        _jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>())).Callback<Job, IState>((job, _) => created.Add(job)).Returns("1");
        var user = Guid.NewGuid();

        Queue().LearnMerchant(new LearnMerchantRequest(user, "Highlands", "food"));
        Queue().Feedback(new FeedbackRequest(Guid.NewGuid(), user, null, "com.bank", "food", "shopping", "category_correction"));

        created.Select(j => (j.Type, j.Method.Name)).Should().Equal((typeof(AiLearningJob), nameof(AiLearningJob.LearnMerchantAsync)), (typeof(AiLearningJob), nameof(AiLearningJob.FeedbackAsync)));
        ((LearnMerchantRequest)created[0].Args[0]).CategorySlug.Should().Be("food");
    }

    [Fact]
    public void AFailureToEnqueueIsSwallowed()
    {
        _jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>())).Throws(new InvalidOperationException("hết kết nối"));

        var act = () => Queue().LearnMerchant(new LearnMerchantRequest(Guid.NewGuid(), "Highlands", "food"));

        act.Should().NotThrow();
    }
}
