using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.ValueObjects;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class FcmPushNotificationServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IDeviceTokenRepository> _deviceTokenRepository = new();
    private readonly Mock<IFcmSender> _sender = new();
    private readonly FcmPushNotificationService _service;

    private readonly Guid _userId = Guid.NewGuid();

    public FcmPushNotificationServiceTests()
    {
        _service = new FcmPushNotificationService(
            _userRepository.Object,
            _deviceTokenRepository.Object,
            _sender.Object,
            NullLogger<FcmPushNotificationService>.Instance);
    }

    private void Arrange(NotificationPreferences? prefs = null, params string[] tokens)
    {
        _userRepository
            .Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _userId, NotificationPrefs = prefs ?? new NotificationPreferences() });

        _deviceTokenRepository
            .Setup(r => r.GetForUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens.Select(t => new DeviceToken { Id = Guid.NewGuid(), UserId = _userId, Token = t }).ToList());
    }

    private void SenderReturns(params FcmSendOutcome[] outcomes)
        => _sender
            .Setup(s => s.SendAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcomes);

    private Task NotifyAsync() => _service.NotifyAsync(_userId, "Tiêu đề", "Nội dung", default);

    [Fact]
    public async Task SendsToEveryDeviceTheUserOwns()
    {
        Arrange(null, "token-dien-thoai", "token-may-tinh-bang");
        SenderReturns(
            new FcmSendOutcome("token-dien-thoai", Delivered: true, TokenIsDead: false),
            new FcmSendOutcome("token-may-tinh-bang", Delivered: true, TokenIsDead: false));

        await NotifyAsync();

        _sender.Verify(s => s.SendAsync(
            It.Is<IReadOnlyList<string>>(t => t.Count == 2
                && t.Contains("token-dien-thoai")
                && t.Contains("token-may-tinh-bang")),
            "Tiêu đề", "Nội dung", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PushTurnedOffSendsNothingAtAll()
    {
        // Hai trong bốn chỗ gọi push không tự kiểm cờ này. Chốt chặn nằm ở đây, nên một call
        // site mới quên kiểm cũng không lọt.
        Arrange(new NotificationPreferences { PushEnabled = false }, "token-dien-thoai");

        await NotifyAsync();

        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoRegisteredDeviceIsNotAnError()
    {
        Arrange();

        await NotifyAsync();

        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeletedUserSendsNothing()
    {
        _userRepository
            .Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        await NotifyAsync();

        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeadTokensArePrunedAndLiveOnesKept()
    {
        Arrange(null, "con-song", "da-chet");
        SenderReturns(
            new FcmSendOutcome("con-song", Delivered: true, TokenIsDead: false),
            new FcmSendOutcome("da-chet", Delivered: false, TokenIsDead: true));

        await NotifyAsync();

        _deviceTokenRepository.Verify(
            r => r.RemoveManyAsync(
                It.Is<IReadOnlyList<string>>(t => t.Count == 1 && t[0] == "da-chet"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ATemporaryFailureNeverDeletesAToken()
    {
        // Mất mạng hay FCM quá tải thì thiết bị vẫn tốt. Xoá token vì một lỗi tạm thời là tự
        // cắt đứt đường tới một máy còn sống, và không gì khôi phục lại được ngoài việc người
        // dùng mở app lần nữa.
        Arrange(null, "con-song");
        SenderReturns(new FcmSendOutcome("con-song", Delivered: false, TokenIsDead: false));

        await NotifyAsync();

        _deviceTokenRepository.Verify(
            r => r.RemoveManyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReachingNobodyIsLoggedAsAWarningWithTheReason()
    {
        // Ca thật khi thiếu quyền: FCM KHÔNG ném, nó trả lỗi theo từng token. Nếu chỉ đếm
        // "0 delivered" ở mức Information thì triệu chứng duy nhất là không ai nhận được gì
        // — loại hỏng im lặng đắt nhất để truy.
        var logger = new CountingLogger<FcmPushNotificationService>();
        var service = new FcmPushNotificationService(
            _userRepository.Object, _deviceTokenRepository.Object, _sender.Object, logger);

        Arrange(null, "con-song");
        SenderReturns(new FcmSendOutcome("con-song", Delivered: false, TokenIsDead: false, FailureReason: "Unauthenticated"));

        await service.NotifyAsync(_userId, "Tiêu đề", "Nội dung", default);

        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Unauthenticated");
    }

    [Fact]
    public async Task FcmBlowingUpDoesNotBlowUpTheCaller()
    {
        // Mọi chỗ gọi push đều đứng sau một giao dịch đã lưu. Ném ở đây là để FCM sập kéo
        // theo việc ghi nhận chi tiêu thất bại.
        Arrange(null, "con-song");
        _sender
            .Setup(s => s.SendAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("FCM unreachable"));

        var act = NotifyAsync;

        await act.Should().NotThrowAsync();
    }
}

/// <summary>Logger tối giản chỉ để bắt các dòng cảnh báo — Moq trên ILogger rất khó đọc.</summary>
internal class CountingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            Warnings.Add(formatter(state, exception));
        }
    }
}
