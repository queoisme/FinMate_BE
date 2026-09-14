using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.ValueObjects;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.ExternalServices;
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
        // Dùng BudgetAlertNotifier THẬT chứ không mock: các test dưới đây kiểm chính hành vi
        // của nó (tôn trọng PushEnabled/BudgetAlertsEnabled), mock đi là mất luôn phần đó.
        _job = new BudgetAlertJob(
            _budgetRepository.Object,
            new BudgetAlertNotifier(_userRepository.Object, _push.Object));
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
    public async Task RunAsync_Under70Percent_SendsNothing()
    {
        var (_, period) = Arrange(limitCents: 1_000_000, spentCents: 650_000);

        await _job.RunAsync();

        _push.VerifyNoOtherCalls();
        period.Alert70SentAt.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_At70Percent_SendsOnlyTheLowestWarningOnce()
    {
        var (_, period) = Arrange(limitCents: 1_000_000, spentCents: 750_000);

        await _job.RunAsync();
        await _job.RunAsync();

        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Đã dùng quá 70% hạn mức", It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        period.Alert70SentAt.Should().NotBeNull();
        period.Alert90SentAt.Should().BeNull();
        period.Alert100SentAt.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_At90Percent_SendsThe90AlertAndClosesThe70()
    {
        var (_, period) = Arrange(limitCents: 1_000_000, spentCents: 920_000);

        await _job.RunAsync();
        await _job.RunAsync();

        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Sắp cạn hạn mức chi tiêu", It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Đã ở 92% thì cảnh báo "quá 70%" là thông điệp sai — không được bắn, kể cả ở lần chạy sau.
        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Đã dùng quá 70% hạn mức", It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        period.Alert70SentAt.Should().NotBeNull();
        period.Alert90SentAt.Should().NotBeNull();
        period.Alert100SentAt.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_AtOrOverLimit_SendsOverLimitOnceAndClosesAllThreeThresholds()
    {
        var (_, period) = Arrange(limitCents: 1_000_000, spentCents: 1_200_000);

        await _job.RunAsync();
        await _job.RunAsync();

        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Vượt hạn mức chi tiêu", It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Nhảy thẳng qua cả 3 mốc chỉ được gửi đúng 1 thông báo — mốc cao nhất.
        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Đã dùng quá 70% hạn mức", It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Sắp cạn hạn mức chi tiêu", It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        period.Alert70SentAt.Should().NotBeNull();
        period.Alert90SentAt.Should().NotBeNull();
        period.Alert100SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_ClimbsThroughAllThreeThresholds_SendsEachExactlyOnce()
    {
        var (_, period) = Arrange(limitCents: 1_000_000, spentCents: 750_000);

        await _job.RunAsync();
        period.SpentCents = 950_000;
        await _job.RunAsync();
        period.SpentCents = 1_050_000;
        await _job.RunAsync();

        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Đã dùng quá 70% hạn mức", It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Sắp cạn hạn mức chi tiêu", It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _push.Verify(p => p.NotifyAsync(
            It.IsAny<Guid>(), "Vượt hạn mức chi tiêu", It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
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
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
