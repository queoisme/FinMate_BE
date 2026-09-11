using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities.Gamification;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class MissionResetJobTests
{
    private static readonly DateOnly Today = new(2026, 9, 11);

    private readonly Mock<IMissionRepository> _missionRepository = new();
    private readonly MissionResetJob _job;

    public MissionResetJobTests()
    {
        _job = new MissionResetJob(_missionRepository.Object, NullLogger<MissionResetJob>.Instance);
    }

    [Fact]
    public async Task Rollover_DoesNotPreCreateRowsForTheNewPeriod()
    {
        _missionRepository.Setup(r => r.GetExpiredUserMissionsAsync(Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserMission>());

        await _job.RolloverAsync(Today);

        // Dòng chu kỳ mới sinh lazily khi user hoạt động — tạo sẵn cho mọi user × mọi mission
        // mỗi ngày sẽ đẻ ra hàng loạt dòng tiến độ 0 của người không dùng app.
        _missionRepository.Verify(r => r.AddUserMission(It.IsAny<UserMission>()), Times.Never);
    }

    [Fact]
    public async Task Rollover_LeavesUnfinishedMissionsInPlaceAsHistory()
    {
        var expired = new UserMission
        {
            Id = Guid.NewGuid(),
            Progress = 2,
            IsCompleted = false,
            PeriodStart = Today.AddDays(-1),
            PeriodEnd = Today.AddDays(-1),
        };
        _missionRepository.Setup(r => r.GetExpiredUserMissionsAsync(Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserMission> { expired });

        await _job.RolloverAsync(Today);

        expired.Progress.Should().Be(2);
        expired.IsCompleted.Should().BeFalse();
        _missionRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
