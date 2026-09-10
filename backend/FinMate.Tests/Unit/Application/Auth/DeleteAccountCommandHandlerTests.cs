using FinMate.Application.Auth.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Auth;

public class DeleteAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IDataDeletionRequestRepository> _dataDeletionRequestRepository = new();
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly DeleteAccountCommandHandler _handler;

    public DeleteAccountCommandHandlerTests()
    {
        _handler = new DeleteAccountCommandHandler(
            _userRepository.Object,
            _passwordHasher.Object,
            _refreshTokenRepository.Object,
            _dataDeletionRequestRepository.Object,
            _auditLogService.Object);
    }

    [Fact]
    public async Task HandleAsync_GoogleOnlyAccountHasNoPasswordHash_ThrowsInvalidCredentialsNotCrash()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "person@gmail.com",
            PasswordHash = null,
            GoogleId = "google-sub-1",
            DisplayName = "Person",
        };
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var act = () => _handler.HandleAsync(new DeleteAccountCommand(user.Id, "anything"));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.InvalidCredentials);
        _passwordHasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _userRepository.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
