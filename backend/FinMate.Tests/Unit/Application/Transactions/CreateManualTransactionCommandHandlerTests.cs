using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

public class CreateManualTransactionCommandHandlerTests
{
    private readonly Mock<ITransactionRepository> _transactionRepository = new();
    private readonly Mock<IFinancialAccountRepository> _financialAccountRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<IBudgetPeriodService> _budgetPeriodService = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CreateManualTransactionCommandHandler _handler;

    public CreateManualTransactionCommandHandlerTests()
    {
        _handler = new CreateManualTransactionCommandHandler(
            _transactionRepository.Object,
            _financialAccountRepository.Object,
            _categoryRepository.Object,
            _budgetPeriodService.Object,
            _gamificationService.Object,
            _cache.Object,
            new CreateManualTransactionCommandValidator());
    }

    [Fact]
    public async Task HandleAsync_DebitTransaction_ConsumesBudgetImmediately()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 1_000_000 };
        var category = new Category { Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống" };
        var transactedAt = DateTimeOffset.UtcNow;

        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _categoryRepository.Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        await _handler.HandleAsync(new CreateManualTransactionCommand(
            userId, account.Id, category.Id, 45_000, TransactionType.Debit, transactedAt, null, null));

        // Giao dịch thủ công tạo thẳng ở Confirmed nên không đi qua ConfirmTransactionCommand —
        // hạn mức phải bị tiêu ngay tại đây.
        _budgetPeriodService.Verify(s => s.ApplyDeltaAsync(
            userId, category.Id, 45_000, transactedAt, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_AccountNotOwned_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        _financialAccountRepository.Setup(r => r.GetByIdAsync(accountId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinancialAccount?)null);

        var command = new CreateManualTransactionCommand(userId, accountId, null, 10_000, TransactionType.Debit, DateTimeOffset.UtcNow, null, null);
        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_NegativeAmount_ThrowsValidationException()
    {
        var command = new CreateManualTransactionCommand(Guid.NewGuid(), Guid.NewGuid(), null, -1, TransactionType.Debit, DateTimeOffset.UtcNow, null, null);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task HandleAsync_OtherUsersCategory_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 0 };
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var otherUsersCategory = new Category { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "X", Slug = "x" };
        _categoryRepository.Setup(r => r.GetByIdAsync(otherUsersCategory.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherUsersCategory);

        var command = new CreateManualTransactionCommand(
            userId, account.Id, otherUsersCategory.Id, 10_000, TransactionType.Debit, DateTimeOffset.UtcNow, null, null);
        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_DebitTransaction_SubtractsFromBalanceAndPersistsAsConfirmed()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 200_000 };
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        FinMate.Domain.Entities.Transaction? saved = null;
        _transactionRepository
            .Setup(r => r.AddAsync(It.IsAny<FinMate.Domain.Entities.Transaction>(), It.IsAny<CancellationToken>()))
            .Callback<FinMate.Domain.Entities.Transaction, CancellationToken>((t, _) => saved = t)
            .Returns(Task.CompletedTask);

        var command = new CreateManualTransactionCommand(
            userId, account.Id, null, 30_000, TransactionType.Debit, DateTimeOffset.UtcNow, "Coffee", "Sáng");

        var dto = await _handler.HandleAsync(command);

        account.BalanceCents.Should().Be(170_000);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(TransactionStatus.Confirmed);
        saved.Source.Should().Be(TransactionSource.Manual);
        dto.Status.Should().Be("Confirmed");
    }
}
