using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

public class DeleteTransactionCommandHandlerTests
{
    private readonly Mock<ITransactionRepository> _transactionRepository = new();
    private readonly Mock<IFinancialAccountRepository> _financialAccountRepository = new();
    private readonly Mock<IBudgetPeriodService> _budgetPeriodService = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly DeleteTransactionCommandHandler _handler;

    public DeleteTransactionCommandHandlerTests()
    {
        _handler = new DeleteTransactionCommandHandler(
            _transactionRepository.Object,
            _financialAccountRepository.Object,
            _budgetPeriodService.Object,
            _gamificationService.Object,
            _cache.Object);
    }

    [Fact]
    public async Task HandleAsync_NotFound_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        _transactionRepository.Setup(r => r.GetByIdAsync(id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinMate.Domain.Entities.Transaction?)null);

        var act = () => _handler.HandleAsync(new DeleteTransactionCommand(userId, id));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_ConfirmedCreditTransaction_RevertsBalanceAndSoftDeletes()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 500_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            AmountCents = 100_000,
            TransactionType = TransactionType.Credit,
            Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _handler.HandleAsync(new DeleteTransactionCommand(userId, transaction.Id));

        account.BalanceCents.Should().Be(400_000);
        transaction.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_DraftTransaction_DoesNotTouchBalance()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 500_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            AmountCents = 100_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Draft,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        await _handler.HandleAsync(new DeleteTransactionCommand(userId, transaction.Id));

        transaction.DeletedAt.Should().NotBeNull();
        _financialAccountRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _budgetPeriodService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_ConfirmedDebit_RevertsBudgetSpendWithNegativeDelta()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 500_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            CategoryId = categoryId,
            AmountCents = 120_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow.AddDays(-2),
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _handler.HandleAsync(new DeleteTransactionCommand(userId, transaction.Id));

        _budgetPeriodService.Verify(s => s.ApplyDeltaAsync(
            userId, categoryId, -120_000, transaction.TransactedAt, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ConfirmedTransaction_TakesBackTheExpItAwarded()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 500_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            AmountCents = 120_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _handler.HandleAsync(new DeleteTransactionCommand(userId, transaction.Id));

        // Đúng bằng số EXP đã cộng lúc confirm, nếu không user farm được bằng thêm/xóa.
        _gamificationService.Verify(
            s => s.RevertExpAsync(userId, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DraftTransaction_HasNoExpToTakeBack()
    {
        var userId = Guid.NewGuid();
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = Guid.NewGuid(),
            AmountCents = 100_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Draft,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        await _handler.HandleAsync(new DeleteTransactionCommand(userId, transaction.Id));

        _gamificationService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_ConfirmedTransfer_RevertsBothAccounts()
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

        await _handler.HandleAsync(new DeleteTransactionCommand(userId, transaction.Id));

        // Tiền quay về ví nguồn VÀ bị lấy khỏi ví đích. Quên vế thứ hai là tự nhân đôi tài sản.
        from.BalanceCents.Should().Be(5_000_000);
        to.BalanceCents.Should().Be(0);
        transaction.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_ConfirmedTransfer_DoesNotTouchBudget()
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
        _financialAccountRepository.Setup(r => r.GetByIdAsync(from.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(from);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(to.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(to);

        await _handler.HandleAsync(new DeleteTransactionCommand(userId, transaction.Id));

        // Xóa transfer phải hoàn 0 đồng hạn mức — vì lúc tạo nó cũng chưa từng tiêu đồng nào.
        _budgetPeriodService.Verify(s => s.ApplyDeltaAsync(
            userId, It.IsAny<Guid?>(), 0, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ManualTransaction_RefundsOnlyTheExpItGranted()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 1_000_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            AmountCents = 100_000,
            TransactionType = TransactionType.Debit,
            Source = TransactionSource.Manual,
            Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _handler.HandleAsync(new DeleteTransactionCommand(userId, transaction.Id));

        // Giao dịch tự nhập chỉ được +5 lúc tạo, nên xóa chỉ được trừ lại 5. Trừ 10 (mức của
        // giao dịch từ thông báo) sẽ ăn mất 5 EXP mà user chưa từng được cộng.
        _gamificationService.Verify(g => g.RevertExpAsync(userId, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NotificationTransaction_RefundsTheConfirmReward()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 1_000_000 };
        var transaction = new FinMate.Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            AmountCents = 100_000,
            TransactionType = TransactionType.Debit,
            Source = TransactionSource.Notification,
            Status = TransactionStatus.Confirmed,
            TransactedAt = DateTimeOffset.UtcNow,
        };

        _transactionRepository.Setup(r => r.GetByIdAsync(transaction.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _handler.HandleAsync(new DeleteTransactionCommand(userId, transaction.Id));

        _gamificationService.Verify(g => g.RevertExpAsync(userId, 10, It.IsAny<CancellationToken>()), Times.Once);
    }
}
