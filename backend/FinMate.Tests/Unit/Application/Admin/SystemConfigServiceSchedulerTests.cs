using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Economics;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>Lưu cấu hình là đăng ký lại lịch job ngay; cấu hình sai thì không đụng tới lịch.</summary>
public class SystemConfigServiceSchedulerTests
{
    private readonly Mock<ISystemConfigRepository> _repository = new();
    private readonly Mock<IEconomicFeedScheduler> _scheduler = new();

    private SystemConfigService Build()
    {
        _repository.Setup(r => r.TryActivateNextAsync(It.IsAny<SystemConfigVersion>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new SystemConfigService(_repository.Object, Mock.Of<ISystemConfigProvider>(), new SystemConfigDocumentValidator(), Mock.Of<IAuditLogService>(), _scheduler.Object);
    }

    [Fact]
    public async Task SavingAppliesTheNewScheduleRightAway()
    {
        var doc = new SystemConfigDocument { EconomicFeeds = new EconomicFeedsSettings { ScheduleDayOfMonth = 4, ScheduleHour = 7 } };

        await Build().SaveAsync(Guid.NewGuid(), null, 0, doc, "đổi lịch CPI");

        _scheduler.Verify(s => s.Apply(It.Is<EconomicFeedsSettings>(f => f.ScheduleDayOfMonth == 4 && f.ScheduleHour == 7)), Times.Once);
    }

    [Fact]
    public async Task AnInvalidDocumentNeverTouchesTheSchedule()
    {
        var doc = new SystemConfigDocument { EconomicFeeds = new EconomicFeedsSettings { ScheduleDayOfMonth = 31 } };

        var act = () => Build().SaveAsync(Guid.NewGuid(), null, 0, doc, "đổi lịch CPI");

        await act.Should().ThrowAsync<Exception>();
        _scheduler.Verify(s => s.Apply(It.IsAny<EconomicFeedsSettings>()), Times.Never);
    }
}
