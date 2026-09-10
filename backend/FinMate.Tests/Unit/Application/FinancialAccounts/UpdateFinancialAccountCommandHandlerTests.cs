using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinancialAccounts.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.FinancialAccounts;

public class UpdateFinancialAccountCommandHandlerTests
{
    private readonly Mock<IFinancialAccountRepository> _financialAccountRepository = new();
    private readonly UpdateFinancialAccountCommandHandler _handler;

    public UpdateFinancialAccountCommandHandlerTests()
    {
        _handler = new UpdateFinancialAccountCommandHandler(
            _financialAccountRepository.Object, new UpdateFinancialAccountCommandValidator());
    }

    private static FinancialAccount MakeAccount(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        AccountType = AccountType.Cash,
        AccountName = "Tên cũ",
        IsMonitored = true,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    [Fact]
    public async Task HandleAsync_AccountNotFound_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        _financialAccountRepository.Setup(r => r.GetByIdAsync(accountId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinancialAccount?)null);

        var act = () => _handler.HandleAsync(new UpdateFinancialAccountCommand(userId, accountId, "Tên mới"));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_EmptyAccountName_ThrowsValidationException()
    {
        var act = () => _handler.HandleAsync(
            new UpdateFinancialAccountCommand(Guid.NewGuid(), Guid.NewGuid(), ""));

        await act.Should().ThrowAsync<ValidationException>();
        _financialAccountRepository.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ExistingAccount_RenamesAndPersists()
    {
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        FinancialAccount? updated = null;
        _financialAccountRepository
            .Setup(r => r.UpdateAsync(It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()))
            .Callback<FinancialAccount, CancellationToken>((a, _) => updated = a)
            .Returns(Task.CompletedTask);

        var dto = await _handler.HandleAsync(new UpdateFinancialAccountCommand(userId, account.Id, "Tên mới"));

        updated.Should().NotBeNull();
        updated!.AccountName.Should().Be("Tên mới");
        dto.AccountName.Should().Be("Tên mới");
    }
}
