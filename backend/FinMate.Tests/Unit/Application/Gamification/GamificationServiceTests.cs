using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Gamification;
using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Gamification;

public class GamificationServiceTests
{
    private readonly Mock<IGamificationRepository> _gamificationRepository = new();
    private readonly Mock<IMissionRepository> _missionRepository = new();
    private readonly GamificationService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public GamificationServiceTests()
    {
        _service = new GamificationService(_gamificationRepository.Object, _missionRepository.Object);

        _missionRepository.Setup(r => r.GetActiveMissionsByConditionAsync(
                It.IsAny<MissionConditionType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Mission>());
        _missionRepository.Setup(r => r.GetUserMissionsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserMission>());
        _missionRepository.Setup(r => r.HasEverCompletedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _gamificationRepository.Setup(r => r.GetAllMascotItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MascotItem>());
        _gamificationRepository.Setup(r => r.GetOwnedMascotItemsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserMascotItem>());
    }

    private UserGamification SetupProfile(
        int exp = 0, int streak = 0, int longest = 0, DateOnly? lastActivity = null)
    {
        var profile = new UserGamification
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ExpPoints = exp,
            Level = LevelCurve.LevelForExp(exp),
            CurrentStreakDays = streak,
            LongestStreakDays = longest,
            LastActivityDate = lastActivity,
        };

        _gamificationRepository.Setup(r => r.GetProfileAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        return profile;
    }

    private void SetupMissions(params Mission[] missions)
        => _missionRepository.Setup(r => r.GetActiveMissionsByConditionAsync(
                It.IsAny<MissionConditionType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(missions.ToList());

    private static Mission Mission(
        string code,
        MissionConditionType condition,
        int target,
        int exp = 50,
        MissionPeriodType period = MissionPeriodType.Daily) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = code,
            Description = code,
            PeriodType = period,
            ConditionType = condition,
            ConditionTarget = target,
            ExpReward = exp,
            IsActive = true,
        };

    private Task<GamificationOutcome> RecordAsync(
        MissionConditionType condition = MissionConditionType.ConfirmTransaction,
        int exp = 10,
        DateTimeOffset? at = null)
        => _service.RecordActivityAsync(
            new GamificationActivity(_userId, condition, exp, at ?? DateTimeOffset.UtcNow));

    // ---- Hồ sơ ----

    [Fact]
    public async Task RecordActivity_FirstEverActivity_CreatesTheProfileLazily()
    {
        _gamificationRepository.Setup(r => r.GetProfileAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserGamification?)null);

        UserGamification? added = null;
        _gamificationRepository.Setup(r => r.AddProfile(It.IsAny<UserGamification>()))
            .Callback<UserGamification>(p => added = p);

        var outcome = await RecordAsync(exp: 10);

        added.Should().NotBeNull();
        added!.UserId.Should().Be(_userId);
        outcome.ExpPoints.Should().Be(10);
        outcome.CurrentStreakDays.Should().Be(1);
    }

    // ---- EXP và level ----

    [Fact]
    public async Task RecordActivity_AccumulatesExp()
    {
        var profile = SetupProfile(exp: 40);

        var outcome = await RecordAsync(exp: 10);

        profile.ExpPoints.Should().Be(50);
        outcome.LeveledUp.Should().BeFalse();
    }

    [Fact]
    public async Task RecordActivity_CrossingAThreshold_ReportsLevelUp()
    {
        var profile = SetupProfile(exp: 95);

        var outcome = await RecordAsync(exp: 10);

        profile.Level.Should().Be(2);
        outcome.LeveledUp.Should().BeTrue();
        outcome.Level.Should().Be(2);
    }

    [Fact]
    public async Task RecordActivity_MissionRewardCanCarryThroughSeveralLevelsAtOnce()
    {
        var profile = SetupProfile(exp: 0);
        SetupMissions(Mission("big", MissionConditionType.ConfirmTransaction, target: 1, exp: 1_000));

        var outcome = await RecordAsync(exp: 10);

        // 10 + 1000 = 1010 → level 5 (mốc 1000), nhảy 4 bậc trong một lượt.
        profile.ExpPoints.Should().Be(1_010);
        outcome.Level.Should().Be(5);
        outcome.LeveledUp.Should().BeTrue();
    }

    [Fact]
    public async Task RevertExp_RecomputesLevelDownwards()
    {
        var profile = SetupProfile(exp: 110);
        profile.Level.Should().Be(2);

        await _service.RevertExpAsync(_userId, 20);

        profile.ExpPoints.Should().Be(90);
        profile.Level.Should().Be(1);
    }

    [Fact]
    public async Task RevertExp_NeverDrivesExpNegative()
    {
        var profile = SetupProfile(exp: 10);

        await _service.RevertExpAsync(_userId, 500);

        profile.ExpPoints.Should().Be(0);
        profile.Level.Should().Be(1);
    }

    [Fact]
    public async Task RevertExp_UserWithNoProfile_IsANoOp()
    {
        _gamificationRepository.Setup(r => r.GetProfileAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserGamification?)null);

        var act = () => _service.RevertExpAsync(_userId, 50);

        await act.Should().NotThrowAsync();
        _gamificationRepository.Verify(r => r.AddProfile(It.IsAny<UserGamification>()), Times.Never);
    }

    // ---- Streak ----

    [Fact]
    public async Task Streak_ActivityOnConsecutiveDays_Increments()
    {
        var today = VietnamTime.Today();
        var profile = SetupProfile(streak: 3, longest: 3, lastActivity: today.AddDays(-1));

        await RecordAsync();

        profile.CurrentStreakDays.Should().Be(4);
        profile.LongestStreakDays.Should().Be(4);
        profile.LastActivityDate.Should().Be(today);
    }

    [Fact]
    public async Task Streak_SecondActivitySameDay_DoesNotDoubleCount()
    {
        var today = VietnamTime.Today();
        var profile = SetupProfile(streak: 3, longest: 5, lastActivity: today);

        await RecordAsync();

        profile.CurrentStreakDays.Should().Be(3);
        profile.LongestStreakDays.Should().Be(5);
    }

    [Fact]
    public async Task Streak_AfterAGap_RestartsAtOneButKeepsTheRecord()
    {
        var today = VietnamTime.Today();
        var profile = SetupProfile(streak: 9, longest: 9, lastActivity: today.AddDays(-3));

        await RecordAsync();

        profile.CurrentStreakDays.Should().Be(1);
        profile.LongestStreakDays.Should().Be(9);
    }

    [Fact]
    public async Task Streak_UsesVietnamCalendarDay_NotUtc()
    {
        // 2026-09-10T18:00Z là 11/09 01:00 giờ VN — phải tính là ngày 11, không phải ngày 10.
        var profile = SetupProfile(streak: 1, lastActivity: new DateOnly(2026, 9, 10));

        await RecordAsync(at: new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero));

        profile.LastActivityDate.Should().Be(new DateOnly(2026, 9, 11));
        profile.CurrentStreakDays.Should().Be(2);
    }

