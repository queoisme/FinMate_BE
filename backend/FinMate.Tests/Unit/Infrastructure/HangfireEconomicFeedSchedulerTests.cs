using FinMate.Application.Admin.SystemConfig;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Lịch job CPI theo cấu hình: giờ Việt Nam, đổi cấu hình là đổi lịch, tắt là gỡ khỏi lịch.</summary>
public class HangfireEconomicFeedSchedulerTests
{
    [Theory]
    [InlineData(3, 8, "0 1 3 * *")]      // 08:00 VN = 01:00 UTC cùng ngày
    [InlineData(6, 9, "0 2 6 * *")]
    [InlineData(6, 3, "0 20 5 * *")]     // 03:00 VN = 20:00 UTC hôm trước
    [InlineData(1, 3, "0 20 L * *")]     // ngày 1 giờ sớm = cuối tháng trước theo UTC
    [InlineData(28, 23, "0 16 28 * *")]
    public void RegistersTheJobWithTheVietnamTimeConvertedToUtc(int day, int hour, string expectedCron)
    {
        var jobs = new Mock<IRecurringJobManager>();
        string? cron = null;
        jobs.Setup(j => j.AddOrUpdate(HangfireEconomicFeedScheduler.JobId, It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<RecurringJobOptions>()))
            .Callback<string, Job, string, RecurringJobOptions>((_, _, c, _) => cron = c);

        new HangfireEconomicFeedScheduler(jobs.Object).Apply(new EconomicFeedsSettings { ScheduleDayOfMonth = day, ScheduleHour = hour });

        cron.Should().Be(expectedCron);
    }

    [Fact]
    public void DisablingTheScheduleRemovesTheJob()
    {
        var jobs = new Mock<IRecurringJobManager>();

        new HangfireEconomicFeedScheduler(jobs.Object).Apply(new EconomicFeedsSettings { Enabled = false });

        jobs.Verify(j => j.RemoveIfExists(HangfireEconomicFeedScheduler.JobId), Times.Once);
        jobs.Verify(j => j.AddOrUpdate(It.IsAny<string>(), It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<RecurringJobOptions>()), Times.Never);
    }
}
