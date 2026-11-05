using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application;

public class OpsApprovalServiceTests
{
    private readonly Mock<IOpsRepository> _ops = new();
    private readonly Mock<ISetUserLockCommandHandler> _lock = new();
    private readonly OpsApprovalRequest _approval;
    private readonly User _manager = new() { Id = Guid.NewGuid(), Role = UserRole.OpsManager };
    private readonly OpsApprovalService _service;

    public OpsApprovalServiceTests()
    {
        var target = new User { Id = Guid.NewGuid(), Role = UserRole.User, IsLocked = true };
        _approval = new OpsApprovalRequest
        {
            Id = Guid.NewGuid(),
            Type = OpsApprovalType.UnlockUser,
            TargetId = target.Id,
            RequestedByUserId = Guid.NewGuid(),
            Status = OpsApprovalStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        };
        _ops.Setup(r => r.GetApprovalAsync(_approval.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_approval);

        var access = new Mock<IOpsAccessService>();
        access.Setup(a => a.RequireAsync(_manager.Id, OpsCapability.DecideApprovals, It.IsAny<CancellationToken>())).ReturnsAsync(_manager);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        _service = new OpsApprovalService(
            _ops.Object,
            access.Object,
            users.Object,
            Mock.Of<IDataDeletionRequestRepository>(),
            Mock.Of<IGroupRepository>(),
            Mock.Of<ISupportRepository>(),
            _lock.Object,
            Mock.Of<ICancelDataDeletionRequestCommandHandler>(),
            Mock.Of<IGroupSupportTransfer>(),
            Mock.Of<IAuditLogService>(),
            Mock.Of<ISystemConfigProvider>());
    }

    [Fact]
    public async Task AnUnexpectedFailureDoesNotLeaveTheRecordSayingApproved()
    {
        _lock.Setup(h => h.HandleAsync(It.IsAny<SetUserLockCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("DB lỗi"));

        var act = () => _service.ApproveAsync(_manager.Id, _approval.Id, null, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _ops.Verify(r => r.MarkApprovalFailedAsync(_approval.Id, OpsErrorCodes.ApprovalExecutionError, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClosingTheTabMidApprovalDoesNotAbandonTheAction()
    {
        using var cancelled = new CancellationTokenSource();
        var seen = new List<CancellationToken>();
        _lock.Setup(h => h.HandleAsync(It.IsAny<SetUserLockCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SetUserLockCommand, CancellationToken>((_, ct) => seen.Add(ct))
            .ReturnsAsync(new AdminUserDto(Guid.NewGuid(), "a@b.c", "A", UserRole.User, false, true, false, DateTimeOffset.UtcNow, null));
        _ops.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(cancelled.Cancel).Returns(Task.CompletedTask);

        await _service.ApproveAsync(_manager.Id, _approval.Id, null, null, cancelled.Token);

        seen.Should().ContainSingle().Which.CanBeCanceled.Should().BeFalse();
    }
}
