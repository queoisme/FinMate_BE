using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
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
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IAIServiceClient> _aiServiceClient = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly ConfirmTransactionCommandHandler _handler;

    public ConfirmTransactionCommandHandlerTests()
    {
        _handler = new ConfirmTransactionCommandHandler(
            _transactionRepository.Object,
            _financialAccountRepository.Object,
            _budgetPeriodService.Object,
            _budgetAlertNotifier.Object,
            _gamificationService.Object,
            _categoryRepository.Object,
            _aiServiceClient.Object,
            _cache.Object);
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
    public async Task HandleAsync_Confirming_AwardsExpAndAdvancesConfirmMissions()
    {
        var userId = Guid.NewGuid();
        var (transaction, account) = DraftDebit(userId, 50_000);
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));

        _gamificationService.Verify(s => s.RecordActivityAsync(
            It.Is<GamificationActivity>(a =>
                a.UserId == userId
                && a.ConditionType == MissionConditionType.ConfirmTransaction
                && a.ExpReward == 10),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_AlreadyConfirmed_AwardsNothing()
    {
        var userId = Guid.NewGuid();
        var (transaction, _) = DraftDebit(userId, 50_000);
        transaction.Status = TransactionStatus.Confirmed;
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        var act = () => _handler.HandleAsync(new ConfirmTransactionCommand(userId, transaction.Id));

        await act.Should().ThrowAsync<BusinessRuleException>();
        _gamificationService.VerifyNoOtherCalls();
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
}
