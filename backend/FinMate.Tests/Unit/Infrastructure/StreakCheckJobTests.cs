using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities.Gamification;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class StreakCheckJobTests
{
    private static readonly DateOnly Today = new(2026, 9, 11);

    private readonly Mock<IGamificationRepository> _gamificationRepository = new();
    private readonly StreakCheckJob _job;

    public StreakCheckJobTests()
    {
        _job = new StreakCheckJob(_gamificationRepository.Object);
    }

    private UserGamification Profile(int streak, int longest, DateOnly? lastActivity) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        CurrentStreakDays = streak,
        LongestStreakDays = longest,
        LastActivityDate = lastActivity,
    };

    private void SetupStale(params UserGamification[] profiles)
        => _gamificationRepository.Setup(r => r.GetProfilesWithStaleStreakAsync(Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profiles.ToList());

    [Fact]
    public async Task Reset_UserInactiveToday_LosesTheCurrentStreak()
    {
        var profile = Profile(streak: 12, longest: 20, lastActivity: Today.AddDays(-1));
        SetupStale(profile);

        await _job.ResetStaleStreaksAsync(Today);

        profile.CurrentStreakDays.Should().Be(0);
    }

    [Fact]
    public async Task Reset_KeepsTheLongestStreakRecord()
    {
        var profile = Profile(streak: 12, longest: 20, lastActivity: Today.AddDays(-5));
        SetupStale(profile);

        await _job.ResetStaleStreaksAsync(Today);

        // Kỷ lục là thành tích đã đạt được, không phải trạng thái hiện tại.
        profile.LongestStreakDays.Should().Be(20);
    }

    [Fact]
    public async Task Reset_NobodyToReset_DoesNotWrite()
    {
        SetupStale();

        await _job.ResetStaleStreaksAsync(Today);

        _gamificationRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reset_SavesOnceForTheWholeBatch()
    {
        SetupStale(
            Profile(3, 3, Today.AddDays(-1)),
            Profile(7, 9, Today.AddDays(-2)));

        await _job.ResetStaleStreaksAsync(Today);

        _gamificationRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
