using FinMate.Application.Common.Interfaces;
using FinMate.Application.SavingGoals.Queries;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.SavingGoals;

public class GetGoalProgressQueryHandlerTests
{
    private readonly Mock<ISavingGoalRepository> _savingGoalRepository = new();
    private readonly GetGoalProgressQueryHandler _handler;

    public GetGoalProgressQueryHandlerTests()
    {
        _handler = new GetGoalProgressQueryHandler(_savingGoalRepository.Object);
    }

    private SavingGoal Arrange(
        Guid userId,
        long targetCents,
        long savedCents,
        DateTimeOffset createdAt,
        DateTimeOffset? deadline,
        SavingGoalStatus status = SavingGoalStatus.Active)
    {
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Mua laptop",
            TargetCents = targetCents,
            SavedCents = savedCents,
            Status = status,
            CreatedAt = createdAt,
            Deadline = deadline,
        };

        _savingGoalRepository.Setup(r => r.GetByIdAsync(goal.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(goal);
        _savingGoalRepository.Setup(r => r.GetContributionsAsync(goal.Id, userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoalContribution>());

        return goal;
    }

    [Fact]
    public async Task HandleAsync_NoDeadline_IsAlwaysOnTrack()
    {
        var userId = Guid.NewGuid();
        var goal = Arrange(userId, 10_000_000, 0, DateTimeOffset.UtcNow.AddDays(-300), deadline: null);

        var result = await _handler.HandleAsync(new GetGoalProgressQuery(userId, goal.Id));

        result.IsOnTrack.Should().BeTrue();
        result.DaysRemaining.Should().BeNull();
        result.RequiredPerDayCents.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_HalfwayThroughWithHalfSaved_IsOnTrack()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var goal = Arrange(userId, 10_000_000, 5_000_000, now.AddDays(-50), now.AddDays(50));

        var result = await _handler.HandleAsync(new GetGoalProgressQuery(userId, goal.Id));

        result.IsOnTrack.Should().BeTrue();
        result.PercentComplete.Should().Be(50);
        result.RemainingCents.Should().Be(5_000_000);
    }

    [Fact]
    public async Task HandleAsync_HalfwayThroughWithAlmostNothingSaved_IsNotOnTrack()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var goal = Arrange(userId, 10_000_000, 100_000, now.AddDays(-50), now.AddDays(50));

        var result = await _handler.HandleAsync(new GetGoalProgressQuery(userId, goal.Id));

        result.IsOnTrack.Should().BeFalse();
        result.DaysRemaining.Should().BeInRange(49, 51);
        // Còn thiếu 9.9tr chia cho ~50 ngày.
        result.RequiredPerDayCents.Should().BeInRange(190_000, 210_000);
    }

    [Fact]
    public async Task HandleAsync_PastDeadlineAndUnfinished_IsNotOnTrack()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var goal = Arrange(userId, 10_000_000, 8_000_000, now.AddDays(-100), now.AddDays(-1));

        var result = await _handler.HandleAsync(new GetGoalProgressQuery(userId, goal.Id));

        result.IsOnTrack.Should().BeFalse();
        result.DaysRemaining.Should().BeLessThanOrEqualTo(0);
        // Quá hạn: phần còn thiếu dồn vào 1 ngày thay vì chia cho số ngày âm.
        result.RequiredPerDayCents.Should().Be(2_000_000);
    }

    [Fact]
    public async Task HandleAsync_CompletedGoal_IsOnTrackEvenPastDeadline()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var goal = Arrange(
            userId, 10_000_000, 10_000_000, now.AddDays(-100), now.AddDays(-1), SavingGoalStatus.Completed);

        var result = await _handler.HandleAsync(new GetGoalProgressQuery(userId, goal.Id));

        result.IsOnTrack.Should().BeTrue();
        result.Status.Should().Be("completed");
        result.RequiredPerDayCents.Should().BeNull();
    }
}
