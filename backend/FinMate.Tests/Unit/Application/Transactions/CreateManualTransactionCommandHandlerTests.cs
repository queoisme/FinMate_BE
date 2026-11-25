using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
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
    private readonly Mock<IBudgetAlertNotifier> _budgetAlertNotifier = new();
    private readonly Mock<IAIServiceClient> _aiServiceClient = new();
    private readonly Mock<IReceiptScanRepository> _receiptScanRepository = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CreateManualTransactionCommandHandler _handler;

    public CreateManualTransactionCommandHandlerTests()
    {
        _handler = new CreateManualTransactionCommandHandler(
            _transactionRepository.Object,
            _financialAccountRepository.Object,
            _categoryRepository.Object,
            _budgetPeriodService.Object,
            _budgetAlertNotifier.Object,
            _aiServiceClient.Object,
            _receiptScanRepository.Object,
            _cache.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateManualTransactionCommandHandler>.Instance,
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

        var result = await _handler.HandleAsync(command);

        account.BalanceCents.Should().Be(170_000);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(TransactionStatus.Confirmed);
        saved.Source.Should().Be(TransactionSource.Manual);
        result.Transaction.Status.Should().Be("Confirmed");
        result.AlreadyExisted.Should().BeFalse();
    }

    // ------------------------------------------------------------ bộ nhớ món theo người dùng

    private readonly Mock<IItemMemoryService> _itemMemory = new();

    /// <summary>Dựng handler có dịch vụ bộ nhớ món; lần quét là bản TÁCH, mặc định nối thành công.</summary>
    private (CreateManualTransactionCommandHandler Handler, CreateManualTransactionCommand Command, Category Category) ItemMemorySetup(
        TransactionSource source = TransactionSource.Receipt, bool linked = true, string? group = "food")
    {
        var userId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 200_000 };
        var category = new Category { Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống" };
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        _categoryRepository.Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>())).ReturnsAsync(category);
        _receiptScanRepository.Setup(r => r.IsSplitAsync(scanId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _receiptScanRepository.Setup(r => r.LinkToTransactionAsync(scanId, userId, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(linked);
        var handler = new CreateManualTransactionCommandHandler(
            _transactionRepository.Object, _financialAccountRepository.Object, _categoryRepository.Object, _budgetPeriodService.Object,
            _budgetAlertNotifier.Object, _aiServiceClient.Object, _receiptScanRepository.Object, _cache.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateManualTransactionCommandHandler>.Instance,
            new CreateManualTransactionCommandValidator(), itemMemory: _itemMemory.Object);
        var command = new CreateManualTransactionCommand(
            userId, account.Id, category.Id, 16_000, TransactionType.Debit, DateTimeOffset.UtcNow, "Co.opmart", null,
            Source: source, ReceiptScanId: scanId, ReceiptGroup: group);
        return (handler, command, category);
    }

    [Fact]
    public async Task Create_ReceiptGroup_TeachesItemMemoryAfterTheLinkIsCreated()
    {
        var (handler, command, category) = ItemMemorySetup();
        var order = new List<string>();
        _receiptScanRepository.Setup(r => r.LinkToTransactionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("link")).ReturnsAsync(true);
        _itemMemory.Setup(m => m.LearnFromSavedGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Category?>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("learn")).Returns(Task.CompletedTask);

        await handler.HandleAsync(command);

        _itemMemory.Verify(m => m.LearnFromSavedGroupAsync(command.UserId, command.ReceiptScanId!.Value, "food", category, It.IsAny<CancellationToken>()), Times.Once);
        order.Should().Equal("link", "learn");
    }

    [Fact]
    public async Task Create_DoesNotTeachWhenTheLinkWasNotCreated()
    {
        var (handler, command, _) = ItemMemorySetup(linked: false);

        var result = await handler.HandleAsync(command);

        result.AlreadyExisted.Should().BeFalse();
        _itemMemory.Verify(m => m.LearnFromSavedGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Category?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_DoesNotTeachWhenTheLinkLookupThrew()
    {
        var (handler, command, _) = ItemMemorySetup();
        _receiptScanRepository.Setup(r => r.LinkToTransactionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await handler.HandleAsync(command);

        result.Transaction.AmountCents.Should().Be(16_000);
        _itemMemory.Verify(m => m.LearnFromSavedGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Category?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_NonReceiptSourceDoesNotTeach()
    {
        var (handler, command, _) = ItemMemorySetup(source: TransactionSource.Manual);

        await handler.HandleAsync(command);

        _itemMemory.Verify(m => m.LearnFromSavedGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Category?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_StillSavesWhenItemMemoryThrows()
    {
        var (handler, command, _) = ItemMemorySetup();
        _itemMemory.Setup(m => m.LearnFromSavedGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Category?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await handler.HandleAsync(command);

        result.AlreadyExisted.Should().BeFalse();
        result.Transaction.AmountCents.Should().Be(16_000);
        _transactionRepository.Verify(r => r.AddAsync(It.IsAny<FinMate.Domain.Entities.Transaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
