using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Community;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Community;

public class CommunitySharePromptServiceTests
{
    [Fact]
    public async Task WhenTheCacheIsDownNobodyIsPromptedAndNothingThrows()
    {
        var userId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = userId });
        var transactions = new Mock<ITransactionRepository>();
        transactions.Setup(r => r.GetByIdAsync(transactionId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(new Transaction
        {
            Id = transactionId,
            UserId = userId,
            AmountCents = 30_000,
            TransactionType = TransactionType.Debit,
            Status = TransactionStatus.Confirmed,
            MerchantName = "Quán cơm Bà Ba",
            TransactedAt = now,
        });
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(0, new SystemConfigDocument(), null));
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis down"));

        var service = new CommunitySharePromptService(transactions.Object, users.Object, Mock.Of<ICommunityRepository>(), config.Object, cache.Object);

        (await service.GetAsync(userId, transactionId)).ShouldPrompt.Should().BeFalse();
    }
}
