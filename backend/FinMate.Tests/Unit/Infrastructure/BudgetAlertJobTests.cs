using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.ValueObjects;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class BudgetAlertJobTests
{
    private readonly Mock<IBudgetRepository> _budgetRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPushNotificationService> _push = new();
    private readonly BudgetAlertJob _job;

    public BudgetAlertJobTests()
    {
        _job = new BudgetAlertJob(_budgetRepository.Object, _userRepository.Object, _push.Object);
    }

    private (Budget Budget, BudgetPeriod Period) Arrange(
        long limitCents,
        long spentCents,
        NotificationPreferences? prefs = null)
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, NotificationPrefs = prefs ?? new NotificationPreferences() };
        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Category = new Category { Id = Guid.NewGuid(), Name = "Ăn uống", Slug = "food" },
            LimitCents = limitCents,
        };
        var period = new BudgetPeriod
        {
            Id = Guid.NewGuid(),
            BudgetId = budget.Id,
            LimitCents = limitCents,
            SpentCents = spentCents,
        };

        _userRepository.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _budgetRepository.Setup(r => r.GetPeriodsForAlertAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BudgetAlertCandidate> { new(period, budget) });

        return (budget, period);
    }

    [Fact]
    public async Task RunAsync_At80Percent_SendsWarningOnceAndStampsOnly80Flag()
    {
        var (_, period) = Arrange(limitCents: 1_000_000, spentCents: 850_000);

        await _job.RunAsync();
        await _job.RunAsync();

        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Sắp chạm hạn mức chi tiêu", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        period.Alert80SentAt.Should().NotBeNull();
        period.Alert100SentAt.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_AtOrOverLimit_SendsOverLimitOnceAndClosesBothThresholds()
    {
        var (_, period) = Arrange(limitCents: 1_000_000, spentCents: 1_200_000);

        await _job.RunAsync();
        await _job.RunAsync();

        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Vượt hạn mức chi tiêu", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Nhảy thẳng qua 100% không được kéo theo cảnh báo 80% ở lần chạy sau.
        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Sắp chạm hạn mức chi tiêu", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        period.Alert80SentAt.Should().NotBeNull();
        period.Alert100SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_CrossesFrom80To100_SendsBothAlertsInOrder()
    {
        var (_, period) = Arrange(limitCents: 1_000_000, spentCents: 850_000);

        await _job.RunAsync();
        period.SpentCents = 1_050_000;
        await _job.RunAsync();

        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Sắp chạm hạn mức chi tiêu", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Vượt hạn mức chi tiêu", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_BudgetAlertsDisabled_SendsNothing()
    {
        var (_, period) = Arrange(
            limitCents: 1_000_000,
            spentCents: 1_200_000,
            prefs: new NotificationPreferences { BudgetAlertsEnabled = false });

        await _job.RunAsync();

        _push.VerifyNoOtherCalls();
        period.Alert100SentAt.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_PushDisabled_SendsNothing()
    {
        Arrange(
            limitCents: 1_000_000,
            spentCents: 1_200_000,
            prefs: new NotificationPreferences { PushEnabled = false });

        await _job.RunAsync();

        _push.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_TotalBudget_DescribesScopeWithoutCategoryName()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, NotificationPrefs = new NotificationPreferences() };
        var budget = new Budget { Id = Guid.NewGuid(), UserId = userId, CategoryId = null, LimitCents = 1_000_000 };
        var period = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = budget.Id, LimitCents = 1_000_000, SpentCents = 1_000_000 };

        _userRepository.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _budgetRepository.Setup(r => r.GetPeriodsForAlertAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BudgetAlertCandidate> { new(period, budget) });

        await _job.RunAsync();

        _push.Verify(p => p.NotifyAsync(
            userId,
            It.IsAny<string>(),
            It.Is<string>(body => body.Contains("toàn bộ chi tiêu")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
