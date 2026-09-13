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
}
