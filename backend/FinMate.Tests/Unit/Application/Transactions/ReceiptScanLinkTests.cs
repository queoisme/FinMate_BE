using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// Lưu một giao dịch điền từ ảnh hóa đơn thì nối nó với bản ghi quét (<c>receiptScanId</c>). Nối là siêu dữ liệu: mã sai không được làm hỏng việc lưu tiền.
/// </summary>
public class ReceiptScanLinkTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _scanId = Guid.NewGuid();
    private readonly FinancialAccount _account;
    private readonly Category _category = new() { Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống" };
    private readonly Mock<IReceiptScanRepository> _scans = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly CreateManualTransactionCommandHandler _handler;

    public ReceiptScanLinkTests()
    {
        _account = new FinancialAccount { Id = Guid.NewGuid(), UserId = _userId, BalanceCents = 100_000 };
        var accounts = new Mock<IFinancialAccountRepository>();
        accounts.Setup(r => r.GetByIdAsync(_account.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(_account);
        var categories = new Mock<ICategoryRepository>();
        categories.Setup(r => r.GetByIdAsync(_category.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_category);
        var budget = new Mock<IBudgetPeriodService>();
        budget.Setup(b => b.ApplyDeltaAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _handler = new CreateManualTransactionCommandHandler(
            _transactions.Object, accounts.Object, categories.Object, budget.Object, new Mock<IBudgetAlertNotifier>().Object,
            new Mock<IGamificationService>().Object, new Mock<IAIServiceClient>().Object, _scans.Object, new Mock<ICacheService>().Object,
            new CreateManualTransactionCommandValidator());
    }

    private CreateManualTransactionCommand Command(TransactionSource source, Guid? scanId, Guid? clientRequestId = null)
        => new(_userId, _account.Id, _category.Id, 16_000, TransactionType.Debit, DateTimeOffset.UtcNow, "Highlands Coffee", null,
            clientRequestId, source, scanId);

    private void LinkWasCalledWith(Guid transactionId, Times times) => _scans.Verify(
        r => r.LinkToTransactionAsync(_scanId, _userId, transactionId, It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task SavingFromAScanLinksTheScanToTheNewTransaction()
    {
        var created = await _handler.HandleAsync(Command(TransactionSource.Receipt, _scanId));

        LinkWasCalledWith(created.Transaction.Id, Times.Once());
    }

    [Theory]
    [InlineData(TransactionSource.Manual)]
    [InlineData(TransactionSource.Voice)]
    public async Task OnlyReceiptTransactionsAreLinked(TransactionSource source)
    {
        await _handler.HandleAsync(Command(source, _scanId));

        _scans.Verify(r => r.LinkToTransactionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithoutAScanIdNothingIsLinked()
    {
        await _handler.HandleAsync(Command(TransactionSource.Receipt, null));

        _scans.Verify(r => r.LinkToTransactionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AScanTheRepositoryRefusesDoesNotBreakTheSave()
    {
        // Mã sai, của người khác hoặc đã được nối: repository trả false, giao dịch vẫn phải được lưu.
        _scans.Setup(r => r.LinkToTransactionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var created = await _handler.HandleAsync(Command(TransactionSource.Receipt, _scanId));

        created.AlreadyExisted.Should().BeFalse();
        created.Transaction.AmountCents.Should().Be(16_000);
        _transactions.Verify(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AReplayOfTheSameSaveDoesNotLinkAgain()
    {
        var clientRequestId = Guid.NewGuid();
        var existing = new Transaction
        {
            Id = Guid.NewGuid(), UserId = _userId, FinancialAccountId = _account.Id, AmountCents = 16_000, TransactionType = TransactionType.Debit,
            Source = TransactionSource.Receipt, Status = TransactionStatus.Confirmed, TransactedAt = DateTimeOffset.UtcNow,
        };
        _transactions.Setup(r => r.GetByClientRequestIdAsync(_userId, clientRequestId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var created = await _handler.HandleAsync(Command(TransactionSource.Receipt, _scanId, clientRequestId));

        created.AlreadyExisted.Should().BeTrue();
        _scans.Verify(r => r.LinkToTransactionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
