using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Notifications.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Notifications;

public class AnalyzeNotificationCommandHandlerTests
{
    private readonly Mock<IFinancialAccountRepository> _financialAccountRepository = new();
    private readonly Mock<INotificationLogRepository> _notificationLogRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<ITransactionRepository> _transactionRepository = new();
    private readonly Mock<IAIServiceClient> _aiServiceClient = new();
    private readonly Mock<IPushNotificationService> _pushNotificationService = new();
    private readonly AnalyzeNotificationCommandHandler _handler;

    public AnalyzeNotificationCommandHandlerTests()
    {
        _handler = new AnalyzeNotificationCommandHandler(
            _financialAccountRepository.Object,
            _notificationLogRepository.Object,
            _categoryRepository.Object,
            _transactionRepository.Object,
            _aiServiceClient.Object,
            _pushNotificationService.Object,
            new AnalyzeNotificationCommandValidator());
    }

    private static AnalyzeNotificationCommand Command(string packageName = "com.mbmobile") =>
        new(Guid.NewGuid(), packageName, "MB Bank", "TK 123: -75,000VND tai Highlands", DateTimeOffset.UtcNow);

    [Fact]
    public async Task HandleAsync_PackageNotMonitored_MarksIgnoredAndDoesNotCallAI()
    {
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinancialAccount?)null);

        var result = await _handler.HandleAsync(Command());

        result.Status.Should().Be("Ignored");
        _aiServiceClient.Verify(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _notificationLogRepository.Verify(
            r => r.AddAsync(It.Is<NotificationLog>(n => n.Status == NotificationLogStatus.Ignored), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DuplicateWithinWindow_ReturnsExistingLogWithoutCallingAI()
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), IsMonitored = true };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var existingLog = new NotificationLog { Id = Guid.NewGuid(), Status = NotificationLogStatus.Processed };
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLog);

        var result = await _handler.HandleAsync(Command());

        result.NotificationLogId.Should().Be(existingLog.Id);
        result.Status.Should().Be("Processed");
        _aiServiceClient.Verify(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _notificationLogRepository.Verify(
            r => r.AddAsync(It.IsAny<NotificationLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AIServiceUnavailable_MarksFailedAndRethrows()
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), IsMonitored = true };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);
        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("down"));

        var act = () => _handler.HandleAsync(Command());

        await act.Should().ThrowAsync<AIServiceUnavailableException>();
        _notificationLogRepository.Verify(
            r => r.UpdateAsync(It.Is<NotificationLog>(n => n.Status == NotificationLogStatus.Failed), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_FinancialResult_CreatesDraftTransactionAndNotifies()
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), IsMonitored = true };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        var category = new Category { Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống" };
        _categoryRepository
            .Setup(r => r.GetSystemBySlugAsync("food", It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(75_000, "debit", "Highlands Coffee", "Thanh toan", DateTimeOffset.UtcNow, 2_500_000, 0.91),
                new CategorizationResult("food", 0.89),
                new DuplicateResult(false, null),
                new ModelVersions("1.0", "1.0", "1.0"),
                245));

        Transaction? savedTransaction = null;
        _transactionRepository
            .Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .Callback<Transaction, CancellationToken>((t, _) => savedTransaction = t)
            .Returns(Task.CompletedTask);

        var result = await _handler.HandleAsync(Command());

        result.DraftTransactionId.Should().NotBeNull();
        savedTransaction.Should().NotBeNull();
        savedTransaction!.AmountCents.Should().Be(75_000);
        savedTransaction.TransactionType.Should().Be(TransactionType.Debit);
        savedTransaction.Status.Should().Be(TransactionStatus.Draft);
        savedTransaction.Source.Should().Be(TransactionSource.Notification);
        savedTransaction.CategoryId.Should().Be(category.Id);
        _pushNotificationService.Verify(
            p => p.NotifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NonFinancialResult_DoesNotCreateTransaction()
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), IsMonitored = true };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);
        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "non_financial", new ClassifierResult("non_financial", 0.95), null, null, null, null, 12));

        var result = await _handler.HandleAsync(Command());

        result.DraftTransactionId.Should().BeNull();
        _transactionRepository.Verify(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
        _pushNotificationService.Verify(
            p => p.NotifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
