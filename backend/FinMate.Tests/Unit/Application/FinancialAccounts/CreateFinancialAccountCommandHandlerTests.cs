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

public class CreateFinancialAccountCommandHandlerTests
{
    private readonly Mock<IFinancialAccountRepository> _financialAccountRepository = new();
    private readonly Mock<IProviderConfigRepository> _providerConfigRepository = new();
    private readonly CreateFinancialAccountCommandHandler _handler;

    public CreateFinancialAccountCommandHandlerTests()
    {
        _handler = new CreateFinancialAccountCommandHandler(
            _financialAccountRepository.Object,
            _providerConfigRepository.Object,
            new CreateFinancialAccountCommandValidator());
    }

    private static ProviderConfig ActiveProvider(Guid id) => new()
    {
        Id = id,
        ProviderKey = "mb_bank",
        DisplayName = "MB Bank",
        PackageName = "com.mbmobile",
        AccountType = AccountType.Bank,
        IsActive = true,
    };

    [Fact]
    public async Task HandleAsync_DuplicatePackageNameForSameUser_ThrowsConflictException()
    {
        var providerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = ActiveProvider(providerId);

        _providerConfigRepository.Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);
        _financialAccountRepository
            .Setup(r => r.ExistsByUserAndPackageNameAsync(userId, provider.PackageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new CreateFinancialAccountCommand(userId, "MB Bank chính", AccountType.Bank, providerId, 0);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == FinancialAccountErrorCodes.PackageDuplicate);
        _financialAccountRepository.Verify(
            r => r.AddAsync(It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ProviderNotActive_ThrowsBusinessRuleException()
    {
        var providerId = Guid.NewGuid();
        var provider = ActiveProvider(providerId);
        provider.IsActive = false;

        _providerConfigRepository.Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        var command = new CreateFinancialAccountCommand(Guid.NewGuid(), "MB Bank chính", AccountType.Bank, providerId, 0);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.ErrorCode == FinancialAccountErrorCodes.ProviderInvalid);
    }

    [Fact]
    public async Task HandleAsync_ProviderNotFound_ThrowsBusinessRuleException()
    {
        var providerId = Guid.NewGuid();

        _providerConfigRepository.Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderConfig?)null);

        var command = new CreateFinancialAccountCommand(Guid.NewGuid(), "MB Bank chính", AccountType.Bank, providerId, 0);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.ErrorCode == FinancialAccountErrorCodes.ProviderInvalid);
    }

    [Fact]
    public async Task HandleAsync_CashAccount_DoesNotRequireProvider()
    {
        var userId = Guid.NewGuid();
        FinancialAccount? saved = null;
        _financialAccountRepository
            .Setup(r => r.AddAsync(It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()))
            .Callback<FinancialAccount, CancellationToken>((a, _) => saved = a)
            .Returns(Task.CompletedTask);

        var command = new CreateFinancialAccountCommand(userId, "Ví tiền mặt", AccountType.Cash, null, 500_000);

        var dto = await _handler.HandleAsync(command);

        saved.Should().NotBeNull();
        saved!.ProviderConfigId.Should().BeNull();
        saved.PackageName.Should().BeNull();
        saved.BalanceCents.Should().Be(500_000);
        dto.ProviderDisplayName.Should().BeNull();
        _providerConfigRepository.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidBankAccount_PersistsWithPackageNameFromProvider()
    {
        var userId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var provider = ActiveProvider(providerId);

        _providerConfigRepository.Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);
        _financialAccountRepository
            .Setup(r => r.ExistsByUserAndPackageNameAsync(userId, provider.PackageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        FinancialAccount? saved = null;
        _financialAccountRepository
            .Setup(r => r.AddAsync(It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()))
            .Callback<FinancialAccount, CancellationToken>((a, _) => saved = a)
            .Returns(Task.CompletedTask);

        var command = new CreateFinancialAccountCommand(userId, "MB Bank chính", AccountType.Bank, providerId, 0);

        var dto = await _handler.HandleAsync(command);

        saved.Should().NotBeNull();
        saved!.PackageName.Should().Be(provider.PackageName);
        dto.ProviderDisplayName.Should().Be(provider.DisplayName);
    }

    [Fact]
    public async Task HandleAsync_EmptyAccountName_ThrowsValidationExceptionAndDoesNotPersist()
    {
        var command = new CreateFinancialAccountCommand(Guid.NewGuid(), "", AccountType.Cash, null, 0);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
        _financialAccountRepository.Verify(
            r => r.AddAsync(It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NegativeInitialBalance_ThrowsValidationExceptionAndDoesNotPersist()
    {
        var command = new CreateFinancialAccountCommand(Guid.NewGuid(), "Ví âm", AccountType.Cash, null, -1);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
        _financialAccountRepository.Verify(
            r => r.AddAsync(It.IsAny<FinancialAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_CashAccountWithProviderConfigId_ThrowsValidationException()
    {
        var command = new CreateFinancialAccountCommand(Guid.NewGuid(), "Cash lỗi", AccountType.Cash, Guid.NewGuid(), 0);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task HandleAsync_BankAccountWithoutProviderConfigId_ThrowsValidationException()
    {
        var command = new CreateFinancialAccountCommand(Guid.NewGuid(), "Thiếu provider", AccountType.Bank, null, 0);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
