using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinancialAccounts.Queries;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.FinancialAccounts;

public class GetAccountListQueryHandlerTests
{
    private readonly Mock<IFinancialAccountRepository> _financialAccountRepository = new();
    private readonly GetAccountListQueryHandler _handler;

    public GetAccountListQueryHandlerTests()
    {
        _handler = new GetAccountListQueryHandler(_financialAccountRepository.Object);
    }

    [Fact]
    public async Task HandleAsync_OnlyQueriesAccountsForRequestedUser()
    {
        var userId = Guid.NewGuid();
        var otherUserAccount = new FinancialAccount
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            AccountType = AccountType.Cash,
            AccountName = "Của người khác",
        };
        _financialAccountRepository.Setup(r => r.GetListByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialAccount>());

        var result = await _handler.HandleAsync(new GetAccountListQuery(userId));

        result.Should().BeEmpty();
        _financialAccountRepository.Verify(r => r.GetListByUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _financialAccountRepository.Verify(
            r => r.GetListByUserAsync(otherUserAccount.UserId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MapsProviderDisplayNameFromNavigation()
    {
        var userId = Guid.NewGuid();
        var provider = new ProviderConfig { Id = Guid.NewGuid(), DisplayName = "MB Bank" };
        var account = new FinancialAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountType = AccountType.Bank,
            AccountName = "MB Bank chính",
            PackageName = "com.mbmobile",
            BalanceCents = 100_000,
            ProviderConfig = provider,
        };
        _financialAccountRepository.Setup(r => r.GetListByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialAccount> { account });

        var result = await _handler.HandleAsync(new GetAccountListQuery(userId));

        result.Should().ContainSingle();
        result[0].ProviderDisplayName.Should().Be("MB Bank");
        result[0].BalanceCents.Should().Be(100_000);
    }
}
