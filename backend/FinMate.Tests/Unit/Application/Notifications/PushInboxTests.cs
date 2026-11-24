using FinMate.Application.Common.Interfaces;
using FinMate.Application.Notifications;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Notifications;

/// <summary>Mọi push vào hộp thư trong app kèm data, để bấm vào mục hộp thư mở cùng màn với push.</summary>
public class PushInboxTests
{
    private readonly Mock<IUserNotificationRepository> _repository = new();
    private readonly UserNotificationService _service;

    public PushInboxTests() => _service = new UserNotificationService(_repository.Object);

    [Theory]
    [InlineData("type", "transaction_review", null, UserNotificationType.TransactionReview)]
    [InlineData("action", "budget_alert", null, UserNotificationType.BudgetThreshold)]
    [InlineData("action", "goal_deadline", null, UserNotificationType.GoalMilestone)]
    [InlineData("action", "forecast_alert", "bill_due", UserNotificationType.RecurringBillDue)]
    [InlineData("action", "forecast_alert", "anomaly", UserNotificationType.AnomalyDetected)]
    [InlineData("action", "forecast_alert", "low_balance", UserNotificationType.ForecastAlert)]
    [InlineData("action", "support_ticket", null, UserNotificationType.General)]
    public void Classify_MapsPushDataToInboxType(string key, string value, string? kind, UserNotificationType expected)
    {
        var data = new Dictionary<string, string> { [key] = value };
        if (kind is not null) data["kind"] = kind;

        UserNotificationService.Classify(data).Type.Should().Be(expected);
    }

    [Fact]
    public async Task RecordPush_SavesTitleBodyAndDataAsNewItem()
    {
        var userId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        UserNotification? saved = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<UserNotification>(), It.IsAny<CancellationToken>()))
            .Callback<UserNotification, CancellationToken>((n, _) => saved = n);

        await _service.RecordPushAsync(userId, "Có giao dịch cần xác nhận", "Nhận 5.000đ",
            new Dictionary<string, string> { ["type"] = "transaction_review", ["transactionId"] = txId.ToString() });

        saved.Should().NotBeNull();
        saved!.Type.Should().Be(UserNotificationType.TransactionReview);
        saved.RelatedEntityId.Should().Be(txId);
        saved.Data.Should().Contain("transaction_review").And.Contain(txId.ToString());
    }

    [Fact]
    public async Task RecordPush_AttachesDataToItemTheSenderAlreadyWrote_InsteadOfDuplicating()
    {
        // Cảnh báo hóa đơn: ForecastAlertService ghi mục hộp thư lúc phát hiện, outbox đẩy push sau đó.
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var existing = new UserNotification { UserId = userId, Type = UserNotificationType.RecurringBillDue, RelatedEntityId = itemId };
        _repository.Setup(r => r.FindWithoutPushAsync(userId, UserNotificationType.RecurringBillDue, itemId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await _service.RecordPushAsync(userId, "Sắp đến hạn Điện", "500.000đ đến hạn ngày 25.",
            new Dictionary<string, string> { ["action"] = "forecast_alert", ["kind"] = "bill_due", ["recurringItemId"] = itemId.ToString() });

        existing.Data.Should().Contain(itemId.ToString());
        _repository.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.AddAsync(It.IsAny<UserNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Decorator_RecordsThenPushes_AndStillPushesWhenInboxFails()
    {
        var inner = new Mock<IPushNotificationService>();
        var inbox = new Mock<IUserNotificationService>();
        inbox.Setup(i => i.RecordPushAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var push = new InboxRecordingPushNotificationService(inner.Object, inbox.Object, NullLogger<InboxRecordingPushNotificationService>.Instance);

        await push.NotifyAsync(Guid.NewGuid(), "t", "b");

        inner.Verify(p => p.NotifyAsync(It.IsAny<Guid>(), "t", "b", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
