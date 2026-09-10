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
    private readonly DeleteTransactionCommandHandler _handler;

    public DeleteTransactionCommandHandlerTests()
    {
        _handler = new DeleteTransactionCommandHandler(_transactionRepository.Object, _financialAccountRepository.Object);
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
    }
}