    // ---- Mission ----

    [Fact]
    public async Task Mission_FirstProgress_CreatesTheRowLazily()
    {
        SetupProfile();
        SetupMissions(Mission("daily_confirm_3", MissionConditionType.ConfirmTransaction, target: 3));

        UserMission? added = null;
        _missionRepository.Setup(r => r.AddUserMission(It.IsAny<UserMission>()))
            .Callback<UserMission>(m => added = m);

        await RecordAsync();

        added.Should().NotBeNull();
        added!.Progress.Should().Be(1);
        added.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Mission_ReachingTheTarget_CompletesAndAwardsItsExp()
    {
        var profile = SetupProfile();
        var mission = Mission("daily_confirm_3", MissionConditionType.ConfirmTransaction, target: 3, exp: 50);
        SetupMissions(mission);

        var existing = new UserMission
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            MissionId = mission.Id,
            Progress = 2,
            PeriodStart = VietnamTime.Today(),
        };
        _missionRepository.Setup(r => r.GetUserMissionsAsync(
                _userId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserMission> { existing });

        await RecordAsync(exp: 10);

        existing.IsCompleted.Should().BeTrue();
        existing.ExpAwarded.Should().Be(50);
        profile.ExpPoints.Should().Be(60);
    }

    [Fact]
    public async Task Mission_AlreadyCompletedThisPeriod_DoesNotPayOutTwice()
    {
        var profile = SetupProfile();
        var mission = Mission("daily_confirm_3", MissionConditionType.ConfirmTransaction, target: 1, exp: 50);
        SetupMissions(mission);

        var done = new UserMission
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            MissionId = mission.Id,
            Progress = 1,
            IsCompleted = true,
            ExpAwarded = 50,
            PeriodStart = VietnamTime.Today(),
        };
        _missionRepository.Setup(r => r.GetUserMissionsAsync(
                _userId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserMission> { done });

        await RecordAsync(exp: 10);

        profile.ExpPoints.Should().Be(10);
        done.Progress.Should().Be(1);
    }

    [Fact]
    public async Task Mission_StreakCondition_TracksTheStreakItselfNotACount()
    {
        var today = VietnamTime.Today();
        SetupProfile(streak: 6, lastActivity: today.AddDays(-1));
        var mission = Mission("streak_7", MissionConditionType.LoginStreak, target: 7, exp: 200,
            period: MissionPeriodType.OneTime);
        SetupMissions(mission);

        UserMission? added = null;
        _missionRepository.Setup(r => r.AddUserMission(It.IsAny<UserMission>()))
            .Callback<UserMission>(m => added = m);

        await RecordAsync(condition: MissionConditionType.LoginStreak);

        // Streak vừa lên 7 → tiến độ phải là 7, không phải 1 (số lần ghi nhận).
        added!.Progress.Should().Be(7);
        added.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Mission_OnlyMissionsMatchingTheConditionAdvance()
    {
        SetupProfile();
        _missionRepository.Setup(r => r.GetActiveMissionsByConditionAsync(
                MissionConditionType.ContributeToGoal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Mission>());

        await RecordAsync(condition: MissionConditionType.ContributeToGoal);

        _missionRepository.Verify(r => r.AddUserMission(It.IsAny<UserMission>()), Times.Never);
    }

    // ---- Mascot ----

    [Fact]
    public async Task Mascot_DefaultItems_AreGrantedOnFirstActivity()
    {
        SetupProfile();
        var item = new MascotItem
        {
            Id = Guid.NewGuid(),
            Code = "outfit_basic",
            ItemType = MascotItemType.Outfit,
            UnlockType = MascotUnlockType.Default,
        };
        _gamificationRepository.Setup(r => r.GetAllMascotItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MascotItem> { item });

        var outcome = await RecordAsync();

        outcome.UnlockedItems.Should().ContainSingle().Which.Code.Should().Be("outfit_basic");
    }

    [Fact]
    public async Task Mascot_LevelLockedItem_UnlocksOnlyOnceTheLevelIsReached()
    {
        var profile = SetupProfile(exp: 95);
        var item = new MascotItem
        {
            Id = Guid.NewGuid(),
            Code = "hat_cap",
            ItemType = MascotItemType.Hat,
            UnlockType = MascotUnlockType.Level,
            UnlockLevel = 2,
        };
        _gamificationRepository.Setup(r => r.GetAllMascotItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MascotItem> { item });

        var outcome = await RecordAsync(exp: 10);

        profile.Level.Should().Be(2);
        outcome.UnlockedItems.Should().ContainSingle().Which.Code.Should().Be("hat_cap");
    }

    [Fact]
    public async Task Mascot_MissionLockedItem_UnlocksInTheSameCallThatCompletesTheMission()
    {
        SetupProfile();
        var mission = Mission("first_goal_contribution", MissionConditionType.ContributeToGoal, target: 1,
            period: MissionPeriodType.OneTime);
        SetupMissions(mission);

        var item = new MascotItem
        {
            Id = Guid.NewGuid(),
            Code = "hat_piggy",
            ItemType = MascotItemType.Hat,
            UnlockType = MascotUnlockType.Mission,
            UnlockMissionCode = "first_goal_contribution",
        };
        _gamificationRepository.Setup(r => r.GetAllMascotItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MascotItem> { item });

        // Mission vừa hoàn thành mới chỉ được track, truy vấn DB vẫn trả false.
        var outcome = await RecordAsync(condition: MissionConditionType.ContributeToGoal);

        outcome.UnlockedItems.Should().ContainSingle().Which.Code.Should().Be("hat_piggy");
    }

    [Fact]
    public async Task Mascot_AlreadyOwnedItem_IsNotGrantedAgain()
    {
        SetupProfile();
        var item = new MascotItem
        {
            Id = Guid.NewGuid(),
            Code = "outfit_basic",
            ItemType = MascotItemType.Outfit,
            UnlockType = MascotUnlockType.Default,
        };
        _gamificationRepository.Setup(r => r.GetAllMascotItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MascotItem> { item });
        _gamificationRepository.Setup(r => r.GetOwnedMascotItemsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserMascotItem>
            {
                new() { Id = Guid.NewGuid(), UserId = _userId, MascotItemId = item.Id, ItemType = item.ItemType },
            });

        var outcome = await RecordAsync();

        outcome.UnlockedItems.Should().BeEmpty();
        _gamificationRepository.Verify(r => r.AddOwnedMascotItem(It.IsAny<UserMascotItem>()), Times.Never);
    }

    [Fact]
    public async Task Mascot_PremiumItems_AreNeverGrantedByProgression()
    {
        SetupProfile();
        _gamificationRepository.Setup(r => r.GetAllMascotItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MascotItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Code = "outfit_gold",
                    ItemType = MascotItemType.Outfit,
                    UnlockType = MascotUnlockType.Default,
                    IsPremium = true,
                },
            });

        var outcome = await RecordAsync();

        outcome.UnlockedItems.Should().BeEmpty();
    }
}
