using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinancialAccounts.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.FinancialAccounts;

public class ToggleAccountMonitoringCommandHandlerTests
{
    private readonly Mock<IFinancialAccountRepository> _financialAccountRepository = new();
    private readonly ToggleAccountMonitoringCommandHandler _handler;

    public ToggleAccountMonitoringCommandHandlerTests()
    {
        _handler = new ToggleAccountMonitoringCommandHandler(_financialAccountRepository.Object);
    }

    [Fact]
    public async Task HandleAsync_AccountNotFound_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        _financialAccountRepository.Setup(r => r.GetByIdAsync(accountId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinancialAccount?)null);

        var act = () => _handler.HandleAsync(new ToggleAccountMonitoringCommand(userId, accountId, false));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_ExistingAccount_TogglesIsMonitoredAndPersists()
    {
        var userId = Guid.NewGuid();
        var account = new FinancialAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountType = AccountType.Cash,
            AccountName = "Ví tiền mặt",
            IsMonitored = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _financialAccountRepository.Setup(r => r.GetByIdAsync(account.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        FinancialAccount? updated = null;
        _financialAccountRepository
            .Setup(r => r.UpdateAsync(It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()))
            .Callback<FinancialAccount, CancellationToken>((a, _) => updated = a)
            .Returns(Task.CompletedTask);

        var dto = await _handler.HandleAsync(new ToggleAccountMonitoringCommand(userId, account.Id, false));

        updated.Should().NotBeNull();
        updated!.IsMonitored.Should().BeFalse();
        dto.IsMonitored.Should().BeFalse();
    }
}
