using FinMate.Application.Admin.Commands;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>
/// Trước endpoint này, đường duy nhất tạo admin là biến môi trường đọc lúc khởi động. Mỗi test
/// dưới đây ghim một cách tự bắn vào chân mà endpoint phải chặn.
/// </summary>
public class SetUserRoleCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly SetUserRoleCommandHandler _handler;

    private readonly Guid _adminId = Guid.NewGuid();

    public SetUserRoleCommandHandlerTests()
    {
        // Mặc định: còn nhiều admin, nên chốt "admin cuối cùng" không chắn nhầm test khác.
        _userRepository
            .Setup(r => r.CountActiveAdminsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        _handler = new SetUserRoleCommandHandler(
            _userRepository.Object,
            _refreshTokenRepository.Object,
            _auditLogService.Object,
            new SetUserRoleCommandValidator());
    }

    private User Setup(UserRole role = UserRole.User, DateTimeOffset? deletedAt = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "a@b.c",
            DisplayName = "Người dùng",
            Role = role,
            DeletedAt = deletedAt,
        };
        _userRepository
            .Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return user;
    }

    private Task<FinMate.Application.Common.Models.AdminUserDto> ActAsync(Guid targetId, UserRole role)
        => _handler.HandleAsync(new SetUserRoleCommand(_adminId, targetId, role, "vì lý do X", "127.0.0.1"));

    [Fact]
    public async Task PromotingAUserMakesThemAdmin()
    {
        var user = Setup();

        var result = await ActAsync(user.Id, UserRole.Admin);

        result.Role.Should().Be(UserRole.Admin);
        _userRepository.Verify(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()), Times.Once);
        _auditLogService.Verify(
            a => a.LogAsync(AuditEvents.AdminUserRoleChanged, _adminId, "127.0.0.1",
                It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PromotingDoesNotLogAnyoneOut()
    {
        // RefreshTokenCommandHandler nạp lại user từ DB mỗi lần refresh, nên vai trò mới tự có
        // ở lần refresh kế tiếp — không cần đá họ ra.
        var user = Setup();

        await ActAsync(user.Id, UserRole.Admin);

        _refreshTokenRepository.Verify(
            r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DemotingRevokesRefreshTokens()
    {
        // Vai trò nằm trong access token và không tra lại DB mỗi request. Thu hồi refresh token
        // là thứ kẹp cửa sổ leo thang đặc quyền lại ở đúng TTL của access token.
        var user = Setup(UserRole.Admin);

        await ActAsync(user.Id, UserRole.User);

        _refreshTokenRepository.Verify(
            r => r.RevokeAllForUserAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangingYourOwnRoleIsRefused()
    {
        var act = () => ActAsync(_adminId, UserRole.User);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.ErrorCode.Should().Be(AdminErrorCodes.CannotChangeOwnRole);
    }

    [Fact]
    public async Task DemotingTheLastActiveAdminIsRefusedAndChangesNothing()
    {
        var user = Setup(UserRole.Admin);
        _userRepository
            .Setup(r => r.CountActiveAdminsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var act = () => ActAsync(user.Id, UserRole.User);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.ErrorCode.Should().Be(AdminErrorCodes.CannotDemoteLastAdmin);

        user.Role.Should().Be(UserRole.Admin, "vai trò phải giữ nguyên khi bị từ chối");
        _userRepository.Verify(
            r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheLastAdminRuleStillAllowsPromotion()
    {
        // Chốt chỉ chắn chiều HẠ. Chắn cả chiều thăng thì hệ thống còn đúng một admin sẽ không
        // bao giờ thoát ra khỏi tình trạng đó.
        var user = Setup();
        _userRepository
            .Setup(r => r.CountActiveAdminsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        (await ActAsync(user.Id, UserRole.Admin)).Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task PromotingAnAccountAwaitingDeletionIsRefused()
    {
        var user = Setup(deletedAt: DateTimeOffset.UtcNow);

        var act = () => ActAsync(user.Id, UserRole.Admin);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.ErrorCode.Should().Be(AdminErrorCodes.CannotPromoteDeletedUser);
    }

    [Fact]
    public async Task SettingTheRoleItAlreadyHasWritesNoAuditLine()
    {
        // PATCH nhận trạng thái mong muốn nên gọi lại là hợp lệ; nhật ký đầy những dòng
        // "đặt Admin thành Admin" thì mất tác dụng của chính nó.
        var user = Setup(UserRole.Admin);

        await ActAsync(user.Id, UserRole.Admin);

        _userRepository.Verify(
            r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditLogService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnUnknownUserIsNotFound()
    {
        _userRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => ActAsync(Guid.NewGuid(), UserRole.Admin);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
