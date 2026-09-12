using FinMate.Application.Admin.Commands;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

public class SetUserLockCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly SetUserLockCommandHandler _handler;

    private readonly Guid _adminId = Guid.NewGuid();

    public SetUserLockCommandHandlerTests()
    {
        _handler = new SetUserLockCommandHandler(
            _userRepository.Object,
            _refreshTokenRepository.Object,
            _auditLogService.Object,
            new SetUserLockCommandValidator());
    }

    private User Setup(bool isLocked = false)
    {
        var user = new User { Id = Guid.NewGuid(), Email = "a@b.c", IsLocked = isLocked };
        _userRepository
            .Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return user;
    }

    [Fact]
    public async Task LockingAUserAlsoRevokesTheirRefreshTokens()
    {
        // Vai trò và danh tính nằm trong access token nên khóa tài khoản không đuổi được phiên
        // đang chạy. Thu hồi refresh token là thứ duy nhất chặn họ gia hạn tiếp — thiếu nó thì
        // "khóa" chỉ có tác dụng ở lần đăng nhập sau, tức là không bao giờ.
        var user = Setup();

        await _handler.HandleAsync(new SetUserLockCommand(_adminId, user.Id, true, "spam", null));

        user.IsLocked.Should().BeTrue();
        _refreshTokenRepository.Verify(
            r => r.RevokeAllForUserAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _auditLogService.Verify(
            a => a.LogAsync(
                AuditEvents.AdminUserLocked, _adminId, null, It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UnlockingDoesNotTouchRefreshTokens()
    {
        var user = Setup(isLocked: true);

        await _handler.HandleAsync(new SetUserLockCommand(_adminId, user.Id, false, null, null));

        user.IsLocked.Should().BeFalse();
        _refreshTokenRepository.Verify(
            r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditLogService.Verify(
            a => a.LogAsync(
                AuditEvents.AdminUserUnlocked, _adminId, null, It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnAdminCannotLockThemselves()
    {
        var act = () => _handler.HandleAsync(
            new SetUserLockCommand(_adminId, _adminId, true, null, null));

        var exception = await act.Should().ThrowAsync<BusinessRuleException>();
        exception.And.ErrorCode.Should().Be(AdminErrorCodes.CannotLockSelf);
        _userRepository.Verify(
            r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnAdminCanUnlockThemselves()
    {
        // Chỉ chặn chiều KHÓA. Chặn cả chiều mở là chặn nhầm lối thoát.
        var user = new User { Id = _adminId, Email = "admin@b.c", IsLocked = true };
        _userRepository
            .Setup(r => r.GetByIdAsync(_adminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await _handler.HandleAsync(new SetUserLockCommand(_adminId, _adminId, false, null, null));

        user.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task LockingAnAlreadyLockedUserChangesNothingAndWritesNoAuditEntry()
    {
        // PATCH nhận trạng thái mong muốn nên gọi lại phải hợp lệ, nhưng nhật ký kiểm toán
        // không được đầy những dòng "khóa một tài khoản đã khóa".
        var user = Setup(isLocked: true);

        await _handler.HandleAsync(new SetUserLockCommand(_adminId, user.Id, true, null, null));

        _userRepository.Verify(
            r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditLogService.Verify(
            a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LockingAMissingUserIsNotFound()
    {
        var missing = Guid.NewGuid();
        _userRepository
            .Setup(r => r.GetByIdAsync(missing, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => _handler.HandleAsync(new SetUserLockCommand(_adminId, missing, true, null, null));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
