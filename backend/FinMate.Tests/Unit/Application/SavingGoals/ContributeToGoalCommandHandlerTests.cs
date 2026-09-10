using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.SavingGoals.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.SavingGoals;

public class ContributeToGoalCommandHandlerTests
{
    private readonly Mock<ISavingGoalRepository> _savingGoalRepository = new();
    private readonly Mock<IPushNotificationService> _push = new();
    private readonly ContributeToGoalCommandHandler _handler;

    public ContributeToGoalCommandHandlerTests()
    {
        _handler = new ContributeToGoalCommandHandler(
            _savingGoalRepository.Object, _push.Object, new ContributeToGoalCommandValidator());
    }

    private SavingGoal Arrange(Guid userId, long targetCents, long savedCents, SavingGoalStatus status = SavingGoalStatus.Active)
    {
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Mua laptop",
            TargetCents = targetCents,
            SavedCents = savedCents,
            Status = status,
        };

        _savingGoalRepository.Setup(r => r.GetByIdAsync(goal.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(goal);

        return goal;
    }

    [Fact]
    public async Task HandleAsync_BelowTarget_AddsContributionAndIncrementsSavedCents()
    {
        var userId = Guid.NewGuid();
        var goal = Arrange(userId, targetCents: 20_000_000, savedCents: 5_000_000);

        GoalContribution? saved = null;
        _savingGoalRepository.Setup(r => r.AddContributionAsync(It.IsAny<GoalContribution>(), It.IsAny<CancellationToken>()))
            .Callback<GoalContribution, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        var result = await _handler.HandleAsync(
            new ContributeToGoalCommand(userId, goal.Id, 3_000_000, "Lương tháng 9", null));

        goal.SavedCents.Should().Be(8_000_000);
        goal.Status.Should().Be(SavingGoalStatus.Active);
        result.PercentComplete.Should().Be(40);
        saved.Should().NotBeNull();
        saved!.AmountCents.Should().Be(3_000_000);
        saved.Note.Should().Be("Lương tháng 9");
        _push.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_ReachesTarget_AutoCompletesAndNotifies()
    {
        var userId = Guid.NewGuid();
        var goal = Arrange(userId, targetCents: 10_000_000, savedCents: 9_000_000);

        var result = await _handler.HandleAsync(
            new ContributeToGoalCommand(userId, goal.Id, 1_000_000, null, null));

        goal.Status.Should().Be(SavingGoalStatus.Completed);
        goal.CompletedAt.Should().NotBeNull();
        result.Status.Should().Be("completed");
        result.RemainingCents.Should().Be(0);
        _push.Verify(p => p.NotifyAsync(
            userId, "Hoàn thành mục tiêu tiết kiệm", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_OvershootsTarget_StillCompletesAndKeepsFullSavedAmount()
    {
        var userId = Guid.NewGuid();
        var goal = Arrange(userId, targetCents: 10_000_000, savedCents: 9_000_000);

        var result = await _handler.HandleAsync(
            new ContributeToGoalCommand(userId, goal.Id, 5_000_000, null, null));

        goal.SavedCents.Should().Be(14_000_000);
        goal.Status.Should().Be(SavingGoalStatus.Completed);
        result.PercentComplete.Should().Be(100);
        result.RemainingCents.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_CompletedGoal_ThrowsBusinessRuleException()
    {
        var userId = Guid.NewGuid();
        var goal = Arrange(userId, 10_000_000, 10_000_000, SavingGoalStatus.Completed);

        var act = () => _handler.HandleAsync(new ContributeToGoalCommand(userId, goal.Id, 1_000, null, null));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.ErrorCode.Should().Be(SavingGoalErrorCodes.NotActive);
    }

    [Fact]
    public async Task HandleAsync_CancelledGoal_ThrowsBusinessRuleException()
    {
        var userId = Guid.NewGuid();
        var goal = Arrange(userId, 10_000_000, 0, SavingGoalStatus.Cancelled);

        var act = () => _handler.HandleAsync(new ContributeToGoalCommand(userId, goal.Id, 1_000, null, null));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task HandleAsync_GoalOfAnotherUser_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        _savingGoalRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavingGoal?)null);

        var act = () => _handler.HandleAsync(new ContributeToGoalCommand(userId, Guid.NewGuid(), 1_000, null, null));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
