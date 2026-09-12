using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>
/// Ba khóa định danh mà hệ thống khác join vào: <c>provider_key</c>, <c>slug</c> của danh mục
/// hệ thống, và <c>code</c> của nhiệm vụ. Đổi chúng không làm hỏng gì ở tầng DB — ràng buộc
/// vẫn thỏa, request vẫn 200 — nhưng liên kết sang AI Service, sang seeder và sang
/// GamificationService đứt im lặng. Đây là bộ test giữ chúng đứng yên.
/// </summary>
public class ImmutableKeyGuardTests
{
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly Guid _adminId = Guid.NewGuid();

    [Fact]
    public async Task ProviderKeyCannotChange()
    {
        var repository = new Mock<IProviderConfigRepository>();
        var config = new ProviderConfig
        {
            Id = Guid.NewGuid(),
            ProviderKey = "mb_bank",
            DisplayName = "MB Bank",
            PackageName = "com.mbmobile",
        };
        repository
            .Setup(r => r.GetByIdAsync(config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var handler = new UpdateProviderConfigCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateProviderConfigCommandValidator());

        var act = () => handler.HandleAsync(new UpdateProviderConfigCommand(
            _adminId, config.Id, "mb_bank_v2", null, null, null, null));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .And.ErrorCode.Should().Be(AdminErrorCodes.ImmutableField);
        config.ProviderKey.Should().Be("mb_bank");
    }

    [Fact]
    public async Task ResendingTheSameProviderKeyIsAccepted()
    {
        // Client PATCH thường gửi lại nguyên đối tượng vừa đọc về. Từ chối cả trường hợp đó
        // thì mọi lần sửa tên hiển thị đều hỏng.
        var repository = new Mock<IProviderConfigRepository>();
        var config = new ProviderConfig
        {
            Id = Guid.NewGuid(),
            ProviderKey = "mb_bank",
            DisplayName = "MB Bank",
            PackageName = "com.mbmobile",
        };
        repository
            .Setup(r => r.GetByIdAsync(config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var handler = new UpdateProviderConfigCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateProviderConfigCommandValidator());

        await handler.HandleAsync(new UpdateProviderConfigCommand(
            _adminId, config.Id, "mb_bank", "MB Bank mới", null, null, null));

        config.DisplayName.Should().Be("MB Bank mới");
    }

    [Fact]
    public async Task PackageNameTakenByAnotherProviderIsAConflict()
    {
        var repository = new Mock<IProviderConfigRepository>();
        var config = new ProviderConfig
        {
            Id = Guid.NewGuid(),
            ProviderKey = "mb_bank",
            DisplayName = "MB Bank",
            PackageName = "com.mbmobile",
        };
        repository
            .Setup(r => r.GetByIdAsync(config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        repository
            .Setup(r => r.ExistsByPackageNameAsync("com.VCB", config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new UpdateProviderConfigCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateProviderConfigCommandValidator());

        var act = () => handler.HandleAsync(new UpdateProviderConfigCommand(
            _adminId, config.Id, null, null, "com.VCB", null, null));

        (await act.Should().ThrowAsync<ConflictException>())
            .And.ErrorCode.Should().Be(AdminErrorCodes.PackageNameDuplicate);
    }

    [Fact]
    public async Task SystemCategorySlugCannotChange()
    {
        var repository = new Mock<ICategoryRepository>();
        var category = new Category
        {
            Id = Guid.NewGuid(),
            UserId = null,
            Name = "Ăn uống",
            Slug = "food",
            IsSystem = true,
        };
        repository
            .Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var handler = new UpdateSystemCategoryCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateSystemCategoryCommandValidator());

        var act = () => handler.HandleAsync(new UpdateSystemCategoryCommand(
            _adminId, category.Id, null, "food_and_drink", null, null));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .And.ErrorCode.Should().Be(AdminErrorCodes.ImmutableField);
        category.Slug.Should().Be("food");
    }

    [Fact]
    public async Task AUserOwnedCategoryIsInvisibleToTheAdminEndpoint()
    {
        // 404 chứ không phải 403: danh mục riêng của một người dùng không thuộc phạm vi admin,
        // và trả 403 sẽ tiết lộ rằng nó tồn tại.
        var repository = new Mock<ICategoryRepository>();
        var category = new Category
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Name = "Quỹ đen",
            Slug = "quy_den",
        };
        repository
            .Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var handler = new UpdateSystemCategoryCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateSystemCategoryCommandValidator());

        var act = () => handler.HandleAsync(new UpdateSystemCategoryCommand(
            _adminId, category.Id, "Đổi tên", null, null, null));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task MissionConditionTypeCannotChange()
    {
        // user_missions của chu kỳ đang chạy đã tích tiến độ theo điều kiện cũ; đổi giữa chừng
        // làm công người dùng đã bỏ ra trở thành vô nghĩa.
        var repository = new Mock<IMissionRepository>();
        var mission = NewMission();
        repository
            .Setup(r => r.GetMissionByIdAsync(mission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mission);

        var handler = new UpdateMissionCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateMissionCommandValidator());

        var act = () => handler.HandleAsync(new UpdateMissionCommand(
            _adminId, mission.Id, null, null, null, null,
            MissionConditionType.ContributeToGoal, null, null, null));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .And.ErrorCode.Should().Be(AdminErrorCodes.ImmutableField);
        mission.ConditionType.Should().Be(MissionConditionType.ConfirmTransaction);
    }

    [Fact]
    public async Task MissionPeriodTypeCannotChange()
    {
        var repository = new Mock<IMissionRepository>();
        var mission = NewMission();
        repository
            .Setup(r => r.GetMissionByIdAsync(mission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mission);

        var handler = new UpdateMissionCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateMissionCommandValidator());

        var act = () => handler.HandleAsync(new UpdateMissionCommand(
            _adminId, mission.Id, null, null, null, MissionPeriodType.Weekly, null, null, null, null));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .And.ErrorCode.Should().Be(AdminErrorCodes.ImmutableField);
    }

    [Fact]
    public async Task MissionTargetAndRewardCanChange()
    {
        // Đây là hai thứ DUY NHẤT đổi được về mặt nghiệp vụ: chúng chỉ là con số so sánh, có
        // hiệu lực từ chu kỳ sau mà không làm hỏng tiến độ đã tích.
        var repository = new Mock<IMissionRepository>();
        var mission = NewMission();
        repository
            .Setup(r => r.GetMissionByIdAsync(mission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mission);

        var handler = new UpdateMissionCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateMissionCommandValidator());

        await handler.HandleAsync(new UpdateMissionCommand(
            _adminId, mission.Id, null, "Tiêu đề mới", null, null, null, 5, 50, null));

        mission.ConditionTarget.Should().Be(5);
        mission.ExpReward.Should().Be(50);
        mission.Title.Should().Be("Tiêu đề mới");
    }

    [Fact]
    public async Task DuplicateMissionCodeIsAConflict()
    {
        var repository = new Mock<IMissionRepository>();
        repository
            .Setup(r => r.GetByCodeAsync("daily_manual_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewMission());

        var handler = new CreateMissionCommandHandler(
            repository.Object, _auditLogService.Object, new CreateMissionCommandValidator());

        var act = () => handler.HandleAsync(new CreateMissionCommand(
            _adminId, "daily_manual_1", "Nhiệm vụ", "Mô tả",
            MissionPeriodType.Daily, MissionConditionType.CreateManualTransaction, 1, 10, null));

        (await act.Should().ThrowAsync<ConflictException>())
            .And.ErrorCode.Should().Be(AdminErrorCodes.MissionCodeDuplicate);
    }

    private static Mission NewMission() => new()
    {
        Id = Guid.NewGuid(),
        Code = "daily_manual_1",
        Title = "Ghi một giao dịch",
        Description = "Tự nhập một giao dịch hôm nay",
        PeriodType = MissionPeriodType.Daily,
        ConditionType = MissionConditionType.ConfirmTransaction,
        ConditionTarget = 1,
        ExpReward = 10,
        IsActive = true,
    };
}
