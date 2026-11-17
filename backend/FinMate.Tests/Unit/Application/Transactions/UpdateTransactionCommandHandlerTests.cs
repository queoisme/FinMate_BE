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

public class UpdateTransactionCommandHandlerTests
{
    private readonly Mock<ITransactionRepository> _transactionRepository = new();
    private readonly Mock<IFinancialAccountRepository> _financialAccountRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IAIServiceClient> _aiServiceClient = new();
    private readonly Mock<IBudgetPeriodService> _budgetPeriodService = new();
    private readonly Mock<IBudgetAlertNotifier> _budgetAlertNotifier = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly UpdateTransactionCommandHandler _handler;

    public UpdateTransactionCommandHandlerTests()
    {
        _handler = new UpdateTransactionCommandHandler(
            _transactionRepository.Object,
            _financialAccountRepository.Object,
            _categoryRepository.Object,
            _aiServiceClient.Object,
            _budgetPeriodService.Object,
            _budgetAlertNotifier.Object,
            _cache.Object,
            new Mock<IReceiptScanRepository>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdateTransactionCommandHandler>.Instance,
            new UpdateTransactionCommandValidator());
    }

    [Fact]
    public async Task HandleAsync_ConfirmedTransaction_AmountChanged_RevertsOldAndAppliesNewOnSameAccount()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 500_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            AmountCents = 50_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow,
            Source = TransactionSource.Manual,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // 500_000 (balance after original -50_000 debit was already applied) -> revert (+50_000) = 550_000
        // -> apply new -80_000 debit = 470_000
        var command = new UpdateTransactionCommand(
            userId, transaction.Id, account.Id, null, null, 80_000, TransactionType.Debit, DateTimeOffset.UtcNow, null, null);

        await _handler.HandleAsync(command);

