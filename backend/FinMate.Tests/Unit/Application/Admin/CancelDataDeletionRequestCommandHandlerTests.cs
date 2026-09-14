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
/// Huỷ là đường cứu DUY NHẤT: người gửi yêu cầu bị soft delete ngay lúc gửi nên không đăng
/// nhập lại được để tự huỷ.
/// </summary>
public class CancelDataDeletionRequestCommandHandlerTests
{
    private readonly Mock<IDataDeletionRequestRepository> _requestRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly CancelDataDeletionRequestCommandHandler _handler;

    private readonly Guid _adminId = Guid.NewGuid();

    public CancelDataDeletionRequestCommandHandlerTests()
    {
        _handler = new CancelDataDeletionRequestCommandHandler(
            _requestRepository.Object,
            _userRepository.Object,
            _auditLogService.Object,
            new CancelDataDeletionRequestCommandValidator());
    }

    private (DataDeletionRequest Request, User User) Setup(
        DataDeletionStatus status = DataDeletionStatus.Pending)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "a@b.c",
            DisplayName = "Người dùng",
            DeletedAt = DateTimeOffset.UtcNow.AddDays(-3),
        };
        var request = new DataDeletionRequest
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Status = status,
            RequestedAt = DateTimeOffset.UtcNow.AddDays(-3),
            ScheduledHardDeleteAt = DateTimeOffset.UtcNow.AddDays(27),
        };

        _requestRepository
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _userRepository
            .Setup(r => r.GetByIdIncludingDeletedAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        return (request, user);
    }

    private Task<FinMate.Application.Common.Models.DataDeletionRequestDto> ActAsync(Guid requestId)
        => _handler.HandleAsync(
            new CancelDataDeletionRequestCommand(_adminId, requestId, "người dùng đổi ý", "127.0.0.1"));

    [Fact]
    public async Task CancellingRestoresTheAccount()
    {
        // Bước quan trọng nhất. Thiếu nó thì yêu cầu biến khỏi hàng đợi nhưng người dùng vẫn
        // không đăng nhập được — huỷ mà không cứu được ai.
        var (request, user) = Setup();

        var result = await ActAsync(request.Id);

        user.DeletedAt.Should().BeNull();
        request.Status.Should().Be(DataDeletionStatus.Cancelled);
        request.ProcessedAt.Should().NotBeNull();
        result.Status.Should().Be(DataDeletionStatus.Cancelled);
    }

    [Fact]
    public async Task CancellingSavesOnceSoNothingIsLeftHalfDone()
    {
        // request và user cùng một DbContext; một lần lưu là atomic. Không được có trạng thái
        // "đã huỷ yêu cầu nhưng chưa khôi phục tài khoản".
        var (request, _) = Setup();

        await ActAsync(request.Id);

        _requestRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancellingIsWrittenToTheAuditLog()
    {
        var (request, _) = Setup();

        await ActAsync(request.Id);

        _auditLogService.Verify(
            a => a.LogAsync(AuditEvents.AdminDataDeletionCancelled, _adminId, "127.0.0.1",
                It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnAlreadyProcessedRequestCannotBeCancelled()
    {
        // ExecuteDeleteAsync đã chạy — nói thẳng thay vì để admin tưởng đã cứu được người dùng.
        var (request, user) = Setup(DataDeletionStatus.Processed);

        var act = () => ActAsync(request.Id);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.ErrorCode.Should().Be(AdminErrorCodes.DeletionAlreadyProcessed);

        user.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CancellingTwiceWritesNoSecondAuditLine()
    {
        var (request, _) = Setup(DataDeletionStatus.Cancelled);

        var result = await ActAsync(request.Id);

        result.Status.Should().Be(DataDeletionStatus.Cancelled);
        _requestRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _auditLogService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnUnknownRequestIsNotFound()
    {
        _requestRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataDeletionRequest?)null);

        var act = () => ActAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
