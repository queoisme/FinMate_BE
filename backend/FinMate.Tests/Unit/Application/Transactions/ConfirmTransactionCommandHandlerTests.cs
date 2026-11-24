using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

public class ConfirmTransactionCommandHandlerTests
{
    private readonly Mock<ITransactionRepository> _transactionRepository = new();
    private readonly Mock<IFinancialAccountRepository> _financialAccountRepository = new();
    private readonly Mock<IBudgetPeriodService> _budgetPeriodService = new();
    private readonly Mock<IBudgetAlertNotifier> _budgetAlertNotifier = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IAIServiceClient> _aiServiceClient = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IMerchantCategoryOverrideRepository> _merchantCategoryOverrides = new();
    private readonly ConfirmTransactionCommandHandler _handler;

    public ConfirmTransactionCommandHandlerTests()
    {
        _handler = new ConfirmTransactionCommandHandler(
            _transactionRepository.Object,
            _financialAccountRepository.Object,
            _budgetPeriodService.Object,
            _budgetAlertNotifier.Object,
            _categoryRepository.Object,
            _aiServiceClient.Object,
            _cache.Object,
            merchantCategoryOverrides: _merchantCategoryOverrides.Object);
    }

    private static (FinMate.Domain.Entities.Transaction Transaction, FinancialAccount Account) DraftDebit(Guid userId, long amount = 50_000)
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 1_000_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            AmountCents = amount,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Draft,
            TransactedAt = DateTimeOffset.UtcNow,
        };
        return (transaction, account);
    }

    [Fact]
    public async Task HandleAsync_TransactionNotFound_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        _transactionRepository.Setup(r => r.GetByIdAsync(id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinMate.Domain.Entities.Transaction?)null);

        var act = () => _handler.HandleAsync(new ConfirmTransactionCommand(userId, id));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_AlreadyConfirmed_ThrowsBusinessRuleException()
    {
        var userId = Guid.NewGuid();
        var (transaction, _) = DraftDebit(userId);
        transaction.Status = TransactionStatus.Confirmed;
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        var act = () => _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.ErrorCode == TransactionErrorCodes.NotDraft);
    }

    [Fact]
    public async Task HandleAsync_DebitDraft_SubtractsFromBalanceAndConfirms()
    {
        var userId = Guid.NewGuid();
        var (transaction, account) = DraftDebit(userId, 50_000);
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var dto = await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));

        account.BalanceCents.Should().Be(950_000);
        transaction.Status.Should().Be(TransactionStatus.Confirmed);
        dto.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task HandleAsync_DraftFromBankNotification_WritesBalanceChangeToInbox()
    {
        // Giao dịch bắt từ thông báo ngân hàng chỉ vào hộp thư (tab Biến động) khi được xác nhận ở đây.
        var userId = Guid.NewGuid();
        var (transaction, account) = DraftDebit(userId, 45_000);
        transaction.Source = TransactionSource.Notification;
        transaction.MerchantName = "Grab";
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        var inbox = new Mock<FinMate.Application.Notifications.IUserNotificationService>();
        var handler = new ConfirmTransactionCommandHandler(
            _transactionRepository.Object, _financialAccountRepository.Object, _budgetPeriodService.Object,
            _budgetAlertNotifier.Object, _categoryRepository.Object, _aiServiceClient.Object, _cache.Object,
            userNotifications: inbox.Object);

        await handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));

        inbox.Verify(n => n.CreateAsync(
            userId, UserNotificationType.BalanceChange, "Giao dịch mới", It.Is<string>(b => b.StartsWith("Chi ") && b.EndsWith(" tại Grab")),
            "Transaction", transaction.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_CreditDraft_AddsToBalance()
    {
        var userId = Guid.NewGuid();
        var (transaction, account) = DraftDebit(userId, 50_000);
        transaction.TransactionType = TransactionType.Credit;
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));

        account.BalanceCents.Should().Be(1_050_000);
    }

    private async Task<FinancialAccount> ConfirmNotificationDraftAsync(TransactionType type, long amount, long? bankBalance, bool newerConfirmedExists, TransactionSource source = TransactionSource.Notification)
    {
        var userId = Guid.NewGuid();
        var (transaction, account) = DraftDebit(userId, amount);
        transaction.TransactionType = type;
        transaction.Source = source;
        transaction.BalanceAfterCents = bankBalance;
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        _transactionRepository.Setup(r => r.HasConfirmedAfterAsync(account.Id, transaction.TransactedAt, transaction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newerConfirmedExists);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));
        return account;
    }

    [Fact]
    public async Task HandleAsync_NotificationWithBankBalance_SetsAccountBalanceToBankBalance()
    {
        // Ví đang ghi 1.000.000 nhưng ngân hàng báo còn 10.000: ngân hàng đúng, không cộng dồn lên số cũ.
        var account = await ConfirmNotificationDraftAsync(TransactionType.Credit, 5_000, bankBalance: 10_000, newerConfirmedExists: false);

        account.BalanceCents.Should().Be(10_000);
    }

    [Fact]
    public async Task HandleAsync_NotificationWithBankBalanceButNewerTransactionExists_AppliesDeltaOnly()
    {
        var account = await ConfirmNotificationDraftAsync(TransactionType.Debit, 5_000, bankBalance: 10_000, newerConfirmedExists: true);

        account.BalanceCents.Should().Be(995_000);
    }

    [Fact]
    public async Task HandleAsync_ManualDraftIgnoresBalanceAfter()
    {
        var account = await ConfirmNotificationDraftAsync(TransactionType.Credit, 5_000, bankBalance: 10_000, newerConfirmedExists: false, TransactionSource.Manual);

        account.BalanceCents.Should().Be(1_005_000);
    }

    [Fact]
    public async Task HandleAsync_DebitDraft_AddsSpendToBudgetPeriod()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var (transaction, account) = DraftDebit(userId, 50_000);
        transaction.CategoryId = categoryId;
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));

        _budgetPeriodService.Verify(s => s.ApplyDeltaAsync(
            userId, categoryId, 50_000, transaction.TransactedAt, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_CreditDraft_DoesNotConsumeBudget()
    {
        var userId = Guid.NewGuid();
        var (transaction, account) = DraftDebit(userId, 50_000);
        transaction.TransactionType = TransactionType.Credit;
        transaction.CategoryId = Guid.NewGuid();
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));

        // Tiền vào không tiêu hạn mức chi tiêu — delta phải là 0.
        _budgetPeriodService.Verify(s => s.ApplyDeltaAsync(
            userId, It.IsAny<Guid?>(), 0, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_AlreadyConfirmed_ThrowsAndSavesNothing()
    {
        var userId = Guid.NewGuid();
        var (transaction, _) = DraftDebit(userId, 50_000);
        transaction.Status = TransactionStatus.Confirmed;
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        var act = () => _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));

        await act.Should().ThrowAsync<BusinessRuleException>();
        _transactionRepository.Verify(
            r => r.UpdateAsync(It.IsAny<FinMate.Domain.Entities.Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------- docx bước 5.3: chọn danh mục khi xác nhận

    private (FinMate.Domain.Entities.Transaction Transaction, Category Chosen) ArrangeCategoryChoice(
        Guid userId, string predictedSlug = "other", Guid? chosenOwnerId = null)
    {
        var (transaction, account) = DraftDebit(userId);
        transaction.Source = TransactionSource.Notification;
        transaction.CategoryId = Guid.NewGuid();
        transaction.Category = new Category
        {
            Id = transaction.CategoryId.Value, Slug = predictedSlug, Name = "Khác",
        };

        var chosen = new Category
        {
            Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống", UserId = chosenOwnerId,
        };

        _transactionRepository
            .Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository
            .Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _categoryRepository
            .Setup(r => r.GetByIdAsync(chosen.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chosen);

        return (transaction, chosen);
    }

    [Fact]
    public async Task ChoosingACategoryChargesThatCategorysBudget()
    {
        // Trừ vào danh mục AI ĐOÁN thay vì danh mục người dùng CHỌN là tiền vào sai ngân sách
        // mà không có gì báo — số vẫn cộng đủ, chỉ nằm nhầm chỗ.
        var userId = Guid.NewGuid();
        var (transaction, chosen) = ArrangeCategoryChoice(userId);

        await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id, chosen.Id));

        _budgetPeriodService.Verify(
            s => s.ApplyDeltaAsync(userId, chosen.Id, It.IsAny<long>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
        transaction.CategoryId.Should().Be(chosen.Id);
    }

    [Fact]
    public async Task CorrectingTheCategoryTeachesTheAi()
    {
        // Bảng tình huống biên của docx: "lưu lại lựa chọn của người dùng để cải thiện thuật
        // toán sau này". Người dùng vừa sửa đúng cái AI đoán sai — tín hiệu quý nhất có được.
        var userId = Guid.NewGuid();
        var (transaction, chosen) = ArrangeCategoryChoice(userId, predictedSlug: "other");

        await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id, chosen.Id));

        _aiServiceClient.Verify(
            c => c.SendFeedbackAsync(
                It.Is<FeedbackRequest>(f => f.PredictedCategory == "other"
                    && f.CorrectedCategory == "food"
                    && f.FeedbackType == "category_correction"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConfirmingWithoutChoosingKeepsTheAiCategoryAndSendsNoFeedback()
    {
        // Nhánh một chạm ở bước 5.2 gửi body rỗng; client cũ cũng vậy. Không được đổi gì.
        var userId = Guid.NewGuid();
        var (transaction, _) = ArrangeCategoryChoice(userId);
        var predicted = transaction.CategoryId;

        await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));

        transaction.CategoryId.Should().Be(predicted);
        _aiServiceClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnotherUsersCategoryIsRefused()
    {
        var userId = Guid.NewGuid();
        var (transaction, chosen) = ArrangeCategoryChoice(userId, chosenOwnerId: Guid.NewGuid());
        var predicted = transaction.CategoryId;

        var act = () => _handler.HandleAsync(
            new ConfirmTransactionCommand(userId, transaction.Id, chosen.Id));

        await act.Should().ThrowAsync<NotFoundException>();
        transaction.CategoryId.Should().Be(predicted, "bị từ chối thì không được đổi gì");
        transaction.Status.Should().Be(TransactionStatus.Draft);
    }

    [Fact]
    public async Task ChoosingACategoryForANewMerchantSavesAnOverride()
    {
        var userId = Guid.NewGuid();
        var (transaction, chosen) = ArrangeCategoryChoice(userId);
        transaction.MerchantName = "Highlands Coffee";
        _merchantCategoryOverrides
            .Setup(r => r.GetAsync(userId, MerchantCategoryKey.Normalize("Highlands Coffee"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MerchantCategoryOverride?)null);

        await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id, chosen.Id));

        _merchantCategoryOverrides.Verify(
            r => r.AddAsync(
                It.Is<MerchantCategoryOverride>(o => o.UserId == userId && o.CategoryId == chosen.Id && o.DisplayName == "Highlands Coffee"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChoosingACategoryForAKnownMerchantUpdatesTheExistingOverride()
    {
        var userId = Guid.NewGuid();
        var (transaction, chosen) = ArrangeCategoryChoice(userId);
        transaction.MerchantName = "Highlands Coffee";
        var existing = new MerchantCategoryOverride
        {
            UserId = userId,
            MerchantKey = MerchantCategoryKey.Normalize("Highlands Coffee"),
            DisplayName = "Highlands Coffee",
            CategoryId = Guid.NewGuid(),
        };
        _merchantCategoryOverrides
            .Setup(r => r.GetAsync(userId, MerchantCategoryKey.Normalize("Highlands Coffee"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id, chosen.Id));

        existing.CategoryId.Should().Be(chosen.Id);
        _merchantCategoryOverrides.Verify(
            r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _merchantCategoryOverrides.Verify(
            r => r.AddAsync(It.IsAny<MerchantCategoryOverride>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