        account.BalanceCents.Should().Be(470_000);
        transaction.AmountCents.Should().Be(80_000);
    }

    [Fact]
    public async Task HandleAsync_ConfirmedTransaction_AccountChanged_RevertsOldAccountAndAppliesNewAccount()
    {
        var userId = Guid.NewGuid();
        var oldAccount = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 100_000 };
        var newAccount = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 1_000_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = oldAccount.Id,
            AmountCents = 20_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow,
            Source = TransactionSource.Manual,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(oldAccount.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldAccount);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(newAccount.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAccount);

        var command = new UpdateTransactionCommand(
            userId, transaction.Id, newAccount.Id, null, null, 20_000, TransactionType.Debit, DateTimeOffset.UtcNow, null, null);

        await _handler.HandleAsync(command);

        oldAccount.BalanceCents.Should().Be(120_000);
        newAccount.BalanceCents.Should().Be(980_000);
        transaction.FinancialAccountId.Should().Be(newAccount.Id);
    }

    [Fact]
    public async Task HandleAsync_DraftTransaction_AmountChanged_DoesNotTouchBalance()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 500_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            AmountCents = 50_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Draft,
            Source = TransactionSource.Notification,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        var command = new UpdateTransactionCommand(
            userId, transaction.Id, account.Id, null, null, 999_000, TransactionType.Debit, DateTimeOffset.UtcNow, null, null);

        await _handler.HandleAsync(command);

        account.BalanceCents.Should().Be(500_000);
        _financialAccountRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_CategoryChangedOnNotificationSourcedTransaction_SendsFeedback()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 0, PackageName = "com.mbmobile" };
        var oldCategory = new Category { Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống" };
        var newCategory = new Category { Id = Guid.NewGuid(), Slug = "transport", Name = "Di chuyển" };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            FinancialAccount = account,
            CategoryId = oldCategory.Id,
            Category = oldCategory,
            AmountCents = 10_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Draft,
            Source = TransactionSource.Notification,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _categoryRepository.Setup(r => r.GetByIdAsync(newCategory.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newCategory);

        var command = new UpdateTransactionCommand(
            userId, transaction.Id, account.Id, null, newCategory.Id, 10_000, TransactionType.Debit, DateTimeOffset.UtcNow, null, null);

        await _handler.HandleAsync(command);

        _aiServiceClient.Verify(c => c.SendFeedbackAsync(
            It.Is<FeedbackRequest>(f => f.PredictedCategory == "food" && f.CorrectedCategory == "transport"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ConfirmedTransaction_CategoryChanged_MovesSpendBetweenBudgets()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 500_000 };
        var oldCategory = new Category { Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống" };
        var newCategory = new Category { Id = Guid.NewGuid(), Slug = "transport", Name = "Di chuyển" };
        var oldTransactedAt = DateTimeOffset.UtcNow.AddDays(-40);
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            CategoryId = oldCategory.Id,
            Category = oldCategory,
            AmountCents = 60_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Confirmed,
            Source = TransactionSource.Manual,
            TransactedAt = oldTransactedAt,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _categoryRepository.Setup(r => r.GetByIdAsync(newCategory.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newCategory);

        var newTransactedAt = DateTimeOffset.UtcNow;
        await _handler.HandleAsync(new UpdateTransactionCommand(
            userId, transaction.Id, account.Id, null, newCategory.Id, 90_000, TransactionType.Debit, newTransactedAt, null, null));

        // Hoàn lại theo category/số tiền/ngày CŨ...
        _budgetPeriodService.Verify(s => s.ApplyDeltaAsync(
            userId, oldCategory.Id, -60_000, oldTransactedAt, It.IsAny<CancellationToken>()),
            Times.Once);
        // ...rồi tính vào category/số tiền/ngày MỚI.
        _budgetPeriodService.Verify(s => s.ApplyDeltaAsync(
            userId, newCategory.Id, 90_000, newTransactedAt, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DraftTransaction_DoesNotTouchBudget()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 500_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            AmountCents = 50_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Draft,
            Source = TransactionSource.Notification,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        await _handler.HandleAsync(new UpdateTransactionCommand(
            userId, transaction.Id, account.Id, null, null, 999_000, TransactionType.Debit, DateTimeOffset.UtcNow, null, null));

        _budgetPeriodService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_UpdatingTransferAmount_RebalancesBothAccounts()
    {
        var userId = Guid.NewGuid();
        var from = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 4_000_000 };
        var to = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 1_000_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = from.Id,
            CounterAccountId = to.Id,
            AmountCents = 1_000_000,
            TransactionType = TransactionType.Transfer,
            Source = TransactionSource.Manual,
            Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(from.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(from);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(to.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(to);

        // Ghi nhầm 1tr, thực tế rút 1.5tr.
        await _handler.HandleAsync(new UpdateTransactionCommand(
            userId, transaction.Id, from.Id, to.Id, null, 1_500_000,
            TransactionType.Transfer, DateTimeOffset.UtcNow, null, null));

        from.BalanceCents.Should().Be(3_500_000);
        to.BalanceCents.Should().Be(1_500_000);
        (from.BalanceCents + to.BalanceCents).Should().Be(5_000_000);
    }

    [Fact]
    public async Task HandleAsync_TurningTransferIntoExpense_IsRejected()
    {
        var userId = Guid.NewGuid();
        var from = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 1_000_000 };
        var to = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 1_000_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = from.Id,
            CounterAccountId = to.Id,
            AmountCents = 500_000,
            TransactionType = TransactionType.Transfer,
            Source = TransactionSource.Manual,
            Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        var act = () => _handler.HandleAsync(new UpdateTransactionCommand(
            userId, transaction.Id, from.Id, null, null, 500_000,
            TransactionType.Debit, DateTimeOffset.UtcNow, null, null));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.ErrorCode.Should().Be(TransactionErrorCodes.TypeChangeNotAllowed);

        // Quan trọng: từ chối phải xảy ra TRƯỚC khi chạm số dư, không để lại nửa vời.
        from.BalanceCents.Should().Be(1_000_000);
        to.BalanceCents.Should().Be(1_000_000);
    }

    [Fact]
    public async Task HandleAsync_UpdatingTransfer_NeverConsumesBudget()
    {
        var userId = Guid.NewGuid();
        var from = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 4_000_000 };
        var to = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 0 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = from.Id,
            CounterAccountId = to.Id,
            AmountCents = 1_000_000,
            TransactionType = TransactionType.Transfer,
            Source = TransactionSource.Manual,
            Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(from.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(from);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(to.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(to);

        await _handler.HandleAsync(new UpdateTransactionCommand(
            userId, transaction.Id, from.Id, to.Id, null, 2_000_000,
            TransactionType.Transfer, DateTimeOffset.UtcNow, null, null));

        // Cả revert lẫn apply đều phải là 0 đồng — transfer không tiêu hạn mức ở bất kỳ chiều nào.
        _budgetPeriodService.Verify(s => s.ApplyDeltaAsync(
            userId, It.IsAny<Guid?>(), It.Is<long>(d => d != 0),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------ bộ nhớ món theo người dùng

    private (UpdateTransactionCommandHandler Handler, FinMate.Domain.Entities.Transaction Transaction, Category NewCategory, Mock<IItemMemoryService> ItemMemory)
        ItemMemorySetup(Guid userId, bool changeCategory)
    {
        var oldCategory = new Category { Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống" };
        var newCategory = changeCategory ? new Category { Id = Guid.NewGuid(), Slug = "household", Name = "Gia dụng" } : oldCategory;
        // Draft: không cộng trừ số dư hay ngân sách, thứ cần kiểm ở đây là việc ghi nhớ.
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = Guid.NewGuid(), CategoryId = oldCategory.Id, Category = oldCategory,
            AmountCents = 16_000, TransactionType = TransactionType.Debit, Status = TransactionStatus.Draft,
            Source = TransactionSource.Receipt, TransactedAt = DateTimeOffset.UtcNow,
        };
        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        _categoryRepository.Setup(r => r.GetByIdAsync(newCategory.Id, It.IsAny<CancellationToken>())).ReturnsAsync(newCategory);
        var itemMemory = new Mock<IItemMemoryService>();
        var handler = new UpdateTransactionCommandHandler(
            _transactionRepository.Object, _financialAccountRepository.Object, _categoryRepository.Object, _aiServiceClient.Object,
            _budgetPeriodService.Object, _budgetAlertNotifier.Object, _cache.Object, new Mock<IReceiptScanRepository>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdateTransactionCommandHandler>.Instance,
            new UpdateTransactionCommandValidator(), itemMemory: itemMemory.Object);
        return (handler, transaction, newCategory, itemMemory);
    }

    private static UpdateTransactionCommand ItemMemoryCommand(Guid userId, FinMate.Domain.Entities.Transaction t, Category category)
        => new(userId, t.Id, t.FinancialAccountId, null, category.Id, t.AmountCents, t.TransactionType, t.TransactedAt, null, null);

    [Fact]
    public async Task Update_CategoryChangeOnReceiptTransaction_TeachesCorrection()
    {
        var userId = Guid.NewGuid();
        var (handler, transaction, newCategory, itemMemory) = ItemMemorySetup(userId, changeCategory: true);

        await handler.HandleAsync(ItemMemoryCommand(userId, transaction, newCategory));

        itemMemory.Verify(m => m.LearnFromCorrectionAsync(userId, transaction.Id, newCategory, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_NoCategoryChange_DoesNotTeach()
    {
        var userId = Guid.NewGuid();
        var (handler, transaction, sameCategory, itemMemory) = ItemMemorySetup(userId, changeCategory: false);

        await handler.HandleAsync(ItemMemoryCommand(userId, transaction, sameCategory));

        itemMemory.Verify(m => m.LearnFromCorrectionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Category?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_StillSavesWhenItemMemoryThrows()
    {
        var userId = Guid.NewGuid();
        var (handler, transaction, newCategory, itemMemory) = ItemMemorySetup(userId, changeCategory: true);
        itemMemory.Setup(m => m.LearnFromCorrectionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Category?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await handler.HandleAsync(ItemMemoryCommand(userId, transaction, newCategory));

        result.CategoryId.Should().Be(newCategory.Id);
        _transactionRepository.Verify(r => r.UpdateAsync(transaction, It.IsAny<CancellationToken>()), Times.Once);
    }
}
