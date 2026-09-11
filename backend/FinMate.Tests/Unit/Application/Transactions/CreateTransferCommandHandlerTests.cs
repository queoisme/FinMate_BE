using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Gamification;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

public class CreateTransferCommandHandlerTests
{
    private readonly Mock<ITransactionRepository> _transactionRepository = new();
    private readonly Mock<IFinancialAccountRepository> _financialAccountRepository = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CreateTransferCommandHandler _handler;

    public CreateTransferCommandHandlerTests()
    {
        _handler = new CreateTransferCommandHandler(
            _transactionRepository.Object,
            _financialAccountRepository.Object,
            _gamificationService.Object,
            _cache.Object,
            new CreateTransferCommandValidator());
    }

    private (Guid UserId, FinancialAccount From, FinancialAccount To) Arrange(
        long fromBalance = 5_000_000,
        long toBalance = 0)
    {
        var userId = Guid.NewGuid();
        var from = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = fromBalance };
        var to = new FinancialAccount { Id = Guid.NewGuid(), UserId = userId, BalanceCents = toBalance };

        _financialAccountRepository.Setup(r => r.GetByIdAsync(from.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(from);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(to.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(to);

        return (userId, from, to);
    }

    [Fact]
    public async Task HandleAsync_MovesMoneyBetweenBothAccounts()
    {
        var (userId, from, to) = Arrange(fromBalance: 5_000_000, toBalance: 200_000);

        await _handler.HandleAsync(new CreateTransferCommand(
            userId, from.Id, to.Id, 1_000_000, DateTimeOffset.UtcNow, "Rút ATM"));

        from.BalanceCents.Should().Be(4_000_000);
        to.BalanceCents.Should().Be(1_200_000);

        // Tổng tài sản không đổi — đó là toàn bộ điểm khác biệt của transfer so với debit.
        (from.BalanceCents + to.BalanceCents).Should().Be(5_200_000);
    }

    [Fact]
    public async Task HandleAsync_PersistsTransferShapeTheDbConstraintExpects()
    {
        var (userId, from, to) = Arrange();
        Transaction? saved = null;
        _transactionRepository
            .Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .Callback<Transaction, CancellationToken>((t, _) => saved = t);

        await _handler.HandleAsync(new CreateTransferCommand(
            userId, from.Id, to.Id, 750_000, DateTimeOffset.UtcNow, null));

        saved.Should().NotBeNull();
        saved!.TransactionType.Should().Be(TransactionType.Transfer);
        saved.FinancialAccountId.Should().Be(from.Id);
        saved.CounterAccountId.Should().Be(to.Id);

        // chk_transactions_transfer_shape buộc transfer không mang category.
        saved.CategoryId.Should().BeNull();
        saved.Status.Should().Be(TransactionStatus.Confirmed);

        // balance_after_cents luôn nói về ví nguồn, thống nhất với debit/credit.
        saved.BalanceAfterCents.Should().Be(from.BalanceCents);
    }

    [Fact]
    public async Task HandleAsync_SameAccountBothSides_Throws()
    {
        var (userId, from, _) = Arrange();

        var act = () => _handler.HandleAsync(new CreateTransferCommand(
            userId, from.Id, from.Id, 100_000, DateTimeOffset.UtcNow, null));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.ErrorCode.Should().Be(TransactionErrorCodes.TransferSameAccount);
    }

    [Fact]
    public async Task HandleAsync_DestinationBelongsToAnotherUser_ThrowsNotFound()
    {
        var (userId, from, _) = Arrange();
        var foreignAccountId = Guid.NewGuid();

        // Ví của user khác: repository lọc theo userId nên trả null, không phải trả ví đó.
        _financialAccountRepository.Setup(r => r.GetByIdAsync(foreignAccountId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinancialAccount?)null);

        var act = () => _handler.HandleAsync(new CreateTransferCommand(
            userId, from.Id, foreignAccountId, 100_000, DateTimeOffset.UtcNow, null));

        await act.Should().ThrowAsync<NotFoundException>();
        from.BalanceCents.Should().Be(5_000_000);
    }

    [Fact]
    public async Task HandleAsync_AwardsExpForRecordingTheTransfer()
    {
        var (userId, from, to) = Arrange();

        await _handler.HandleAsync(new CreateTransferCommand(
            userId, from.Id, to.Id, 300_000, DateTimeOffset.UtcNow, null));

        _gamificationService.Verify(g => g.RecordActivityAsync(
            It.Is<GamificationActivity>(a => a.UserId == userId), It.IsAny<CancellationToken>()),
            Times.Once);

        // Không assert "không tiêu ngân sách" ở đây được vì handler không hề nhận
        // IBudgetPeriodService — việc transfer không đụng budget do kiểu dữ liệu bảo đảm,
        // không phải do một nhánh if nào. Nhánh có thể sai thì nằm ở update/delete, test ở đó.
    }
}
