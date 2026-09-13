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
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IBudgetRepository> _budgetRepository = new();
    private readonly GetGoalProgressQueryHandler _handler;

    public GetGoalProgressQueryHandlerTests()
    {
        _handler = new GetGoalProgressQueryHandler(
            _savingGoalRepository.Object, _userRepository.Object, _budgetRepository.Object);

        // Mặc định: chưa khai thu nhập. Các test tiến độ sẵn có không quan tâm tới tính khả
        // thi, và để mặc định ở "chưa đủ dữ liệu" thì chúng không phải biết tới nó.
        _budgetRepository
            .Setup(r => r.GetListForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    /// <summary>Khai thu nhập và tổng hạn mức ngân sách cho các test về tính khả thi.</summary>
    private void GiveUserFinances(Guid userId, long monthlyIncomeCents, long totalBudgetCents)
    {
        _userRepository
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, MonthlyIncomeCents = monthlyIncomeCents });

        _budgetRepository
            .Setup(r => r.GetListForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Budget { Id = Guid.NewGuid(), UserId = userId, LimitCents = totalBudgetCents }]);
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

    // ------------------------------------------------ tính khả thi (docx Flow 3 bước 2.2)

    [Fact]
    public async Task AnAmbitiousGoalIsFlaggedButNotRefused()
    {
        // Mục tiêu 20 triệu trong 6 tháng cần ~3,3 triệu/tháng, nhưng thu nhập 9 triệu trừ
        // ngân sách 7 triệu chỉ còn dư 2 triệu. Docx yêu cầu GỢI Ý điều chỉnh, không chặn.
        var userId = Guid.NewGuid();
        GiveUserFinances(userId, monthlyIncomeCents: 9_000_000, totalBudgetCents: 7_000_000);
        var now = DateTimeOffset.UtcNow;
        var goal = Arrange(userId, targetCents: 20_000_000, savedCents: 0,
            createdAt: now, deadline: now.AddDays(180));

        var progress = await _handler.HandleAsync(new GetGoalProgressQuery(userId, goal.Id));

        progress.Feasibility.Should().NotBeNull();
        progress.Feasibility!.IsFeasible.Should().BeFalse();
        progress.Feasibility.AvailablePerMonthCents.Should().Be(2_000_000);
        progress.Feasibility.RequiredPerMonthCents.Should().BeGreaterThan(2_000_000);
        progress.Feasibility.SuggestedDeadline.Should().NotBeNull();
        progress.Feasibility.SuggestedTargetCents.Should().NotBeNull();
    }

    [Fact]
    public async Task AGoalWithinReachIsFeasibleAndNeedsNoSuggestions()
    {
        var userId = Guid.NewGuid();
        GiveUserFinances(userId, monthlyIncomeCents: 9_000_000, totalBudgetCents: 4_000_000);
        var now = DateTimeOffset.UtcNow;
        var goal = Arrange(userId, targetCents: 20_000_000, savedCents: 0,
            createdAt: now, deadline: now.AddDays(180));

        var feasibility = (await _handler.HandleAsync(new GetGoalProgressQuery(userId, goal.Id))).Feasibility;

        feasibility!.IsFeasible.Should().BeTrue();
        feasibility.SuggestedDeadline.Should().BeNull();
        feasibility.SuggestedTargetCents.Should().BeNull();
    }

    [Fact]
    public async Task WithoutADeclaredIncomeTheMonthlyRequirementIsStillUseful()
    {
        // Không kết luận khả thi hay không, nhưng "mỗi tháng cần bao nhiêu" tự nó đã có ích.
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var goal = Arrange(userId, targetCents: 20_000_000, savedCents: 0,
            createdAt: now, deadline: now.AddDays(180));

        var feasibility = (await _handler.HandleAsync(new GetGoalProgressQuery(userId, goal.Id))).Feasibility;

        feasibility.Should().NotBeNull();
        feasibility!.RequiredPerMonthCents.Should().BeGreaterThan(0);
        feasibility.IsFeasible.Should().BeNull();
        feasibility.AvailablePerMonthCents.Should().BeNull();
    }

    [Fact]
    public async Task AGoalWithoutADeadlineHasNoFeasibilityToAssess()
    {
        var userId = Guid.NewGuid();
        GiveUserFinances(userId, monthlyIncomeCents: 9_000_000, totalBudgetCents: 1_000_000);
        var goal = Arrange(userId, targetCents: 20_000_000, savedCents: 0,
            createdAt: DateTimeOffset.UtcNow, deadline: null);

        var progress = await _handler.HandleAsync(new GetGoalProgressQuery(userId, goal.Id));

        progress.Feasibility.Should().BeNull("không có mốc thời gian thì không có nhịp bắt buộc nào");
    }
}
