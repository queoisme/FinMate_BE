using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Budgets;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Notifications;
using FinMate.Application.Notifications.Commands;
using FinMate.Application.QrTransfers;
using FinMate.Application.Transactions;
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
    private readonly Mock<ISystemConfigProvider> _config = new();
    private readonly Mock<ITransactionConfirmationEffects> _confirmationEffects = new();
    private readonly Mock<IMerchantCategoryOverrideRepository> _merchantCategoryOverrides = new();
    private readonly Mock<IBudgetPeriodService> _budgetPeriodService = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly AnalyzeNotificationCommandHandler _handler;

    public AnalyzeNotificationCommandHandlerTests()
    {
        _config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemConfigSnapshot(0, new SystemConfigDocument(), null));
        _confirmationEffects
            .Setup(e => e.ApplyAsync(It.IsAny<Transaction>(), It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BudgetAlert>)[]);
        _budgetPeriodService
            .Setup(s => s.ApplyDeltaAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BudgetAlert>)[]);

        _handler = new AnalyzeNotificationCommandHandler(
            _financialAccountRepository.Object,
            _notificationLogRepository.Object,
            _categoryRepository.Object,
            _transactionRepository.Object,
            _aiServiceClient.Object,
            _pushNotificationService.Object,
            new AnalyzeNotificationCommandValidator(),
            _config.Object,
            _confirmationEffects.Object,
            _merchantCategoryOverrides.Object,
            _budgetPeriodService.Object,
            _cache.Object);
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
        result.DraftTransactionId.Should().BeNull("lần xử lý trước đó không tạo ra giao dịch nháp nào (không phải tài chính, hoặc nghi trùng)");
        _aiServiceClient.Verify(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _notificationLogRepository.Verify(
            r => r.AddAsync(It.IsAny<NotificationLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_DuplicateOfANotificationThatCreatedADraft_ReturnsTheSameDraftId()
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), IsMonitored = true };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var existingLog = new NotificationLog { Id = Guid.NewGuid(), Status = NotificationLogStatus.Processed };
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLog);

        var draft = new Transaction { Id = Guid.NewGuid(), NotificationLogId = existingLog.Id, Status = TransactionStatus.Draft };
        _transactionRepository
            .Setup(r => r.GetByNotificationLogIdAsync(existingLog.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var result = await _handler.HandleAsync(Command());

        result.NotificationLogId.Should().Be(existingLog.Id);
        result.Status.Should().Be("Processed");
        result.DraftTransactionId.Should().Be(draft.Id, "đồng bộ lại thông báo cũ không được làm client mất liên kết tới giao dịch nháp đã tạo");
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
    public async Task HandleAsync_FinancialResult_LowConfidence_CreatesDraftTransactionAndNotifies()
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

        // Confidence dưới OneTapThreshold (0.85) — phải ra Draft, không Auto-commit.
        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(75_000, "debit", "Highlands Coffee", "Thanh toan", DateTimeOffset.UtcNow, 2_500_000, 0.70),
                new CategorizationResult("food", 0.75),
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
        _confirmationEffects.Verify(
            e => e.ApplyAsync(It.IsAny<Transaction>(), It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()), Times.Never);
        _pushNotificationService.Verify(
            p => p.NotifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_FinancialResult_HighConfidence_AutoCommitsAndUpdatesBalance()
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), IsMonitored = true, BalanceCents = 1_000_000 };
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

        // Confidence >= OneTapThreshold (0.85, min(0.95, 0.90)) — phải Auto-commit ngay.
        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(75_000, "debit", "Highlands Coffee", "Thanh toan", DateTimeOffset.UtcNow, 2_500_000, 0.95),
                new CategorizationResult("food", 0.90),
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
        savedTransaction!.Status.Should().Be(TransactionStatus.Confirmed);
        _confirmationEffects.Verify(
            e => e.ApplyAsync(It.Is<Transaction>(t => t.AmountCents == 75_000), account, It.IsAny<CancellationToken>()), Times.Once);
        _confirmationEffects.Verify(
            e => e.NotifyAsync(It.IsAny<Transaction>(), It.IsAny<IReadOnlyList<BudgetAlert>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_HighExtractionConfidence_LowCategorizationConfidence_StillAutoCommits()
    {
        // §7: ngưỡng Auto-commit chỉ xét ExtractionConfidence — danh mục không quen (vd người
        // nhận lạ) không được chặn Auto-commit của 1 giao dịch đọc số sạch.
        var account = new FinancialAccount { Id = Guid.NewGuid(), IsMonitored = true, BalanceCents = 1_000_000 };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(2_000, "debit", "Tuan", null, DateTimeOffset.UtcNow, 500_000, 0.95),
                new CategorizationResult(null, null),
                new DuplicateResult(false, null),
                new ModelVersions("1.0", "1.0", "1.0"),
                80));

        Transaction? savedTransaction = null;
        _transactionRepository
            .Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .Callback<Transaction, CancellationToken>((t, _) => savedTransaction = t)
            .Returns(Task.CompletedTask);

        var result = await _handler.HandleAsync(Command());

        result.DraftTransactionId.Should().NotBeNull();
        savedTransaction!.Status.Should().Be(TransactionStatus.Confirmed);
        _confirmationEffects.Verify(
            e => e.ApplyAsync(It.Is<Transaction>(t => t.AmountCents == 2_000), account, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UncertainResult_CreatesDraftButNeverAutoCommits()
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), IsMonitored = true };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        // confidence RẤT cao nhưng pipeline là Uncertain — vẫn phải Draft (§5.1/§16.1).
        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "uncertain",
                new ClassifierResult("uncertain", 0.5),
                new ExtractionResult(75_000, "debit", null, "Thanh toan", DateTimeOffset.UtcNow, null, 0.99),
                new CategorizationResult(null, 0.99),
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
        savedTransaction!.Status.Should().Be(TransactionStatus.Draft);
        _confirmationEffects.Verify(
            e => e.ApplyAsync(It.IsAny<Transaction>(), It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ExtractionHasProviderReference_PassesItToTheDuplicateCheck()
    {
        // 2026-11-13: VietinBank/§5.5 — mã tham chiếu phải tới được HasAuthoritativeDuplicateAsync
        // để 2 giao dịch THẬT khác nhau cùng ví+số tiền+chiều tiền trong cửa sổ kiểm tra trùng
        // không còn bị hiểu lầm là 1 giao dịch gửi lặp.
        var account = new FinancialAccount { Id = Guid.NewGuid(), IsMonitored = true, BalanceCents = 1_000_000 };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);
        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(2_000, "debit", "LE XUAN THO Chuyen tien; tai iPay", "ND:...", DateTimeOffset.UtcNow, 2_860_075, 0.98, "xxxxxxxx7666"),
                new CategorizationResult(null, null),
                new DuplicateResult(false, null),
                new ModelVersions("1.0", "1.0", "1.0"),
                11));

        var result = await _handler.HandleAsync(Command());

        result.DraftTransactionId.Should().NotBeNull();
        _transactionRepository.Verify(
            r => r.HasAuthoritativeDuplicateAsync(
                account.Id, 2_000, TransactionType.Debit, It.IsAny<DateTimeOffset>(),
                It.IsAny<TimeSpan>(), null, It.IsAny<CancellationToken>(), "xxxxxxxx7666"),
            Times.Once);
        _transactionRepository.Verify(
            r => r.AddAsync(It.Is<Transaction>(t => t.ProviderReference == "xxxxxxxx7666"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_BusinessDuplicateFound_DoesNotCreateTransaction()
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
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(75_000, "debit", "Highlands Coffee", "Thanh toan", DateTimeOffset.UtcNow, 2_500_000, 0.95),
                new CategorizationResult("food", 0.90),
                new DuplicateResult(false, null),
                new ModelVersions("1.0", "1.0", "1.0"),
                245));
        _transactionRepository
            .Setup(r => r.HasAuthoritativeDuplicateAsync(
                account.Id, 75_000, TransactionType.Debit, It.IsAny<DateTimeOffset>(),
                It.IsAny<TimeSpan>(), null, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        var result = await _handler.HandleAsync(Command());

        result.DraftTransactionId.Should().BeNull();
        _transactionRepository.Verify(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
        _pushNotificationService.Verify(
            p => p.NotifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MergeCandidateFound_ConvertsExistingDraftToConfirmedTransfer()
    {
        var userId = Guid.NewGuid();
        var incomingAccount = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, IsMonitored = true, BalanceCents = 1_000_000 };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(incomingAccount);
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);
        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(50_000, "credit", null, "Chuyen tien", DateTimeOffset.UtcNow, null, 0.70),
                new CategorizationResult(null, null),
                new DuplicateResult(false, null),
                new ModelVersions("1.0", "1.0", "1.0"),
                245));

        var otherAccount = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 500_000 };
        var mergeCandidate = new Transaction
        {
            Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = otherAccount.Id,
            AmountCents = 50_000, TransactionType = TransactionType.Debit, Status = TransactionStatus.Draft,
            TransactedAt = DateTimeOffset.UtcNow,
        };
        _transactionRepository
            .Setup(r => r.FindMergeCandidateAsync(
                It.IsAny<Guid>(), 50_000, TransactionType.Debit, incomingAccount.Id,
                It.IsAny<DateTimeOffset>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mergeCandidate);
        _financialAccountRepository
            .Setup(r => r.GetByIdAsync(otherAccount.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherAccount);

        var command = new AnalyzeNotificationCommand(userId, "com.mbmobile", "MB Bank", "TK 123: +50,000VND", DateTimeOffset.UtcNow);
        var result = await _handler.HandleAsync(command);

        result.DraftTransactionId.Should().Be(mergeCandidate.Id);
        _transactionRepository.Verify(
            r => r.UpdateAsync(It.Is<Transaction>(t =>
                t.Id == mergeCandidate.Id
                && t.TransactionType == TransactionType.Transfer
                && t.Status == TransactionStatus.Confirmed
                && t.CounterAccountId == incomingAccount.Id), It.IsAny<CancellationToken>()),
            Times.Once);
        _transactionRepository.Verify(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
        otherAccount.BalanceCents.Should().Be(450_000);
        incomingAccount.BalanceCents.Should().Be(1_050_000);
    }

    [Fact]
    public async Task HandleAsync_MergeCandidateFound_AlsoLinksTheQrIntentOfTheDebitLeg()
    {
        // Bug thật, 2026-11-13: tự chuyển cho chính mình giữa 2 ví BẰNG cách quét QR — nhánh merge
        // (§16.3) trước đây bỏ qua hoàn toàn việc khớp QR, nên QrTransferIntent không bao giờ được
        // đóng lại dù giao dịch đã đúng là Transfer: C3 kẹt "Chờ ngân hàng xác nhận" mãi mãi.
        var userId = Guid.NewGuid();
        var mbAccount = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, IsMonitored = true, BalanceCents = 1_000_000 };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mbAccount);
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);
        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(17_000, "debit", null, "Chuyen tien", DateTimeOffset.UtcNow, null, 0.95),
                new CategorizationResult(null, null),
                new DuplicateResult(false, null),
                new ModelVersions("1.0", "1.0", "1.0"),
                245));

        var vietinAccount = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 500_000 };
        var mergeCandidate = new Transaction
        {
            Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = vietinAccount.Id,
            AmountCents = 17_000, TransactionType = TransactionType.Credit, Status = TransactionStatus.Draft,
            TransactedAt = DateTimeOffset.UtcNow,
        };
        _transactionRepository
            .Setup(r => r.FindMergeCandidateAsync(
                It.IsAny<Guid>(), 17_000, TransactionType.Credit, mbAccount.Id,
                It.IsAny<DateTimeOffset>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mergeCandidate);
        _financialAccountRepository
            .Setup(r => r.GetByIdAsync(vietinAccount.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vietinAccount);

        var qrIntent = new QrTransferIntent { Id = Guid.NewGuid(), UserId = userId };
        var qrLinker = new Mock<IQrTransferLinker>();
        qrLinker
            .Setup(l => l.FindMatchAsync(userId, "com.mbmobile", It.IsAny<DateTimeOffset>(), 17_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(qrIntent);

        var handler = new AnalyzeNotificationCommandHandler(
            _financialAccountRepository.Object, _notificationLogRepository.Object, _categoryRepository.Object,
            _transactionRepository.Object, _aiServiceClient.Object, _pushNotificationService.Object,
            new AnalyzeNotificationCommandValidator(), _config.Object, _confirmationEffects.Object,
            _merchantCategoryOverrides.Object, _budgetPeriodService.Object, _cache.Object,
            qrLinker: qrLinker.Object);

        var command = new AnalyzeNotificationCommand(userId, "com.mbmobile", "MB Bank", "TK 123: -17,000VND", DateTimeOffset.UtcNow);
        var result = await handler.HandleAsync(command);

        result.DraftTransactionId.Should().Be(mergeCandidate.Id);
        qrLinker.Verify(l => l.LinkAsync(qrIntent, mergeCandidate.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_MergeCandidateAlreadyConfirmed_ReversesAppliedEffectsBeforeMerging()
    {
        // Issue A (§16.3 reversed 2026-11-13, Option A): vế đầu đã Auto-commit (Confirmed) TRƯỚC khi
        // vế đối ứng tới — merge phải đảo ngược hiệu ứng số dư/ngân sách đã áp cho vế đó, không chỉ
        // bỏ qua merge như hành vi v1 (chỉ merge khi còn Draft).
        var userId = Guid.NewGuid();
        var incomingAccount = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, IsMonitored = true, BalanceCents = 1_000_000 };
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(incomingAccount);
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);
        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeResponse(
                "financial",
                new ClassifierResult("financial", 0.97),
                new ExtractionResult(50_000, "credit", null, "Chuyen tien", DateTimeOffset.UtcNow, null, 0.70),
                new CategorizationResult(null, null),
                new DuplicateResult(false, null),
                new ModelVersions("1.0", "1.0", "1.0"),
                245));

        // Vế đầu đã Auto-commit từ trước: ví otherAccount đã bị trừ 50.000đ lúc đó (Confirmed Debit).
        var otherAccount = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 450_000 };
        var mergeCandidate = new Transaction
        {
            Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = otherAccount.Id,
            AmountCents = 50_000, TransactionType = TransactionType.Debit, Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow,
        };
        _transactionRepository
            .Setup(r => r.FindMergeCandidateAsync(
                It.IsAny<Guid>(), 50_000, TransactionType.Debit, incomingAccount.Id,
                It.IsAny<DateTimeOffset>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mergeCandidate);
        _financialAccountRepository
            .Setup(r => r.GetByIdAsync(otherAccount.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherAccount);

        var command = new AnalyzeNotificationCommand(userId, "com.mbmobile", "MB Bank", "TK 123: +50,000VND", DateTimeOffset.UtcNow);
        var result = await _handler.HandleAsync(command);

        result.DraftTransactionId.Should().Be(mergeCandidate.Id);
        // Đảo ngược: trả lại 50.000đ đã trừ lúc Auto-commit, rồi mới trừ lại đúng 1 lần cho Transfer.
        otherAccount.BalanceCents.Should().Be(450_000);
        incomingAccount.BalanceCents.Should().Be(1_050_000);
        _budgetPeriodService.Verify(
            s => s.ApplyDeltaAsync(userId, mergeCandidate.CategoryId, -50_000, mergeCandidate.TransactedAt, It.IsAny<CancellationToken>()),
            Times.Once);
        _transactionRepository.Verify(
            r => r.UpdateAsync(It.Is<Transaction>(t =>
                t.Id == mergeCandidate.Id
                && t.TransactionType == TransactionType.Transfer
                && t.Status == TransactionStatus.Confirmed
                && t.CounterAccountId == incomingAccount.Id), It.IsAny<CancellationToken>()),
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

    private AnalyzeNotificationCommandHandler HandlerWith(IAiConsentReader? consent) => new(
        _financialAccountRepository.Object, _notificationLogRepository.Object, _categoryRepository.Object,
        _transactionRepository.Object, _aiServiceClient.Object, _pushNotificationService.Object,
        new AnalyzeNotificationCommandValidator(), _config.Object, _confirmationEffects.Object,
        _merchantCategoryOverrides.Object, _budgetPeriodService.Object, _cache.Object, consent);

    private static Mock<IAiConsentReader> Consent(bool granted)
    {
        var consent = new Mock<IAiConsentReader>();
        consent.Setup(c => c.HasAiConsentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(granted);
        return consent;
    }

    private List<AnalyzeRequest> MonitoredAccountCapturingAnalyze()
    {
        var sent = new List<AnalyzeRequest>();
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialAccount { Id = Guid.NewGuid(), IsMonitored = true });
        _aiServiceClient
            .Setup(c => c.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AnalyzeRequest, CancellationToken>((r, _) => sent.Add(r))
            .ReturnsAsync(new AnalyzeResponse(
                "non_financial", new ClassifierResult("non_financial", 0.9), null, null, null, new ModelVersions("1", "1", "1"), 10));
        return sent;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_SendsCurrentConsentAsAllowAi(bool granted)
    {
        var sent = MonitoredAccountCapturingAnalyze();

        await HandlerWith(Consent(granted).Object).HandleAsync(Command());

        sent.Should().ContainSingle().Which.AllowAi.Should().Be(granted);
    }

    [Fact]
    public async Task HandleAsync_WithoutConsentReader_SendsAllowAiFalse()
    {
        var sent = MonitoredAccountCapturingAnalyze();

        await HandlerWith(null).HandleAsync(Command());

        sent.Should().ContainSingle().Which.AllowAi.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_IgnoredPackage_NeverReadsConsent()
    {
        _financialAccountRepository
            .Setup(r => r.GetByUserAndMonitoredPackageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinancialAccount?)null);
        var consent = Consent(true);

        await HandlerWith(consent.Object).HandleAsync(Command());

        consent.Verify(c => c.HasAiConsentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_DuplicateHash_NeverReadsConsent()
    {
        _notificationLogRepository
            .Setup(r => r.GetProcessedByContentHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationLog { Id = Guid.NewGuid(), Status = NotificationLogStatus.Processed });
        var consent = Consent(true);

        await HandlerWith(consent.Object).HandleAsync(Command());

        consent.Verify(c => c.HasAiConsentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
