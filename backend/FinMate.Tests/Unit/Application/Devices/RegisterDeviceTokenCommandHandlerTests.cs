using FinMate.Application.Common.Interfaces;
using FinMate.Application.Devices.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Devices;

public class RegisterDeviceTokenCommandHandlerTests
{
    private readonly Mock<IDeviceTokenRepository> _repository = new();
    private readonly RegisterDeviceTokenCommandHandler _handler;

    private readonly Guid _userId = Guid.NewGuid();

    public RegisterDeviceTokenCommandHandlerTests()
    {
        _handler = new RegisterDeviceTokenCommandHandler(
            _repository.Object, new RegisterDeviceTokenCommandValidator());
    }

    private Task RegisterAsync(string token, Guid? userId = null)
        => _handler.HandleAsync(
            new RegisterDeviceTokenCommand(userId ?? _userId, token, DevicePlatform.Android));

    [Fact]
    public async Task AnUnseenTokenIsStored()
    {
        _repository
            .Setup(r => r.GetByTokenAsync("token-moi", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeviceToken?)null);

        await RegisterAsync("token-moi");

        _repository.Verify(
            r => r.AddAsync(
                It.Is<DeviceToken>(d => d.UserId == _userId
                    && d.Token == "token-moi"
                    && d.Platform == DevicePlatform.Android),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReopeningTheAppDoesNotCreateASecondRow()
    {
        // Client đăng ký lại mỗi lần mở app. Thêm dòng mới mỗi lần là mỗi thông báo gửi lặp
        // hàng chục lần xuống cùng một máy.
        var existing = new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Token = "token-cu",
            LastSeenAt = DateTimeOffset.UtcNow.AddDays(-30),
        };
        _repository
            .Setup(r => r.GetByTokenAsync("token-cu", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await RegisterAsync("token-cu");

        _repository.Verify(r => r.AddAsync(It.IsAny<DeviceToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        existing.LastSeenAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task AnotherUserOnTheSameHandsetTakesTheTokenOver()
    {
        // Ca đáng lo nhất của cả tính năng. Nếu dòng cũ vẫn trỏ về chủ cũ, thông báo tài
        // chính của người mới vẫn đẩy xuống đúng cái máy đó cho người cũ đọc.
        var previousOwner = Guid.NewGuid();
        var existing = new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = previousOwner,
            Token = "token-may-cu",
        };
        _repository
            .Setup(r => r.GetByTokenAsync("token-may-cu", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var newOwner = Guid.NewGuid();
        await RegisterAsync("token-may-cu", newOwner);

        existing.UserId.Should().Be(newOwner);
        _repository.Verify(r => r.AddAsync(It.IsAny<DeviceToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnEmptyTokenIsRejected()
    {
        var act = () => RegisterAsync("");

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task AnOversizedTokenIsRejectedBeforeItReachesTheIndex()
    {
        // Token nằm dưới index UNIQUE; btree của Postgres từ chối entry quá ~2704 byte. Không
        // chặn ở validator thì đây là 500 chứ không phải 400.
        var act = () => RegisterAsync(new string('x', 2000));

        await act.Should().ThrowAsync<ValidationException>();
    }
}
